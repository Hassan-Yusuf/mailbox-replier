using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;

namespace EmailCopilot.Worker;

public static class ApiEndpoints
{
    public static void Register(WebApplication app)
    {
        var api = app.MapGroup("/api");

        api.MapGet("/drafts", async (string? status, int skip, int take, IDraftStore draftStore, CancellationToken cancellationToken) =>
        {
            if (skip < 0 || take < 0 || take > 200)
            {
                return Results.BadRequest(new { error = "Invalid skip/take values." });
            }

            var draftSets = await draftStore.GetDraftSetsAsync(status, skip, take, cancellationToken);
            return Results.Ok(draftSets.Select(draft => new DraftSetSummaryDto(
                draft.Id,
                draft.FromAddress,
                draft.Subject,
                draft.SourceReceivedAt,
                draft.DraftCreatedAt,
                draft.Status,
                draft.VariantCount,
                draft.TopConfidence,
                draft.Urgency,
                draft.AggregateConfidence,
                draft.Tier?.ToString())));
        });

        api.MapGet("/drafts/{id:long}", async (long id, IDraftStore draftStore, CancellationToken cancellationToken) =>
        {
            var draftSet = await draftStore.GetDraftSetByIdAsync(id, cancellationToken);
            if (draftSet is null)
            {
                return Results.NotFound();
            }

            return Results.Ok(new DraftSetDto(
                draftSet.Id,
                draftSet.FromAddress,
                draftSet.Subject,
                draftSet.SourceReceivedAt,
                draftSet.DraftCreatedAt,
                draftSet.Status,
                draftSet.Variants.Select(variant => new DraftVariantDto(
                    variant.Id,
                    variant.Shape,
                    variant.ShapeLabel,
                    variant.ConfidenceScore,
                    variant.Body,
                    variant.GroundingWarning,
                    variant.CoverageWarning,
                    variant.WasSelected,
                    variant.WasEdited,
                    variant.EditedBody,
                    variant.EditDistance,
                    variant.EditedAtUtc)).ToArray(),
                draftSet.SourceMessageId,
                draftSet.SelectedVariantId,
                draftSet.Analysis,
                draftSet.AggregateConfidence,
                draftSet.Tier?.ToString()));
        });

        api.MapGet("/drafts/{id:long}/originalEmail", async (long id, IDraftStore draftStore, CancellationToken cancellationToken) =>
        {
            var body = await draftStore.GetOriginalEmailBodyAsync(id, cancellationToken);
            return Results.Ok(new { body });
        });

        api.MapGet("/drafts/{id:long}/audit", async (long id, IDraftStore draftStore, CancellationToken cancellationToken) =>
        {
            var draftSet = await draftStore.GetDraftSetByIdAsync(id, cancellationToken);
            if (draftSet is null)
            {
                return Results.NotFound();
            }

            var events = await draftStore.GetAuditEventsAsync(id, cancellationToken);
            return Results.Ok(events.Select(e => new DraftAuditEventDto(
                e.Id,
                e.EventType,
                e.EventAtUtc,
                e.ActorUserId,
                e.PayloadJson)));
        });

        api.MapPost("/drafts/{id:long}/approve", async (long id, ApproveDraftRequest? request, IDraftStore draftStore, IOutlookDraftPusher draftPusher, IOptions<WorkflowOptions> workflowOptions, ILoggerFactory loggerFactory, CancellationToken cancellationToken) =>
        {
            if (request is null || request.VariantId <= 0)
            {
                return Results.BadRequest(new { error = "A valid variantId is required." });
            }

            var logger = loggerFactory.CreateLogger("DraftApprovalEndpoint");
            var workflowMode = WorkflowModes.Normalize(workflowOptions.Value.Mode);
            if (string.Equals(workflowMode, WorkflowModes.SuggestOnly, StringComparison.Ordinal))
            {
                logger.LogWarning(
                    "Workflow mode is SuggestOnly; approve accepted for draft {DraftSetId} but not recommended.",
                    id);
            }

            var draftSet = await draftStore.GetDraftSetByIdAsync(id, cancellationToken);
            if (draftSet is null)
            {
                return Results.NotFound();
            }

            if (!string.Equals(draftSet.Status, DraftSetStatuses.Pending, StringComparison.OrdinalIgnoreCase))
            {
                return Results.Conflict(new { error = "Draft set is not pending." });
            }

            var selectedVariant = draftSet.Variants.FirstOrDefault(variant => variant.Id == request.VariantId);
            if (selectedVariant is null)
            {
                return Results.BadRequest(new { error = "variantId does not belong to this draft set." });
            }

            var reviewedAt = DateTimeOffset.UtcNow;

            await draftStore.RecordVariantSelectionAsync(request.VariantId, cancellationToken);

            if (!string.IsNullOrEmpty(request.EditedBody) && request.EditedBody != selectedVariant.Body)
            {
                var distance = EditDistance.Levenshtein(selectedVariant.Body, request.EditedBody);
                await draftStore.RecordVariantEditAsync(
                    request.VariantId,
                    request.EditedBody,
                    distance,
                    reviewedAt,
                    cancellationToken);
            }

            await draftStore.UpdateDraftSetStatusAsync(
                id,
                DraftSetStatuses.Approved,
                request.VariantId,
                reviewedAt,
                null,
                false,
                false,
                false,
                cancellationToken);

            var approvalPayload = JsonSerializer.Serialize(new { variantId = request.VariantId });
            await draftStore.RecordAuditEventAsync(
                id,
                DraftAuditEventTypes.Approved,
                reviewedAt,
                null,
                approvalPayload,
                cancellationToken);

            try
            {
                await draftPusher.PushAsync(id, request.VariantId, cancellationToken);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Failed to push approved draft set {DraftSetId} to Outlook Drafts.", id);
                return Results.Json(
                    new { error = "Failed to push draft to Outlook.", detail = ex.Message },
                    statusCode: StatusCodes.Status502BadGateway);
            }

            var pushedAt = DateTimeOffset.UtcNow;
            await draftStore.UpdateDraftSetStatusAsync(
                id,
                DraftSetStatuses.PushedToOutlook,
                request.VariantId,
                reviewedAt,
                pushedAt,
                false,
                false,
                false,
                cancellationToken);

            await draftStore.RecordAuditEventAsync(
                id,
                DraftAuditEventTypes.Pushed,
                pushedAt,
                null,
                approvalPayload,
                cancellationToken);

            return Results.NoContent();
        });

        api.MapPost("/drafts/{id:long}/dismiss", async (long id, DismissDraftRequest? request, IDraftStore draftStore, CancellationToken cancellationToken) =>
        {
            var draftSet = await draftStore.GetDraftSetByIdAsync(id, cancellationToken);
            if (draftSet is null)
            {
                return Results.NotFound();
            }

            if (!string.Equals(draftSet.Status, DraftSetStatuses.Pending, StringComparison.OrdinalIgnoreCase))
            {
                return Results.Conflict(new { error = "Draft set is not pending." });
            }

            var dismissedAt = DateTimeOffset.UtcNow;

            await draftStore.UpdateDraftSetStatusAsync(
                id,
                DraftSetStatuses.Dismissed,
                null,
                dismissedAt,
                null,
                true,
                false,
                false,
                cancellationToken);

            await draftStore.RecordDismissalAsync(id, request?.Reason, dismissedAt, cancellationToken);

            var dismissPayload = string.IsNullOrWhiteSpace(request?.Reason)
                ? null
                : JsonSerializer.Serialize(new { reason = request!.Reason });
            await draftStore.RecordAuditEventAsync(
                id,
                DraftAuditEventTypes.Dismissed,
                dismissedAt,
                null,
                dismissPayload,
                cancellationToken);

            return Results.NoContent();
        });

        api.MapGet("/skipped", async (int skip, int take, IDraftStore draftStore, CancellationToken cancellationToken) =>
        {
            if (skip < 0 || take < 0 || take > 200)
            {
                return Results.BadRequest(new { error = "Invalid skip/take values." });
            }

            var skippedEmails = await draftStore.GetSkippedEmailsAsync(skip, take, cancellationToken);
            return Results.Ok(skippedEmails.Select(skipped => new SkippedEmailDto(
                skipped.Id,
                skipped.FromAddress,
                skipped.Subject,
                skipped.CreatedAtUtc,
                skipped.ReasonCode)));
        });

        api.MapGet("/runs", async (int skip, int take, IDraftStore draftStore, CancellationToken cancellationToken) =>
        {
            if (skip < 0 || take < 0 || take > 200)
            {
                return Results.BadRequest(new { error = "Invalid skip/take values." });
            }

            var runs = await draftStore.GetRunRecordsAsync(skip, take, cancellationToken);
            return Results.Ok(runs.Select(run => new RunRecordDto(
                run.Id,
                run.StartedAtUtc,
                run.CandidatesEvaluated,
                run.SkippedCount,
                run.DraftCreated,
                run.DraftId,
                new Dictionary<string, int>(run.SkipsByReasonCode, StringComparer.OrdinalIgnoreCase))));
        });

        api.MapGet("/policies/rules", async ([FromServices] BuiltInExclusionClassifier classifier, [FromServices] IRuleToggleStore toggleStore, CancellationToken cancellationToken) =>
        {
            var snapshot = await toggleStore.LoadSnapshotAsync(cancellationToken);
            var dtos = classifier.Rules
                .Select(rule => new PolicyRuleDto(
                    rule.RuleName,
                    HumanizeRuleName(rule.RuleName),
                    rule.Description,
                    rule.Category.ToString(),
                    DefaultEnabled: true,
                    IsUserConfigurable: IsUserConfigurable(rule.Category),
                    CurrentlyEnabled: snapshot.TryGetValue(rule.RuleName, out var enabled) ? enabled : true))
                .ToArray();
            return Results.Ok(dtos);
        });

        api.MapGet("/config/workflow", ([FromServices] IOptions<WorkflowOptions> workflowOptions) =>
        {
            var mode = WorkflowModes.Normalize(workflowOptions.Value.Mode);
            return Results.Ok(new WorkflowConfigDto(mode));
        });

        api.MapPost("/policies/rules/{ruleId}", async (string ruleId, SetPolicyRuleRequest? request, [FromServices] BuiltInExclusionClassifier classifier, [FromServices] IRuleToggleStore toggleStore, CancellationToken cancellationToken) =>
        {
            if (request is null)
            {
                return Results.BadRequest(new { error = "Request body is required." });
            }

            var rule = classifier.Rules.FirstOrDefault(r => string.Equals(r.RuleName, ruleId, StringComparison.Ordinal));
            if (rule is null)
            {
                return Results.NotFound(new { error = $"Unknown rule '{ruleId}'." });
            }

            if (!IsUserConfigurable(rule.Category))
            {
                return Results.Json(
                    new { error = "This rule is not user-configurable.", category = rule.Category.ToString() },
                    statusCode: StatusCodes.Status403Forbidden);
            }

            await toggleStore.SetAsync(ruleId, request.Enabled, cancellationToken);
            return Results.NoContent();
        });
    }

    private static bool IsUserConfigurable(RuleCategory category) =>
        category != RuleCategory.Suspicious && category != RuleCategory.AutomatedSender;

    private static string HumanizeRuleName(string ruleName)
    {
        var separatorIndex = ruleName.IndexOf(':');
        var suffix = separatorIndex >= 0 ? ruleName[(separatorIndex + 1)..] : ruleName;
        var words = suffix
            .Split('_', StringSplitOptions.RemoveEmptyEntries)
            .Select(word => word.Length switch
            {
                0 => string.Empty,
                1 => word.ToUpperInvariant(),
                _ => char.ToUpperInvariant(word[0]) + word[1..].ToLowerInvariant()
            });
        return string.Join(' ', words);
    }
}
