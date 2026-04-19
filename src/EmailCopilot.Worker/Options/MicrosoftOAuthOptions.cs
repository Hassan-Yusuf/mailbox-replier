namespace EmailCopilot.Worker;

public sealed class MicrosoftOAuthOptions
{
    public string ClientId { get; set; } = string.Empty;

    public string Tenant { get; set; } = "consumers";

    public string CacheFilePath { get; set; } = ".auth/msal-user-token-cache.bin";

    public string[] Scopes { get; set; } =
    [
        "https://outlook.office.com/IMAP.AccessAsUser.All",
        "offline_access"
    ];
}
