using Microsoft.Extensions.Options;

namespace EmailCopilot.Worker;

public sealed class Phase1ConfigurationValidator
{
    private const string MicrosoftOAuthDeviceCodeMode = "MicrosoftOAuthDeviceCode";
    private readonly ImapOptions _imapOptions;
    private readonly DatabaseOptions _databaseOptions;
    private readonly LlmOptions _llmOptions;
    private readonly MicrosoftOAuthOptions _microsoftOAuthOptions;
    private readonly StyleProfileOptions _styleProfileOptions;
    private readonly ReplyScopeOptions _replyScopeOptions;
    private readonly ExclusionRulesOptions _exclusionRulesOptions;
    private readonly ExclusionRulesValidator _exclusionRulesValidator;
    private readonly ILogger<Phase1ConfigurationValidator> _logger;

    public Phase1ConfigurationValidator(
        IOptions<ImapOptions> imapOptions,
        IOptions<DatabaseOptions> databaseOptions,
        IOptions<LlmOptions> llmOptions,
        IOptions<MicrosoftOAuthOptions> microsoftOAuthOptions,
        IOptions<StyleProfileOptions> styleProfileOptions,
        IOptions<ReplyScopeOptions> replyScopeOptions,
        IOptions<ExclusionRulesOptions> exclusionRulesOptions,
        ExclusionRulesValidator exclusionRulesValidator,
        ILogger<Phase1ConfigurationValidator> logger)
    {
        _imapOptions = imapOptions.Value;
        _databaseOptions = databaseOptions.Value;
        _llmOptions = llmOptions.Value;
        _microsoftOAuthOptions = microsoftOAuthOptions.Value;
        _styleProfileOptions = styleProfileOptions.Value;
        _replyScopeOptions = replyScopeOptions.Value;
        _exclusionRulesOptions = exclusionRulesOptions.Value;
        _exclusionRulesValidator = exclusionRulesValidator;
        _logger = logger;
    }

    public void Validate()
    {
        _logger.LogInformation("Validating Phase 1 configuration.");

        ValidateRequired(_imapOptions.Host, "Imap:Host");
        ValidatePositive(_imapOptions.Port, "Imap:Port");
        ValidateRequired(_imapOptions.Username, "Imap:Username");
        ValidateRequired(_databaseOptions.ConnectionString, "Database:ConnectionString");
        if (!_styleProfileOptions.BootstrapAllSentEmails)
        {
            ValidatePositive(_styleProfileOptions.BootstrapSentEmailCount, "StyleProfile:BootstrapSentEmailCount");
        }

        ValidatePositive(_styleProfileOptions.MinimumRelationshipSampleSize, "StyleProfile:MinimumRelationshipSampleSize");
        ValidatePositive(_styleProfileOptions.MinimumDomainSampleSize, "StyleProfile:MinimumDomainSampleSize");
        ValidateRequired(_styleProfileOptions.Greeting, "StyleProfile:Greeting");
        ValidateRequired(_styleProfileOptions.Closing, "StyleProfile:Closing");
        ValidateRequired(_styleProfileOptions.Tone, "StyleProfile:Tone");
        ValidateReplyScope();

        if (string.Equals(
            _imapOptions.AuthenticationMode,
            MicrosoftOAuthDeviceCodeMode,
            StringComparison.OrdinalIgnoreCase))
        {
            ValidateRequired(_microsoftOAuthOptions.ClientId, "MicrosoftOAuth:ClientId");

            if (_microsoftOAuthOptions.Scopes.Length == 0)
            {
                throw new InvalidOperationException("Configuration value 'MicrosoftOAuth:Scopes' must contain at least one scope.");
            }
        }
        else
        {
            ValidateRequired(_imapOptions.Password, "Imap:Password");
        }

        if (!_llmOptions.UseMock)
        {
            ValidateRequired(_llmOptions.BaseUrl, "Llm:BaseUrl");
            ValidateRequired(_llmOptions.ApiKey, "Llm:ApiKey");
            ValidateRequired(_llmOptions.Model, "Llm:Model");
        }

        var exclusionIssues = _exclusionRulesValidator.Validate(_exclusionRulesOptions.Rules);
        if (exclusionIssues.Count > 0)
        {
            throw new InvalidOperationException(
                "Configured exclusion rules are invalid: " + string.Join(" | ", exclusionIssues));
        }
    }

    private static void ValidateRequired(string value, string settingName)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new InvalidOperationException($"Configuration value '{settingName}' is required.");
        }
    }

    private static void ValidatePositive(int value, string settingName)
    {
        if (value <= 0)
        {
            throw new InvalidOperationException($"Configuration value '{settingName}' must be greater than zero.");
        }
    }

    private void ValidateReplyScope()
    {
        ValidateRequired(_replyScopeOptions.Mode, "ReplyScope:Mode");

        if (string.Equals(_replyScopeOptions.Mode, ReplyScopeModes.All, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        if (!string.Equals(_replyScopeOptions.Mode, ReplyScopeModes.OnlyAllowedDomains, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                $"Configuration value 'ReplyScope:Mode' must be '{ReplyScopeModes.All}' or '{ReplyScopeModes.OnlyAllowedDomains}'.");
        }

        if (_replyScopeOptions.AllowedDomains.Length == 0)
        {
            throw new InvalidOperationException(
                "Configuration value 'ReplyScope:AllowedDomains' must contain at least one domain when reply scope mode is OnlyAllowedDomains.");
        }

        foreach (var domain in _replyScopeOptions.AllowedDomains)
        {
            ValidateRequired(domain, "ReplyScope:AllowedDomains[]");
        }
    }
}
