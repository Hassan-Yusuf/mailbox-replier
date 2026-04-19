using EmailCopilot.Worker;
using Microsoft.Data.Sqlite;
using System.Text.Json;

var builder = WebApplication.CreateBuilder(new WebApplicationOptions
{
    Args = args,
    ContentRootPath = ResolveContentRoot()
});

builder.Configuration.AddJsonFile("exclusion-rules.json", optional: true, reloadOnChange: true);
builder.Logging.ClearProviders();
builder.Logging.AddConsole();
builder.Logging.AddDebug();

builder.Services.Configure<ImapOptions>(builder.Configuration.GetSection("Imap"));
builder.Services.Configure<DatabaseOptions>(builder.Configuration.GetSection("Database"));
builder.Services.PostConfigure<DatabaseOptions>(options =>
{
    options.ConnectionString = NormalizeSqliteConnectionString(
        options.ConnectionString,
        builder.Environment.ContentRootPath);
});
builder.Services.Configure<LlmOptions>(builder.Configuration.GetSection("Llm"));
builder.Services.Configure<MicrosoftOAuthOptions>(builder.Configuration.GetSection("MicrosoftOAuth"));
builder.Services.Configure<StyleProfileOptions>(builder.Configuration.GetSection("StyleProfile"));
builder.Services.Configure<ReplyScopeOptions>(builder.Configuration.GetSection("ReplyScope"));
builder.Services.Configure<WebUiOptions>(builder.Configuration.GetSection("WebUi"));
builder.Services.Configure<WorkerOptions>(builder.Configuration.GetSection("Worker"));
builder.Services.Configure<ExclusionRulesOptions>(builder.Configuration.GetSection("ExclusionRules"));
builder.Services.ConfigureHttpJsonOptions(options =>
{
    options.SerializerOptions.PropertyNamingPolicy = JsonNamingPolicy.CamelCase;
});
builder.Services.AddCors();

builder.Services.AddSingleton<MicrosoftOAuthTokenProvider>();
builder.Services.AddSingleton<ImapEmailReader>();
builder.Services.AddSingleton<IOutlookDraftPusher, OutlookDraftPusher>();
builder.Services.AddSingleton<IIncomingEmailReader>(serviceProvider => serviceProvider.GetRequiredService<ImapEmailReader>());
builder.Services.AddSingleton<ExclusionRulesValidator>();
builder.Services.AddSingleton<Phase1ConfigurationValidator>();
builder.Services.AddSingleton<BuiltInExclusionClassifier>();
builder.Services.AddSingleton<ConfiguredExclusionClassifier>();
builder.Services.AddSingleton<BuiltInExclusionStage>();
builder.Services.AddSingleton<ConfiguredExclusionStage>();
builder.Services.AddSingleton<IEmailClassifier, ClassificationPipeline>();
builder.Services.AddSingleton<IReplyScopeEvaluator, ReplyScopeEvaluator>();
builder.Services.AddSingleton<IEmailRequestAnalyzer, EmailRequestAnalyzer>();
builder.Services.AddSingleton<IDraftEligibilityAssessor, DraftEligibilityAssessor>();
builder.Services.AddSingleton<IReplyShapePlanner, ReplyShapePlanner>();
builder.Services.AddSingleton<GreetingPolicy>();
builder.Services.AddSingleton<IDraftGroundingChecker, DraftGroundingChecker>();
builder.Services.AddSingleton<StyleAuthoredBodyPipeline>();
builder.Services.AddSingleton<StyleSentenceFilterPipeline>();
builder.Services.AddSingleton<StaticStyleProfileProvider>();
builder.Services.AddSingleton<SqliteDraftStore>();
builder.Services.AddSingleton<IDraftStore>(serviceProvider => serviceProvider.GetRequiredService<SqliteDraftStore>());
builder.Services.AddSingleton<SqliteRunRecordStore>();
builder.Services.AddSingleton<IRunRecordStore>(serviceProvider => serviceProvider.GetRequiredService<SqliteRunRecordStore>());
builder.Services.AddSingleton<SqliteStyleProfileStore>();
builder.Services.AddSingleton<IStyleExampleStore, SqliteStyleExampleStore>();
builder.Services.AddSingleton<StyleExtractor>();
builder.Services.AddSingleton<StyleExampleExtractor>();
builder.Services.AddSingleton<StyleProfileService>();
builder.Services.AddSingleton<IStyleProfileSelector>(serviceProvider => serviceProvider.GetRequiredService<StyleProfileService>());
builder.Services.AddSingleton<ScanInboxUseCase>();
builder.Services.AddSingleton(_ => new HttpClient
{
    Timeout = TimeSpan.FromSeconds(45)
});
builder.Services.AddSingleton<LlmDraftGenerator>();
builder.Services.AddSingleton<IReplyDraftGenerator>(serviceProvider => serviceProvider.GetRequiredService<LlmDraftGenerator>());

var workerEnabled = builder.Configuration.GetValue("Worker:Enabled", true);
if (workerEnabled)
{
    builder.Services.AddHostedService<RunOnceWorker>();
}

var webUiEnabled = builder.Configuration.GetValue("WebUi:Enabled", false);
if (webUiEnabled)
{
    builder.WebHost.UseUrls("http://127.0.0.1:5000");
}

var app = builder.Build();

if (webUiEnabled)
{
    app.UseStaticFiles();

    if (app.Environment.IsDevelopment())
    {
        app.UseCors(policy => policy
            .WithOrigins("http://localhost:5173")
            .AllowAnyMethod()
            .AllowAnyHeader());
    }

    ApiEndpoints.Register(app);

    var indexPath = Path.Combine(app.Environment.WebRootPath ?? Path.Combine(app.Environment.ContentRootPath, "wwwroot"), "index.html");
    if (File.Exists(indexPath))
    {
        app.MapFallbackToFile("/{**path:regex(^(?!api).*$)}", "index.html");
    }
}

await app.RunAsync();

static string ResolveContentRoot()
{
    var candidates = new[]
    {
        AppContext.BaseDirectory,
        Directory.GetCurrentDirectory()
    };

    foreach (var candidate in candidates)
    {
        var resolved = FindNearestContentRoot(candidate);
        if (resolved is not null)
        {
            return resolved;
        }
    }

    return AppContext.BaseDirectory;
}

static string? FindNearestContentRoot(string startingPath)
{
    var directory = new DirectoryInfo(Path.GetFullPath(startingPath));

    while (directory is not null)
    {
        var appSettingsPath = Path.Combine(directory.FullName, "appsettings.json");
        var projectFilePath = Path.Combine(directory.FullName, "EmailCopilot.Worker.csproj");

        if (File.Exists(appSettingsPath) && File.Exists(projectFilePath))
        {
            return directory.FullName;
        }

        directory = directory.Parent;
    }

    return null;
}

static string NormalizeSqliteConnectionString(string connectionString, string contentRootPath)
{
    if (string.IsNullOrWhiteSpace(connectionString))
    {
        return connectionString;
    }

    var builder = new SqliteConnectionStringBuilder(connectionString);

    if (string.IsNullOrWhiteSpace(builder.DataSource) || Path.IsPathRooted(builder.DataSource))
    {
        return builder.ToString();
    }

    builder.DataSource = Path.GetFullPath(builder.DataSource, contentRootPath);
    return builder.ToString();
}
