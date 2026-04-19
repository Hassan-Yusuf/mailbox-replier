using Microsoft.Extensions.Options;

namespace EmailCopilot.Worker;

public sealed class ReplyScopeEvaluator : IReplyScopeEvaluator
{
    private readonly ReplyScopeOptions _options;
    private readonly HashSet<string> _allowedDomains;

    public ReplyScopeEvaluator(IOptions<ReplyScopeOptions> options)
    {
        _options = options.Value;
        _allowedDomains =
        [
            .. _options.AllowedDomains
                .Where(domain => !string.IsNullOrWhiteSpace(domain))
                .Select(NormalizeDomain)
        ];
    }

    public ReplyScopeDecision Evaluate(IncomingEmail email)
    {
        if (string.Equals(_options.Mode, ReplyScopeModes.All, StringComparison.OrdinalIgnoreCase))
        {
            return new ReplyScopeDecision(true, "reply scope allows all domains");
        }

        if (!string.Equals(_options.Mode, ReplyScopeModes.OnlyAllowedDomains, StringComparison.OrdinalIgnoreCase))
        {
            return new ReplyScopeDecision(true, $"reply scope mode '{_options.Mode}' is treated as allow-all");
        }

        var senderDomain = NormalizeDomain(email.From.Domain);

        if (string.IsNullOrWhiteSpace(senderDomain))
        {
            return new ReplyScopeDecision(false, "reply scope rejected sender with no domain");
        }

        var isAllowed = _allowedDomains.Any(allowedDomain =>
            senderDomain.Equals(allowedDomain, StringComparison.OrdinalIgnoreCase) ||
            senderDomain.EndsWith("." + allowedDomain, StringComparison.OrdinalIgnoreCase));

        return isAllowed
            ? new ReplyScopeDecision(true, $"reply scope allows sender domain {senderDomain}")
            : new ReplyScopeDecision(false, $"reply scope excludes sender domain {senderDomain}");
    }

    private static string NormalizeDomain(string domain) =>
        domain.Trim().TrimStart('@').ToLowerInvariant();
}
