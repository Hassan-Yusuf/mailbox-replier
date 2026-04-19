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
                draft.Urgency)));
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
                    variant.GroundingWarning)).ToArray(),
                draftSet.SourceMessageId,
                draftSet.SelectedVariantId,
                draftSet.Analysis));
        });

        api.MapGet("/drafts/{id:long}/originalEmail", async (long id, IDraftStore draftStore, CancellationToken cancellationToken) =>
        {
            var body = await draftStore.GetOriginalEmailBodyAsync(id, cancellationToken);
            return Results.Ok(new { body });
        });

        api.MapPost("/drafts/{id:long}/approve", async (long id, ApproveDraftRequest? request, IDraftStore draftStore, IOutlookDraftPusher draftPusher, ILoggerFactory loggerFactory, CancellationToken cancellationToken) =>
        {
            if (request is null || request.VariantId <= 0)
            {
                return Results.BadRequest(new { error = "A valid variantId is required." });
            }

            var logger = loggerFactory.CreateLogger("DraftApprovalEndpoint");
            var draftSet = await draftStore.GetDraftSetByIdAsync(id, cancellationToken);
            if (draftSet is null)
            {
                return Results.NotFound();
            }

            if (!string.Equals(draftSet.Status, DraftSetStatuses.Pending, StringComparison.OrdinalIgnoreCase))
            {
                return Results.Conflict(new { error = "Draft set is not pending." });
            }

            if (draftSet.Variants.All(variant => variant.Id != request.VariantId))
            {
                return Results.BadRequest(new { error = "variantId does not belong to this draft set." });
            }

            var reviewedAt = DateTimeOffset.UtcNow;
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

            await draftStore.UpdateDraftSetStatusAsync(
                id,
                DraftSetStatuses.PushedToOutlook,
                request.VariantId,
                reviewedAt,
                DateTimeOffset.UtcNow,
                false,
                false,
                false,
                cancellationToken);

            return Results.NoContent();
        });

        api.MapPost("/drafts/{id:long}/dismiss", async (long id, IDraftStore draftStore, CancellationToken cancellationToken) =>
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

            await draftStore.UpdateDraftSetStatusAsync(
                id,
                DraftSetStatuses.Dismissed,
                null,
                DateTimeOffset.UtcNow,
                null,
                true,
                false,
                false,
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
    }
}
