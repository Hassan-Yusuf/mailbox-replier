using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using System.Net;
using System.Net.Http.Json;
using System.Net.Sockets;
using System.Text.Json;

namespace EmailCopilot.Worker.Tests;

[TestFixture]
public sealed class ApiEndpointsTests
{
    [Test]
    public async Task Approve_should_return_conflict_when_status_is_not_pending()
    {
        var store = new FakeDraftStore();
        store.SetDraft(new DraftSetDetail(
            10,
            "sender@example.com",
            "Subject",
            DateTimeOffset.UtcNow,
            DateTimeOffset.UtcNow,
            DraftSetStatuses.Approved,
            "<message-10@example.com>",
            null,
            null,
            [new DraftVariantDetail(1001, ReplyShapes.GeneralReply, "General reply", 0.5, "Hello", null)],
            null,
            null));

        var pusher = new FakeOutlookDraftPusher();
        await using var harness = await ApiTestHarness.StartAsync(store, pusher);

        var response = await harness.Client.PostAsJsonAsync("/api/drafts/10/approve", new ApproveDraftRequest(1001));

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Conflict));
        Assert.That(pusher.Calls, Is.Empty);
        Assert.That(store.Updates, Is.Empty);
    }

    [Test]
    public async Task Approve_should_return_bad_request_when_variant_does_not_belong_to_draft()
    {
        var store = new FakeDraftStore();
        store.SetDraft(new DraftSetDetail(
            11,
            "sender@example.com",
            "Subject",
            DateTimeOffset.UtcNow,
            DateTimeOffset.UtcNow,
            DraftSetStatuses.Pending,
            "<message-11@example.com>",
            null,
            null,
            [new DraftVariantDetail(1002, ReplyShapes.GeneralReply, "General reply", 0.5, "Hello", null)],
            null,
            null));

        await using var harness = await ApiTestHarness.StartAsync(store, new FakeOutlookDraftPusher());

        var response = await harness.Client.PostAsJsonAsync("/api/drafts/11/approve", new ApproveDraftRequest(9999));

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest));
        Assert.That(store.Updates, Is.Empty);
    }

    [Test]
    public async Task Approve_should_return_bad_gateway_and_leave_draft_approved_when_pusher_fails()
    {
        var store = new FakeDraftStore();
        store.SetDraft(new DraftSetDetail(
            12,
            "sender@example.com",
            "Subject",
            DateTimeOffset.UtcNow,
            DateTimeOffset.UtcNow,
            DraftSetStatuses.Pending,
            "<message-12@example.com>",
            null,
            null,
            [new DraftVariantDetail(1003, ReplyShapes.GeneralReply, "General reply", 0.5, "Hello", null)],
            null,
            null));

        var pusher = new FakeOutlookDraftPusher
        {
            ExceptionToThrow = new InvalidOperationException("push failed")
        };

        await using var harness = await ApiTestHarness.StartAsync(store, pusher);

        var response = await harness.Client.PostAsJsonAsync("/api/drafts/12/approve", new ApproveDraftRequest(1003));
        var body = await response.Content.ReadAsStringAsync();

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.BadGateway));
        Assert.That(body, Does.Contain("Failed to push draft"));
        Assert.That(pusher.Calls, Has.Count.EqualTo(1));
        Assert.That(store.Updates, Has.Count.EqualTo(1));
        Assert.That(store.Updates[0].Status, Is.EqualTo(DraftSetStatuses.Approved));
        Assert.That(store.Updates[0].SelectedVariantId, Is.EqualTo(1003));
    }

    [Test]
    public async Task GetPolicyRules_returns_rule_metadata_with_currently_enabled_state()
    {
        var toggleStore = new InMemoryRuleToggleStore();
        toggleStore.Set("BUILT_IN:NO_REPLY_PLATFORM_SENDER", false);
        toggleStore.Set("BUILT_IN:SOCIAL_DIGEST", false);
        var classifier = new BuiltInExclusionClassifier(toggleStore);

        await using var harness = await ApiTestHarness.StartAsync(new FakeDraftStore(), new FakeOutlookDraftPusher(), toggleStore, classifier);

        var response = await harness.Client.GetAsync("/api/policies/rules");
        response.EnsureSuccessStatusCode();
        var rules = await response.Content.ReadFromJsonAsync<PolicyRuleDto[]>();

        Assert.That(rules, Is.Not.Null);
        Assert.That(rules!.Length, Is.EqualTo(classifier.Rules.Count));

        var noReply = rules.Single(rule => rule.RuleId == "BUILT_IN:NO_REPLY_PLATFORM_SENDER");
        Assert.That(noReply.DisplayName, Is.EqualTo("No Reply Platform Sender"));
        Assert.That(noReply.Category, Is.EqualTo("AutomatedSender"));
        Assert.That(noReply.IsUserConfigurable, Is.False, "AutomatedSender rules must be locked.");
        Assert.That(noReply.DefaultEnabled, Is.True);
        Assert.That(noReply.CurrentlyEnabled, Is.False);

        var social = rules.Single(rule => rule.RuleId == "BUILT_IN:SOCIAL_DIGEST");
        Assert.That(social.IsUserConfigurable, Is.True);
        Assert.That(social.CurrentlyEnabled, Is.False);

        var suspicious = rules.Single(rule => rule.Category == "Suspicious");
        Assert.That(suspicious.IsUserConfigurable, Is.False, "Suspicious rules must be locked.");
        Assert.That(suspicious.CurrentlyEnabled, Is.True);
    }

    [Test]
    public async Task SetPolicyRule_persists_disabled_toggle_for_user_configurable_rule()
    {
        var toggleStore = new InMemoryRuleToggleStore();
        var classifier = new BuiltInExclusionClassifier(toggleStore);

        await using var harness = await ApiTestHarness.StartAsync(new FakeDraftStore(), new FakeOutlookDraftPusher(), toggleStore, classifier);

        var setResponse = await harness.Client.PostAsJsonAsync(
            "/api/policies/rules/BUILT_IN:SOCIAL_DIGEST",
            new SetPolicyRuleRequest(false));

        Assert.That(setResponse.StatusCode, Is.EqualTo(HttpStatusCode.NoContent));

        var listResponse = await harness.Client.GetAsync("/api/policies/rules");
        var rules = await listResponse.Content.ReadFromJsonAsync<PolicyRuleDto[]>();
        var social = rules!.Single(rule => rule.RuleId == "BUILT_IN:SOCIAL_DIGEST");
        Assert.That(social.CurrentlyEnabled, Is.False);
    }

    [Test]
    public async Task SetPolicyRule_returns_forbidden_when_rule_is_not_user_configurable()
    {
        var toggleStore = new InMemoryRuleToggleStore();
        var classifier = new BuiltInExclusionClassifier(toggleStore);

        await using var harness = await ApiTestHarness.StartAsync(new FakeDraftStore(), new FakeOutlookDraftPusher(), toggleStore, classifier);

        var response = await harness.Client.PostAsJsonAsync(
            "/api/policies/rules/BUILT_IN:NO_REPLY_PLATFORM_SENDER",
            new SetPolicyRuleRequest(false));

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Forbidden));
        Assert.That(toggleStore.Snapshot, Is.Empty, "Toggle store must remain untouched for locked rules.");
    }

    [Test]
    public async Task SetPolicyRule_returns_not_found_for_unknown_rule_id()
    {
        var toggleStore = new InMemoryRuleToggleStore();
        var classifier = new BuiltInExclusionClassifier(toggleStore);

        await using var harness = await ApiTestHarness.StartAsync(new FakeDraftStore(), new FakeOutlookDraftPusher(), toggleStore, classifier);

        var response = await harness.Client.PostAsJsonAsync(
            "/api/policies/rules/BUILT_IN:DOES_NOT_EXIST",
            new SetPolicyRuleRequest(false));

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.NotFound));
        Assert.That(toggleStore.Snapshot, Is.Empty);
    }

    [Test]
    public async Task GetWorkflowConfig_returns_default_review_before_send()
    {
        await using var harness = await ApiTestHarness.StartAsync(new FakeDraftStore(), new FakeOutlookDraftPusher());

        var config = await harness.Client.GetFromJsonAsync<WorkflowConfigDto>("/api/config/workflow");

        Assert.That(config, Is.Not.Null);
        Assert.That(config!.Mode, Is.EqualTo(WorkflowModes.ReviewBeforeSend));
    }

    [Test]
    public async Task GetWorkflowConfig_returns_suggest_only_when_configured()
    {
        await using var harness = await ApiTestHarness.StartAsync(
            new FakeDraftStore(),
            new FakeOutlookDraftPusher(),
            toggleStore: null,
            classifier: null,
            workflow: new WorkflowOptions { Mode = WorkflowModes.SuggestOnly });

        var config = await harness.Client.GetFromJsonAsync<WorkflowConfigDto>("/api/config/workflow");

        Assert.That(config, Is.Not.Null);
        Assert.That(config!.Mode, Is.EqualTo(WorkflowModes.SuggestOnly));
    }

    [Test]
    public async Task Approve_under_suggest_only_still_succeeds_as_soft_gate()
    {
        var store = new FakeDraftStore();
        store.SetDraft(new DraftSetDetail(
            14,
            "sender@example.com",
            "Subject",
            DateTimeOffset.UtcNow,
            DateTimeOffset.UtcNow,
            DraftSetStatuses.Pending,
            "<message-14@example.com>",
            null,
            null,
            [new DraftVariantDetail(1005, ReplyShapes.GeneralReply, "General reply", 0.5, "Hello", null)],
            null,
            null));

        var pusher = new FakeOutlookDraftPusher();
        await using var harness = await ApiTestHarness.StartAsync(
            store,
            pusher,
            toggleStore: null,
            classifier: null,
            workflow: new WorkflowOptions { Mode = WorkflowModes.SuggestOnly });

        var response = await harness.Client.PostAsJsonAsync("/api/drafts/14/approve", new ApproveDraftRequest(1005));

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.NoContent));
        Assert.That(pusher.Calls, Has.Count.EqualTo(1));
    }

    [Test]
    public async Task Dismiss_should_return_conflict_when_called_twice()
    {
        var store = new FakeDraftStore();
        store.SetDraft(new DraftSetDetail(
            13,
            "sender@example.com",
            "Subject",
            DateTimeOffset.UtcNow,
            DateTimeOffset.UtcNow,
            DraftSetStatuses.Pending,
            "<message-13@example.com>",
            null,
            null,
            [new DraftVariantDetail(1004, ReplyShapes.GeneralReply, "General reply", 0.5, "Hello", null)],
            null,
            null));

        await using var harness = await ApiTestHarness.StartAsync(store, new FakeOutlookDraftPusher());

        var firstResponse = await harness.Client.PostAsync("/api/drafts/13/dismiss", content: null);
        var secondResponse = await harness.Client.PostAsync("/api/drafts/13/dismiss", content: null);

        Assert.That(firstResponse.StatusCode, Is.EqualTo(HttpStatusCode.NoContent));
        Assert.That(secondResponse.StatusCode, Is.EqualTo(HttpStatusCode.Conflict));
        Assert.That(store.Updates, Has.Count.EqualTo(1));
        Assert.That(store.Updates[0].Status, Is.EqualTo(DraftSetStatuses.Dismissed));
    }

    [Test]
    public async Task Approve_with_edited_body_persists_levenshtein_distance()
    {
        var store = new FakeDraftStore();
        store.SetDraft(new DraftSetDetail(
            20,
            "sender@example.com",
            "Subject",
            DateTimeOffset.UtcNow,
            DateTimeOffset.UtcNow,
            DraftSetStatuses.Pending,
            "<message-20@example.com>",
            null,
            null,
            [new DraftVariantDetail(2001, ReplyShapes.GeneralReply, "General reply", 0.5, "kitten", null)],
            null,
            null));

        await using var harness = await ApiTestHarness.StartAsync(store, new FakeOutlookDraftPusher());

        var response = await harness.Client.PostAsJsonAsync(
            "/api/drafts/20/approve",
            new ApproveDraftRequest(2001, "sitting"));

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.NoContent));
        Assert.That(store.VariantEdits, Has.Count.EqualTo(1));
        Assert.That(store.VariantEdits[0].VariantId, Is.EqualTo(2001));
        Assert.That(store.VariantEdits[0].EditedBody, Is.EqualTo("sitting"));
        Assert.That(store.VariantEdits[0].EditDistance, Is.EqualTo(3));
    }

    [Test]
    public async Task Approve_without_edited_body_does_not_record_variant_edit()
    {
        var store = new FakeDraftStore();
        store.SetDraft(new DraftSetDetail(
            21,
            "sender@example.com",
            "Subject",
            DateTimeOffset.UtcNow,
            DateTimeOffset.UtcNow,
            DraftSetStatuses.Pending,
            "<message-21@example.com>",
            null,
            null,
            [new DraftVariantDetail(2002, ReplyShapes.GeneralReply, "General reply", 0.5, "Hello", null)],
            null,
            null));

        await using var harness = await ApiTestHarness.StartAsync(store, new FakeOutlookDraftPusher());

        var response = await harness.Client.PostAsJsonAsync("/api/drafts/21/approve", new ApproveDraftRequest(2002));

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.NoContent));
        Assert.That(store.VariantEdits, Is.Empty);
    }

    [Test]
    public async Task Approve_with_edited_body_identical_to_original_does_not_record_edit()
    {
        var store = new FakeDraftStore();
        store.SetDraft(new DraftSetDetail(
            22,
            "sender@example.com",
            "Subject",
            DateTimeOffset.UtcNow,
            DateTimeOffset.UtcNow,
            DraftSetStatuses.Pending,
            "<message-22@example.com>",
            null,
            null,
            [new DraftVariantDetail(2003, ReplyShapes.GeneralReply, "General reply", 0.5, "same body", null)],
            null,
            null));

        await using var harness = await ApiTestHarness.StartAsync(store, new FakeOutlookDraftPusher());

        var response = await harness.Client.PostAsJsonAsync(
            "/api/drafts/22/approve",
            new ApproveDraftRequest(2003, "same body"));

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.NoContent));
        Assert.That(store.VariantEdits, Is.Empty);
    }

    [Test]
    public async Task Approve_without_edited_body_records_variant_selection()
    {
        var store = new FakeDraftStore();
        store.SetDraft(new DraftSetDetail(
            40,
            "sender@example.com",
            "Subject",
            DateTimeOffset.UtcNow,
            DateTimeOffset.UtcNow,
            DraftSetStatuses.Pending,
            "<message-40@example.com>",
            null,
            null,
            [new DraftVariantDetail(2010, ReplyShapes.GeneralReply, "General reply", 0.5, "Hello", null)],
            null,
            null));

        await using var harness = await ApiTestHarness.StartAsync(store, new FakeOutlookDraftPusher());

        var response = await harness.Client.PostAsJsonAsync(
            "/api/drafts/40/approve",
            new ApproveDraftRequest(2010));

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.NoContent));
        Assert.That(store.VariantSelections, Is.EqualTo(new[] { 2010L }));
        Assert.That(store.VariantEdits, Is.Empty);
    }

    [Test]
    public async Task Approve_with_edited_body_records_both_selection_and_edit()
    {
        var store = new FakeDraftStore();
        store.SetDraft(new DraftSetDetail(
            41,
            "sender@example.com",
            "Subject",
            DateTimeOffset.UtcNow,
            DateTimeOffset.UtcNow,
            DraftSetStatuses.Pending,
            "<message-41@example.com>",
            null,
            null,
            [new DraftVariantDetail(2011, ReplyShapes.GeneralReply, "General reply", 0.5, "kitten", null)],
            null,
            null));

        await using var harness = await ApiTestHarness.StartAsync(store, new FakeOutlookDraftPusher());

        var response = await harness.Client.PostAsJsonAsync(
            "/api/drafts/41/approve",
            new ApproveDraftRequest(2011, "sitting"));

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.NoContent));
        Assert.That(store.VariantSelections, Is.EqualTo(new[] { 2011L }));
        Assert.That(store.VariantEdits, Has.Count.EqualTo(1));
        Assert.That(store.VariantEdits[0].VariantId, Is.EqualTo(2011));
    }

    [Test]
    public async Task GetDraft_returns_edit_metadata_fields_on_variant_dto()
    {
        var editedAt = new DateTimeOffset(2026, 5, 8, 9, 30, 0, TimeSpan.Zero);
        var store = new FakeDraftStore();
        store.SetDraft(new DraftSetDetail(
            42,
            "sender@example.com",
            "Subject",
            DateTimeOffset.UtcNow,
            DateTimeOffset.UtcNow,
            DraftSetStatuses.PushedToOutlook,
            "<message-42@example.com>",
            null,
            null,
            [new DraftVariantDetail(
                2012,
                ReplyShapes.GeneralReply,
                "General reply",
                0.5,
                "kitten",
                null,
                WasSelected: true,
                WasEdited: true,
                EditedBody: "sitting",
                EditDistance: 3,
                EditedAtUtc: editedAt)],
            null,
            null));

        await using var harness = await ApiTestHarness.StartAsync(store, new FakeOutlookDraftPusher());

        var dto = await harness.Client.GetFromJsonAsync<DraftSetDto>("/api/drafts/42");

        Assert.That(dto, Is.Not.Null);
        Assert.That(dto!.Variants, Has.Count.EqualTo(1));
        var variant = dto.Variants[0];
        Assert.That(variant.WasSelected, Is.True);
        Assert.That(variant.WasEdited, Is.True);
        Assert.That(variant.EditedBody, Is.EqualTo("sitting"));
        Assert.That(variant.EditDistance, Is.EqualTo(3));
        Assert.That(variant.EditedAtUtc, Is.EqualTo(editedAt));
    }

    [Test]
    public async Task Dismiss_with_reason_persists_dismiss_reason()
    {
        var store = new FakeDraftStore();
        store.SetDraft(new DraftSetDetail(
            23,
            "sender@example.com",
            "Subject",
            DateTimeOffset.UtcNow,
            DateTimeOffset.UtcNow,
            DraftSetStatuses.Pending,
            "<message-23@example.com>",
            null,
            null,
            [new DraftVariantDetail(2004, ReplyShapes.GeneralReply, "General reply", 0.5, "Hello", null)],
            null,
            null));

        await using var harness = await ApiTestHarness.StartAsync(store, new FakeOutlookDraftPusher());

        var response = await harness.Client.PostAsJsonAsync(
            "/api/drafts/23/dismiss",
            new DismissDraftRequest("Wrong tone"));

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.NoContent));
        Assert.That(store.Dismissals, Has.Count.EqualTo(1));
        Assert.That(store.Dismissals[0].Id, Is.EqualTo(23));
        Assert.That(store.Dismissals[0].Reason, Is.EqualTo("Wrong tone"));
    }

    [Test]
    public async Task Approve_writes_approved_then_pushed_audit_events()
    {
        var store = new FakeDraftStore();
        store.SetDraft(new DraftSetDetail(
            30,
            "sender@example.com",
            "Subject",
            DateTimeOffset.UtcNow,
            DateTimeOffset.UtcNow,
            DraftSetStatuses.Pending,
            "<message-30@example.com>",
            null,
            null,
            [new DraftVariantDetail(3001, ReplyShapes.GeneralReply, "General reply", 0.5, "Hello", null)],
            null,
            null));

        await using var harness = await ApiTestHarness.StartAsync(store, new FakeOutlookDraftPusher());

        var response = await harness.Client.PostAsJsonAsync("/api/drafts/30/approve", new ApproveDraftRequest(3001));

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.NoContent));
        Assert.That(store.AuditEvents, Has.Count.EqualTo(2));
        Assert.That(store.AuditEvents[0].EventType, Is.EqualTo(DraftAuditEventTypes.Approved));
        Assert.That(store.AuditEvents[0].DraftSetId, Is.EqualTo(30));
        Assert.That(store.AuditEvents[0].PayloadJson, Does.Contain("3001"));
        Assert.That(store.AuditEvents[1].EventType, Is.EqualTo(DraftAuditEventTypes.Pushed));
        Assert.That(store.AuditEvents[1].EventAtUtc, Is.GreaterThanOrEqualTo(store.AuditEvents[0].EventAtUtc));
    }

    [Test]
    public async Task Approve_does_not_write_pushed_audit_event_when_pusher_fails()
    {
        var store = new FakeDraftStore();
        store.SetDraft(new DraftSetDetail(
            31,
            "sender@example.com",
            "Subject",
            DateTimeOffset.UtcNow,
            DateTimeOffset.UtcNow,
            DraftSetStatuses.Pending,
            "<message-31@example.com>",
            null,
            null,
            [new DraftVariantDetail(3002, ReplyShapes.GeneralReply, "General reply", 0.5, "Hello", null)],
            null,
            null));

        var pusher = new FakeOutlookDraftPusher
        {
            ExceptionToThrow = new InvalidOperationException("push failed")
        };
        await using var harness = await ApiTestHarness.StartAsync(store, pusher);

        var response = await harness.Client.PostAsJsonAsync("/api/drafts/31/approve", new ApproveDraftRequest(3002));

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.BadGateway));
        Assert.That(store.AuditEvents, Has.Count.EqualTo(1));
        Assert.That(store.AuditEvents[0].EventType, Is.EqualTo(DraftAuditEventTypes.Approved));
    }

    [Test]
    public async Task Dismiss_with_reason_writes_dismissed_audit_event_with_payload()
    {
        var store = new FakeDraftStore();
        store.SetDraft(new DraftSetDetail(
            32,
            "sender@example.com",
            "Subject",
            DateTimeOffset.UtcNow,
            DateTimeOffset.UtcNow,
            DraftSetStatuses.Pending,
            "<message-32@example.com>",
            null,
            null,
            [new DraftVariantDetail(3003, ReplyShapes.GeneralReply, "General reply", 0.5, "Hello", null)],
            null,
            null));

        await using var harness = await ApiTestHarness.StartAsync(store, new FakeOutlookDraftPusher());

        var response = await harness.Client.PostAsJsonAsync(
            "/api/drafts/32/dismiss",
            new DismissDraftRequest("Wrong tone"));

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.NoContent));
        Assert.That(store.AuditEvents, Has.Count.EqualTo(1));
        Assert.That(store.AuditEvents[0].EventType, Is.EqualTo(DraftAuditEventTypes.Dismissed));
        Assert.That(store.AuditEvents[0].PayloadJson, Does.Contain("Wrong tone"));
    }

    [Test]
    public async Task Dismiss_without_reason_writes_dismissed_audit_event_with_null_payload()
    {
        var store = new FakeDraftStore();
        store.SetDraft(new DraftSetDetail(
            33,
            "sender@example.com",
            "Subject",
            DateTimeOffset.UtcNow,
            DateTimeOffset.UtcNow,
            DraftSetStatuses.Pending,
            "<message-33@example.com>",
            null,
            null,
            [new DraftVariantDetail(3004, ReplyShapes.GeneralReply, "General reply", 0.5, "Hello", null)],
            null,
            null));

        await using var harness = await ApiTestHarness.StartAsync(store, new FakeOutlookDraftPusher());

        var response = await harness.Client.PostAsync("/api/drafts/33/dismiss", content: null);

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.NoContent));
        Assert.That(store.AuditEvents, Has.Count.EqualTo(1));
        Assert.That(store.AuditEvents[0].EventType, Is.EqualTo(DraftAuditEventTypes.Dismissed));
        Assert.That(store.AuditEvents[0].PayloadJson, Is.Null);
    }

    [Test]
    public async Task GetAuditEvents_returns_events_in_order_for_existing_draft()
    {
        var store = new FakeDraftStore();
        store.SetDraft(new DraftSetDetail(
            34,
            "sender@example.com",
            "Subject",
            DateTimeOffset.UtcNow,
            DateTimeOffset.UtcNow,
            DraftSetStatuses.Pending,
            "<message-34@example.com>",
            null,
            null,
            [new DraftVariantDetail(3005, ReplyShapes.GeneralReply, "General reply", 0.5, "Hello", null)],
            null,
            null));

        await using var harness = await ApiTestHarness.StartAsync(store, new FakeOutlookDraftPusher());

        var approveResponse = await harness.Client.PostAsJsonAsync(
            "/api/drafts/34/approve",
            new ApproveDraftRequest(3005));
        Assert.That(approveResponse.StatusCode, Is.EqualTo(HttpStatusCode.NoContent));

        var auditResponse = await harness.Client.GetAsync("/api/drafts/34/audit");
        auditResponse.EnsureSuccessStatusCode();
        var events = await auditResponse.Content.ReadFromJsonAsync<DraftAuditEventDto[]>();

        Assert.That(events, Is.Not.Null);
        Assert.That(events!.Length, Is.EqualTo(2));
        Assert.That(events[0].EventType, Is.EqualTo(DraftAuditEventTypes.Approved));
        Assert.That(events[1].EventType, Is.EqualTo(DraftAuditEventTypes.Pushed));
        Assert.That(events[1].EventAtUtc, Is.GreaterThanOrEqualTo(events[0].EventAtUtc));
    }

    [Test]
    public async Task GetAuditEvents_returns_not_found_for_unknown_draft()
    {
        await using var harness = await ApiTestHarness.StartAsync(new FakeDraftStore(), new FakeOutlookDraftPusher());

        var response = await harness.Client.GetAsync("/api/drafts/9999/audit");

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.NotFound));
    }

    [Test]
    public async Task Dismiss_without_body_persists_null_reason()
    {
        var store = new FakeDraftStore();
        store.SetDraft(new DraftSetDetail(
            24,
            "sender@example.com",
            "Subject",
            DateTimeOffset.UtcNow,
            DateTimeOffset.UtcNow,
            DraftSetStatuses.Pending,
            "<message-24@example.com>",
            null,
            null,
            [new DraftVariantDetail(2005, ReplyShapes.GeneralReply, "General reply", 0.5, "Hello", null)],
            null,
            null));

        await using var harness = await ApiTestHarness.StartAsync(store, new FakeOutlookDraftPusher());

        var response = await harness.Client.PostAsync("/api/drafts/24/dismiss", content: null);

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.NoContent));
        Assert.That(store.Dismissals, Has.Count.EqualTo(1));
        Assert.That(store.Dismissals[0].Reason, Is.Null);
    }

    private sealed class ApiTestHarness : IAsyncDisposable
    {
        private readonly WebApplication _app;

        private ApiTestHarness(WebApplication app, HttpClient client)
        {
            _app = app;
            Client = client;
        }

        public HttpClient Client { get; }

        public static Task<ApiTestHarness> StartAsync(FakeDraftStore store, FakeOutlookDraftPusher pusher) =>
            StartAsync(store, pusher, toggleStore: null, classifier: null);

        public static async Task<ApiTestHarness> StartAsync(
            FakeDraftStore store,
            FakeOutlookDraftPusher pusher,
            IRuleToggleStore? toggleStore,
            BuiltInExclusionClassifier? classifier,
            WorkflowOptions? workflow = null)
        {
            var port = GetFreePort();
            var builder = WebApplication.CreateSlimBuilder(new WebApplicationOptions
            {
                EnvironmentName = Environments.Development
            });

            builder.WebHost.UseUrls($"http://127.0.0.1:{port}");
            builder.Services.Configure<JsonOptions>(options =>
            {
                options.SerializerOptions.PropertyNamingPolicy = JsonNamingPolicy.CamelCase;
            });
            builder.Services.AddSingleton<IDraftStore>(store);
            builder.Services.AddSingleton<IOutlookDraftPusher>(pusher);

            if (toggleStore is not null)
            {
                builder.Services.AddSingleton(toggleStore);
            }

            if (classifier is not null)
            {
                builder.Services.AddSingleton(classifier);
            }

            if (workflow is not null)
            {
                builder.Services.Configure<WorkflowOptions>(options =>
                {
                    options.Mode = workflow.Mode;
                });
            }

            var app = builder.Build();
            ApiEndpoints.Register(app);
            await app.StartAsync();

            var client = new HttpClient
            {
                BaseAddress = new Uri($"http://127.0.0.1:{port}")
            };

            return new ApiTestHarness(app, client);
        }

        public async ValueTask DisposeAsync()
        {
            Client.Dispose();
            await _app.StopAsync();
            await _app.DisposeAsync();
        }

        private static int GetFreePort()
        {
            var listener = new TcpListener(IPAddress.Loopback, 0);
            listener.Start();
            var port = ((IPEndPoint)listener.LocalEndpoint).Port;
            listener.Stop();
            return port;
        }
    }

    private sealed class FakeOutlookDraftPusher : IOutlookDraftPusher
    {
        public List<(long DraftSetId, long SelectedVariantId)> Calls { get; } = [];

        public Exception? ExceptionToThrow { get; init; }

        public Task PushAsync(long draftSetId, long selectedVariantId, CancellationToken cancellationToken)
        {
            Calls.Add((draftSetId, selectedVariantId));

            if (ExceptionToThrow is not null)
            {
                throw ExceptionToThrow;
            }

            return Task.CompletedTask;
        }
    }

    private sealed class FakeDraftStore : IDraftStore
    {
        private readonly Dictionary<long, DraftSetDetail> _drafts = new();

        public List<UpdateCall> Updates { get; } = [];

        public void SetDraft(DraftSetDetail draft) => _drafts[draft.Id] = draft;

        public Task<long> InsertAsync(DraftSetRecord draftSet, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<long> InsertStrategyAsync(DraftStrategyRecord draftStrategy, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<long> InsertSkippedAsync(SkippedEmailRecord skippedEmail, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<IReadOnlyList<DraftSetSummary>> GetDraftSetsAsync(string? status, int skip, int take, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<DraftSetSummary>>([]);

        public Task<DraftSetDetail?> GetDraftSetByIdAsync(long id, CancellationToken cancellationToken)
        {
            _drafts.TryGetValue(id, out var draft);
            return Task.FromResult(draft);
        }

        public Task<string?> GetOriginalEmailBodyAsync(long id, CancellationToken cancellationToken) =>
            Task.FromResult<string?>(null);

        public Task<IReadOnlyList<SkippedEmailRecord>> GetSkippedEmailsAsync(int skip, int take, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<SkippedEmailRecord>>([]);

        public Task<IReadOnlyList<RunRecordListItem>> GetRunRecordsAsync(int skip, int take, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<RunRecordListItem>>([]);

        public Task UpdateDraftSetStatusAsync(
            long id,
            string status,
            long? selectedVariantId,
            DateTimeOffset? reviewedAt,
            DateTimeOffset? pushedAt,
            bool clearSelectedVariantId,
            bool clearReviewedAt,
            bool clearPushedAt,
            CancellationToken cancellationToken)
        {
            if (!_drafts.TryGetValue(id, out var draft))
            {
                throw new InvalidOperationException($"Unknown draft set id {id}.");
            }

            var updatedDraft = draft with
            {
                Status = status,
                SelectedVariantId = clearSelectedVariantId ? null : selectedVariantId ?? draft.SelectedVariantId
            };

            _drafts[id] = updatedDraft;
            Updates.Add(new UpdateCall(
                id,
                status,
                selectedVariantId,
                reviewedAt,
                pushedAt,
                clearSelectedVariantId,
                clearReviewedAt,
                clearPushedAt));

            return Task.CompletedTask;
        }

        public List<VariantEditCall> VariantEdits { get; } = [];

        public List<long> VariantSelections { get; } = [];

        public List<DismissalCall> Dismissals { get; } = [];

        public List<AuditEventCall> AuditEvents { get; } = [];

        public Task RecordVariantEditAsync(long variantId, string editedBody, int editDistance, DateTimeOffset editedAtUtc, CancellationToken cancellationToken)
        {
            VariantEdits.Add(new VariantEditCall(variantId, editedBody, editDistance, editedAtUtc));
            return Task.CompletedTask;
        }

        public Task RecordVariantSelectionAsync(long variantId, CancellationToken cancellationToken)
        {
            VariantSelections.Add(variantId);
            return Task.CompletedTask;
        }

        public Task RecordDismissalAsync(long id, string? reason, DateTimeOffset dismissedAtUtc, CancellationToken cancellationToken)
        {
            Dismissals.Add(new DismissalCall(id, reason, dismissedAtUtc));
            return Task.CompletedTask;
        }

        public Task RecordAuditEventAsync(long draftSetId, string eventType, DateTimeOffset eventAtUtc, string? actorUserId, string? payloadJson, CancellationToken cancellationToken)
        {
            AuditEvents.Add(new AuditEventCall(draftSetId, eventType, eventAtUtc, actorUserId, payloadJson));
            return Task.CompletedTask;
        }

        public Task<IReadOnlyList<DraftAuditEventRecord>> GetAuditEventsAsync(long draftSetId, CancellationToken cancellationToken)
        {
            var matches = AuditEvents
                .Where(call => call.DraftSetId == draftSetId)
                .Select((call, index) => new DraftAuditEventRecord(
                    index + 1,
                    call.DraftSetId,
                    call.EventType,
                    call.EventAtUtc,
                    call.ActorUserId,
                    call.PayloadJson))
                .ToArray();
            return Task.FromResult<IReadOnlyList<DraftAuditEventRecord>>(matches);
        }
    }

    private sealed class InMemoryRuleToggleStore : IRuleToggleStore
    {
        private readonly Dictionary<string, bool> _toggles = new(StringComparer.Ordinal);

        public IReadOnlyDictionary<string, bool> Snapshot => _toggles;

        public void Set(string ruleName, bool isEnabled) => _toggles[ruleName] = isEnabled;

        public Task InitializeAsync(CancellationToken cancellationToken) => Task.CompletedTask;

        public Task<IReadOnlyDictionary<string, bool>> LoadSnapshotAsync(CancellationToken cancellationToken)
        {
            IReadOnlyDictionary<string, bool> snapshot = new Dictionary<string, bool>(_toggles, StringComparer.Ordinal);
            return Task.FromResult(snapshot);
        }

        public Task SetAsync(string ruleName, bool isEnabled, CancellationToken cancellationToken)
        {
            _toggles[ruleName] = isEnabled;
            return Task.CompletedTask;
        }
    }

    private sealed record UpdateCall(
        long Id,
        string Status,
        long? SelectedVariantId,
        DateTimeOffset? ReviewedAt,
        DateTimeOffset? PushedAt,
        bool ClearSelectedVariantId,
        bool ClearReviewedAt,
        bool ClearPushedAt);

    private sealed record VariantEditCall(
        long VariantId,
        string EditedBody,
        int EditDistance,
        DateTimeOffset EditedAtUtc);

    private sealed record DismissalCall(
        long Id,
        string? Reason,
        DateTimeOffset DismissedAtUtc);

    private sealed record AuditEventCall(
        long DraftSetId,
        string EventType,
        DateTimeOffset EventAtUtc,
        string? ActorUserId,
        string? PayloadJson);
}
