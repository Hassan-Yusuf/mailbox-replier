using EmailCopilot.Worker;
using EmailCopilot.Worker.Diagnostics;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Options;
using System.Text.Json;

if (args.Contains("--diagnose-avoid-filter"))
{
    var diagnoseConfig = new ConfigurationBuilder()
        .SetBasePath(ResolveContentRoot())
        .AddJsonFile("appsettings.json", optional: false)
        .AddJsonFile("appsettings.Development.json", optional: true)
        .AddUserSecrets<Program>(optional: true)
        .Build();
    var apiKey = diagnoseConfig["Embedding:ApiKey"] ?? diagnoseConfig["Llm:ApiKey"] ?? string.Empty;
    var model = diagnoseConfig["Embedding:Model"] ?? "text-embedding-3-small";
    return await TestAvoidFilter.RunAsync(apiKey, model);
}

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
builder.Services.Configure<EmbeddingOptions>(builder.Configuration.GetSection("Embedding"));
builder.Services.Configure<MicrosoftOAuthOptions>(builder.Configuration.GetSection("MicrosoftOAuth"));
builder.Services.Configure<StyleProfileOptions>(builder.Configuration.GetSection("StyleProfile"));
builder.Services.Configure<ReplyScopeOptions>(builder.Configuration.GetSection("ReplyScope"));
builder.Services.Configure<WorkflowOptions>(builder.Configuration.GetSection("Workflow"));
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
builder.Services.AddSingleton<SqliteRuleToggleStore>();
builder.Services.AddSingleton<IRuleToggleStore>(serviceProvider => serviceProvider.GetRequiredService<SqliteRuleToggleStore>());
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
builder.Services.AddSingleton<IEmbeddingClient, OpenAIEmbeddingClient>();
builder.Services.AddSingleton<ICoverageVerifier, CoverageVerifier>();
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
builder.Services.AddSingleton<AvoidPhraseEmbeddings>(serviceProvider =>
{
    var options = serviceProvider.GetRequiredService<IOptions<LlmOptions>>().Value;
    if (!options.AvoidFilter.Enabled)
    {
        return new AvoidPhraseEmbeddings(Array.Empty<AvoidPhraseCentroid>());
    }

    var embeddingClient = serviceProvider.GetRequiredService<IEmbeddingClient>();
    return AvoidPhraseEmbeddings.BuildAsync(embeddingClient, CancellationToken.None).GetAwaiter().GetResult();
});
builder.Services.AddSingleton<AvoidPhraseEmbeddingFilter>();
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
return 0;

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
