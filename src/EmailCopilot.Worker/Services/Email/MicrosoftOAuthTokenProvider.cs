using Microsoft.Extensions.Options;
using Microsoft.Identity.Client;

namespace EmailCopilot.Worker;

public sealed class MicrosoftOAuthTokenProvider
{
    private readonly object _tokenCacheLock = new();
    private readonly MicrosoftOAuthOptions _options;
    private readonly IPublicClientApplication _publicClientApplication;
    private readonly ILogger<MicrosoftOAuthTokenProvider> _logger;

    public MicrosoftOAuthTokenProvider(
        IOptions<MicrosoftOAuthOptions> options,
        ILogger<MicrosoftOAuthTokenProvider> logger)
    {
        _options = options.Value;
        _logger = logger;

        var authority = BuildAuthority(_options.Tenant);
        _publicClientApplication = PublicClientApplicationBuilder
            .Create(_options.ClientId)
            .WithAuthority(authority)
            .WithDefaultRedirectUri()
            .Build();

        ConfigurePersistentTokenCache(_publicClientApplication.UserTokenCache);
    }

    public async Task<string> GetAccessTokenAsync(string username, CancellationToken cancellationToken)
    {
        var accounts = await _publicClientApplication.GetAccountsAsync().ConfigureAwait(false);
        _logger.LogInformation(
            "Microsoft OAuth cache currently has {AccountCount} account(s): {AccountUsernames}.",
            accounts.Count(),
            accounts.Any()
                ? string.Join(", ", accounts.Select(account => account.Username))
                : "(none)");

        var account = accounts.FirstOrDefault(candidate =>
            string.Equals(candidate.Username, username, StringComparison.OrdinalIgnoreCase));

        try
        {
            if (account is not null)
            {
                _logger.LogInformation(
                    "Attempting to acquire a cached Microsoft OAuth token for {Username}.",
                    username);

                var silentResult = await _publicClientApplication
                    .AcquireTokenSilent(_options.Scopes, account)
                    .ExecuteAsync(cancellationToken)
                    .ConfigureAwait(false);

                LogTokenDiagnostics(username, silentResult);
                return silentResult.AccessToken;
            }
        }
        catch (MsalUiRequiredException)
        {
            _logger.LogInformation(
                "No reusable cached Microsoft OAuth token was available for {Username}. Falling back to device code flow.",
                username);
        }

        _logger.LogWarning(
            "Starting Microsoft device code authentication for {Username}. Follow the sign-in instructions below.",
            username);

        var interactiveResult = await _publicClientApplication
            .AcquireTokenWithDeviceCode(
                _options.Scopes,
                deviceCodeResult =>
                {
                    _logger.LogWarning("{DeviceCodeMessage}", deviceCodeResult.Message);
                    return Task.CompletedTask;
                })
            .ExecuteAsync(cancellationToken)
            .ConfigureAwait(false);

        LogTokenDiagnostics(username, interactiveResult);
        return interactiveResult.AccessToken;
    }

    private static string BuildAuthority(string tenant)
    {
        var trimmedTenant = string.IsNullOrWhiteSpace(tenant) ? "consumers" : tenant.Trim();
        return $"https://login.microsoftonline.com/{trimmedTenant}/";
    }

    private void LogTokenDiagnostics(string configuredUsername, AuthenticationResult result)
    {
        var diagnostics = MicrosoftAccessTokenDiagnostics.Parse(result.AccessToken);

        _logger.LogInformation(
            "Microsoft OAuth token acquired. ConfiguredUsername={ConfiguredUsername}; AccountUsername={AccountUsername}; Audience={Audience}; Scope={Scope}; PreferredUsername={PreferredUsername}; TenantId={TenantId}; ExpiresAtUtc={ExpiresAtUtc}.",
            configuredUsername,
            result.Account?.Username ?? "(none)",
            string.IsNullOrWhiteSpace(diagnostics.Audience) ? "(unknown)" : diagnostics.Audience,
            string.IsNullOrWhiteSpace(diagnostics.Scope) ? "(unknown)" : diagnostics.Scope,
            string.IsNullOrWhiteSpace(diagnostics.PreferredUsername) ? "(unknown)" : diagnostics.PreferredUsername,
            string.IsNullOrWhiteSpace(diagnostics.TenantId) ? "(unknown)" : diagnostics.TenantId,
            diagnostics.ExpiresAtUtc?.ToString("O") ?? "(unknown)");

        var effectiveUsername = result.Account?.Username;
        if (string.IsNullOrWhiteSpace(effectiveUsername))
        {
            effectiveUsername = diagnostics.PreferredUsername;
        }

        if (!string.IsNullOrWhiteSpace(effectiveUsername) &&
            !string.Equals(effectiveUsername, configuredUsername, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                $"Microsoft OAuth returned a token for '{effectiveUsername}', but the IMAP configuration is set to '{configuredUsername}'. Sign in with the configured mailbox account.");
        }
    }

    private void ConfigurePersistentTokenCache(ITokenCache tokenCache)
    {
        var cacheFilePath = ResolvePath(_options.CacheFilePath);
        EnsureParentDirectoryExists(cacheFilePath);

        tokenCache.SetBeforeAccess(args =>
        {
            lock (_tokenCacheLock)
            {
                if (!File.Exists(cacheFilePath))
                {
                    return;
                }

                var cacheBytes = File.ReadAllBytes(cacheFilePath);
                args.TokenCache.DeserializeMsalV3(cacheBytes, shouldClearExistingCache: true);
            }
        });

        tokenCache.SetAfterAccess(args =>
        {
            if (!args.HasStateChanged)
            {
                return;
            }

            lock (_tokenCacheLock)
            {
                var cacheBytes = args.TokenCache.SerializeMsalV3();
                File.WriteAllBytes(cacheFilePath, cacheBytes);
            }

            _logger.LogInformation("Persisted the Microsoft OAuth token cache to {CacheFilePath}.", cacheFilePath);
        });
    }

    private static string ResolvePath(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            throw new InvalidOperationException("Microsoft OAuth cache path must not be empty.");
        }

        return Path.GetFullPath(path);
    }

    private static void EnsureParentDirectoryExists(string filePath)
    {
        var directory = Path.GetDirectoryName(filePath);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }
    }
}
