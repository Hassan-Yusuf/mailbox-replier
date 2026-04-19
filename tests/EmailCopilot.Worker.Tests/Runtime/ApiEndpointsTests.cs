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
            [new DraftVariantDetail(1001, ReplyShapes.GeneralReply, "General reply", 0.5, "Hello", null)]));

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
            [new DraftVariantDetail(1002, ReplyShapes.GeneralReply, "General reply", 0.5, "Hello", null)]));

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
            [new DraftVariantDetail(1003, ReplyShapes.GeneralReply, "General reply", 0.5, "Hello", null)]));

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
            [new DraftVariantDetail(1004, ReplyShapes.GeneralReply, "General reply", 0.5, "Hello", null)]));

        await using var harness = await ApiTestHarness.StartAsync(store, new FakeOutlookDraftPusher());

        var firstResponse = await harness.Client.PostAsync("/api/drafts/13/dismiss", content: null);
        var secondResponse = await harness.Client.PostAsync("/api/drafts/13/dismiss", content: null);

        Assert.That(firstResponse.StatusCode, Is.EqualTo(HttpStatusCode.NoContent));
        Assert.That(secondResponse.StatusCode, Is.EqualTo(HttpStatusCode.Conflict));
        Assert.That(store.Updates, Has.Count.EqualTo(1));
        Assert.That(store.Updates[0].Status, Is.EqualTo(DraftSetStatuses.Dismissed));
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

        public static async Task<ApiTestHarness> StartAsync(FakeDraftStore store, FakeOutlookDraftPusher pusher)
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
}
