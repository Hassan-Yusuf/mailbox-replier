using Microsoft.Extensions.Options;

namespace EmailCopilot.Worker;

public sealed class StyleProfileService : IStyleProfileSelector
{
    private const string GlobalDefaultSegment = "global-default";
    private const string PersonalSegment = "relationship-personal";
    private const string ProfessionalSegment = "relationship-professional";

    private readonly ImapEmailReader _imapEmailReader;
    private readonly StaticStyleProfileProvider _fallbackProfileProvider;
    private readonly StyleExtractor _styleExtractor;
    private readonly SqliteStyleProfileStore _styleProfileStore;
    private readonly IStyleExampleStore _styleExampleStore;
    private readonly StyleExampleExtractor _styleExampleExtractor;
    private readonly StyleProfileOptions _options;
    private readonly ILogger<StyleProfileService> _logger;

    public StyleProfileService(
        ImapEmailReader imapEmailReader,
        StaticStyleProfileProvider fallbackProfileProvider,
        StyleExtractor styleExtractor,
        SqliteStyleProfileStore styleProfileStore,
        IStyleExampleStore styleExampleStore,
        StyleExampleExtractor styleExampleExtractor,
        IOptions<StyleProfileOptions> options,
        ILogger<StyleProfileService> logger)
    {
        _imapEmailReader = imapEmailReader;
        _fallbackProfileProvider = fallbackProfileProvider;
        _styleExtractor = styleExtractor;
        _styleProfileStore = styleProfileStore;
        _styleExampleStore = styleExampleStore;
        _styleExampleExtractor = styleExampleExtractor;
        _options = options.Value;
        _logger = logger;
    }

    public async Task<StyleProfileSelection> GetOrBuildProfileSelectionAsync(
        IncomingEmail email,
        CancellationToken cancellationToken)
    {
        var profiles = await GetOrBuildProfilesAsync(cancellationToken);
        return SelectProfile(email.From.Address, profiles);
    }

    private async Task<IReadOnlyDictionary<string, StyleProfile>> GetOrBuildProfilesAsync(
        CancellationToken cancellationToken)
    {
        var existingProfiles = await _styleProfileStore.GetAllAsync(cancellationToken);
        if (existingProfiles.Count > 0 &&
            !_options.RebuildOnStartup &&
            !LooksContaminated(existingProfiles))
        {
            _logger.LogInformation(
                "Loaded {ProfileCount} persisted style profile segment(s).",
                existingProfiles.Count);

            return existingProfiles;
        }

        var fallbackProfile = _fallbackProfileProvider.GetFallbackProfile();

        if (existingProfiles.Count > 0)
        {
            var reason = _options.RebuildOnStartup
                ? "StyleProfile:RebuildOnStartup is enabled"
                : "one or more persisted style profiles appear contaminated";

            _logger.LogInformation(
                "Rebuilding persisted style profiles because {Reason}.",
                reason);
        }

        try
        {
            _logger.LogInformation(
                _options.BootstrapAllSentEmails
                    ? "No usable persisted style profiles found. Bootstrapping from the full sent mailbox."
                    : "No usable persisted style profiles found. Bootstrapping from up to {BootstrapSentEmailCount} sent email(s).",
                _options.BootstrapSentEmailCount);

            var sentEmails = await _imapEmailReader.GetSentEmailsAsync(
                _options.BootstrapAllSentEmails,
                _options.BootstrapSentEmailCount,
                cancellationToken);

            if (sentEmails.Count == 0)
            {
                _logger.LogWarning(
                    "Sent-mail bootstrap returned no usable samples. Falling back to the configured default style profile.");

                return new Dictionary<string, StyleProfile>(StringComparer.OrdinalIgnoreCase)
                {
                    [fallbackProfile.SegmentKey] = fallbackProfile
                };
            }

            var buildResult = BuildProfiles(sentEmails, fallbackProfile);

            foreach (var segmentSamples in buildResult.SamplesBySegment)
            {
                var examples = await _styleExampleExtractor.ExtractAsync(
                    segmentSamples.Key,
                    segmentSamples.Value,
                    cancellationToken);

                await _styleExampleStore.ReplaceBySegmentAsync(
                    segmentSamples.Key,
                    examples,
                    cancellationToken);
            }

            await _styleProfileStore.ReplaceAllAsync(buildResult.Profiles.Values, cancellationToken);

            _logger.LogInformation(
                "Built and persisted {ProfileCount} style profile segment(s) from {SampleCount} sent email(s).",
                buildResult.Profiles.Count,
                sentEmails.Count);

            return buildResult.Profiles;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(
                ex,
                "Sent-mail bootstrap failed. Falling back to the configured default style profile for this run.");

            return new Dictionary<string, StyleProfile>(StringComparer.OrdinalIgnoreCase)
            {
                [fallbackProfile.SegmentKey] = fallbackProfile
            };
        }
    }

    private BuildProfilesResult BuildProfiles(
        IReadOnlyList<SentEmailSample> sentEmails,
        StyleProfile fallbackProfile)
    {
        var profiles = new Dictionary<string, StyleProfile>(StringComparer.OrdinalIgnoreCase);
        var samplesBySegment = new Dictionary<string, IReadOnlyList<SentEmailSample>>(StringComparer.OrdinalIgnoreCase);

        var globalProfile = _styleExtractor.BuildProfile(GlobalDefaultSegment, sentEmails, fallbackProfile);
        profiles[globalProfile.SegmentKey] = globalProfile;
        samplesBySegment[globalProfile.SegmentKey] = sentEmails;

        var personalEmails = sentEmails
            .Where(email => IsPersonalDomain(email.RecipientDomain))
            .ToList();

        if (personalEmails.Count >= _options.MinimumRelationshipSampleSize)
        {
            var personalProfile = _styleExtractor.BuildProfile(PersonalSegment, personalEmails, globalProfile);
            profiles[personalProfile.SegmentKey] = personalProfile;
            samplesBySegment[personalProfile.SegmentKey] = personalEmails;
        }

        var professionalEmails = sentEmails
            .Where(email => !string.IsNullOrWhiteSpace(email.RecipientDomain) && !IsPersonalDomain(email.RecipientDomain))
            .ToList();

        if (professionalEmails.Count >= _options.MinimumRelationshipSampleSize)
        {
            var professionalProfile = _styleExtractor.BuildProfile(ProfessionalSegment, professionalEmails, globalProfile);
            profiles[professionalProfile.SegmentKey] = professionalProfile;
            samplesBySegment[professionalProfile.SegmentKey] = professionalEmails;
        }

        var domainGroups = professionalEmails
            .Where(email => !string.IsNullOrWhiteSpace(email.RecipientDomain))
            .GroupBy(email => email.RecipientDomain, StringComparer.OrdinalIgnoreCase)
            .Where(group => group.Count() >= _options.MinimumDomainSampleSize)
            .OrderBy(group => group.Key, StringComparer.OrdinalIgnoreCase);

        foreach (var domainGroup in domainGroups)
        {
            var segmentKey = BuildDomainSegmentKey(domainGroup.Key);
            var domainEmails = domainGroup.ToList();
            var domainProfile = _styleExtractor.BuildProfile(
                segmentKey,
                domainEmails,
                profiles.TryGetValue(ProfessionalSegment, out var professionalProfile)
                    ? professionalProfile
                    : globalProfile);

            profiles[segmentKey] = domainProfile;
            samplesBySegment[segmentKey] = domainEmails;
        }

        return new BuildProfilesResult(profiles, samplesBySegment);
    }

    private StyleProfileSelection SelectProfile(
        string senderAddress,
        IReadOnlyDictionary<string, StyleProfile> profiles)
    {
        var senderDomain = ExtractDomain(senderAddress);

        if (!string.IsNullOrWhiteSpace(senderDomain))
        {
            var domainSegmentKey = BuildDomainSegmentKey(senderDomain);
            if (profiles.TryGetValue(domainSegmentKey, out var domainProfile))
            {
                return new StyleProfileSelection(domainProfile, $"exact domain match: {senderDomain}");
            }

            if (IsPersonalDomain(senderDomain) &&
                profiles.TryGetValue(PersonalSegment, out var personalProfile))
            {
                return new StyleProfileSelection(personalProfile, $"personal-domain fallback: {senderDomain}");
            }

            if (!IsPersonalDomain(senderDomain) &&
                profiles.TryGetValue(ProfessionalSegment, out var professionalProfile))
            {
                return new StyleProfileSelection(professionalProfile, $"professional-domain fallback: {senderDomain}");
            }
        }

        if (profiles.TryGetValue(GlobalDefaultSegment, out var globalProfile))
        {
            return new StyleProfileSelection(globalProfile, "global default fallback");
        }

        var fallbackProfile = profiles.Values.First();
        return new StyleProfileSelection(fallbackProfile, "first available fallback");
    }

    private bool IsPersonalDomain(string domain) =>
        !string.IsNullOrWhiteSpace(domain) &&
        _options.PersonalDomains.Contains(domain, StringComparer.OrdinalIgnoreCase);

    private static string BuildDomainSegmentKey(string domain) => $"domain:{domain.Trim().ToLowerInvariant()}";

    private static string ExtractDomain(string emailAddress)
    {
        var atIndex = emailAddress.LastIndexOf('@');
        if (atIndex < 0 || atIndex == emailAddress.Length - 1)
        {
            return string.Empty;
        }

        return emailAddress[(atIndex + 1)..].Trim().ToLowerInvariant();
    }

    private static bool LooksContaminated(IReadOnlyDictionary<string, StyleProfile> profiles)
    {
        if (!profiles.ContainsKey(GlobalDefaultSegment))
        {
            return true;
        }

        return profiles.Values.Any(profile =>
            ClosingContainsInlineSignature(profile.Closing) ||
            LooksSuspiciousSignature(profile.Signature) ||
            LooksContaminatedValue(profile.Signature) ||
            profile.CommonPhrases.Any(LooksContaminatedValue));
    }

    private static bool LooksContaminatedValue(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        return value.Contains("From:", StringComparison.OrdinalIgnoreCase) ||
               value.Contains("To:", StringComparison.OrdinalIgnoreCase) ||
               value.Contains("Subject:", StringComparison.OrdinalIgnoreCase) ||
               value.Contains("Outlook Web Access", StringComparison.OrdinalIgnoreCase) ||
               value.Contains("impact on your team's projects", StringComparison.OrdinalIgnoreCase) ||
               value.Contains("is not detailed in my attached cv", StringComparison.OrdinalIgnoreCase) ||
               value.Contains("shared a file with you", StringComparison.OrdinalIgnoreCase) ||
               value.Contains("shared a folder", StringComparison.OrdinalIgnoreCase) ||
               value.Contains("shared a video with you", StringComparison.OrdinalIgnoreCase) ||
               value.Contains("shared the folder", StringComparison.OrdinalIgnoreCase) ||
               value.Contains("date of birth", StringComparison.OrdinalIgnoreCase) ||
               value.Contains("your date of birth", StringComparison.OrdinalIgnoreCase) ||
               value.Contains("name:", StringComparison.OrdinalIgnoreCase) ||
               value.Contains("name is", StringComparison.OrdinalIgnoreCase) ||
               value.Contains("highlight tape", StringComparison.OrdinalIgnoreCase) ||
               value.Contains("game recordings", StringComparison.OrdinalIgnoreCase) ||
               value.Contains("i am a 6 ft", StringComparison.OrdinalIgnoreCase) ||
               value.Contains("cargurus", StringComparison.OrdinalIgnoreCase) ||
               value.Contains("autotrader", StringComparison.OrdinalIgnoreCase) ||
               value.Contains("view in browser", StringComparison.OrdinalIgnoreCase) ||
               value.Contains("www.", StringComparison.OrdinalIgnoreCase) ||
               value.Contains('@', StringComparison.Ordinal) ||
               value.Contains('<', StringComparison.Ordinal) ||
               value.Contains('>', StringComparison.Ordinal);
    }

    private static bool ClosingContainsInlineSignature(string closing)
    {
        if (string.IsNullOrWhiteSpace(closing))
        {
            return false;
        }

        var commaIndex = closing.IndexOf(',');
        if (commaIndex < 0 || commaIndex >= closing.Length - 1)
        {
            return false;
        }

        var remainder = closing[(commaIndex + 1)..].Trim();
        if (string.IsNullOrWhiteSpace(remainder))
        {
            return false;
        }

        return closing.StartsWith("Best regards", StringComparison.OrdinalIgnoreCase) ||
               closing.StartsWith("Kind regards", StringComparison.OrdinalIgnoreCase) ||
               closing.StartsWith("Regards", StringComparison.OrdinalIgnoreCase) ||
               closing.StartsWith("Thanks", StringComparison.OrdinalIgnoreCase);
    }

    private static bool LooksSuspiciousSignature(string signature)
    {
        if (string.IsNullOrWhiteSpace(signature))
        {
            return false;
        }

        if (signature.StartsWith("best", StringComparison.OrdinalIgnoreCase) ||
            signature.StartsWith("regards", StringComparison.OrdinalIgnoreCase) ||
            signature.StartsWith("thanks", StringComparison.OrdinalIgnoreCase) ||
            signature.StartsWith("thank you", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (signature.Contains('!') || signature.Contains('?'))
        {
            return true;
        }

        var wordCount = signature
            .Split([' ', '\r', '\n', '\t'], StringSplitOptions.RemoveEmptyEntries)
            .Length;

        return wordCount > 4;
    }

    private sealed record BuildProfilesResult(
        IReadOnlyDictionary<string, StyleProfile> Profiles,
        IReadOnlyDictionary<string, IReadOnlyList<SentEmailSample>> SamplesBySegment);
}
