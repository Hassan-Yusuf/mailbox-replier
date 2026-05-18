using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Options;

namespace EmailCopilot.Worker;

public sealed class LlmDraftGenerator : IReplyDraftGenerator
{
    private const int MaxPromptBodyCharacters = 8_000;
    private const int MaxAttempts = 3;

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    private readonly HttpClient _httpClient;
    private readonly LlmOptions _options;
    private readonly ILogger<LlmDraftGenerator> _logger;
    private readonly GreetingPolicy _greetingPolicy;
    private readonly AvoidPhraseEmbeddingFilter _avoidFilter;
    private readonly IEmbeddingClient _embeddingClient;

    public LlmDraftGenerator(
        HttpClient httpClient,
        IOptions<LlmOptions> options,
        ILogger<LlmDraftGenerator> logger,
        GreetingPolicy greetingPolicy,
        AvoidPhraseEmbeddingFilter avoidFilter,
        IEmbeddingClient embeddingClient)
    {
        _httpClient = httpClient;
        _options = options.Value;
        _logger = logger;
        _greetingPolicy = greetingPolicy;
        _avoidFilter = avoidFilter;
        _embeddingClient = embeddingClient;
    }

    public bool UseMock => _options.UseMock;

    public async Task<string> GenerateDraftAsync(
        IncomingEmail email,
        StyleProfile styleProfile,
        IReadOnlyList<StyleExample> styleExamples,
        EmailRequestAnalysis analysis,
        string replyShape,
        string replyShapeLabel,
        IReadOnlyList<string> mustAddressAsks,
        CancellationToken cancellationToken)
    {
        if (_options.UseMock)
        {
            _logger.LogInformation("Generating draft using mock LLM mode.");
            return BuildMockDraft(email, styleProfile, replyShapeLabel, mustAddressAsks);
        }

        _logger.LogInformation("Generating draft using remote OpenAI-compatible LLM mode.");

        var promptBody = TruncateForPrompt(email.BodyText, out var wasTruncated);
        var senderFirstName = _greetingPolicy.DetermineSenderFirstName(email.From);
        if (wasTruncated)
        {
            _logger.LogWarning(
                "Email body exceeded the prompt budget and was truncated to {MaxPromptBodyCharacters} characters.",
                MaxPromptBodyCharacters);
        }

        var selectedExamples = await SelectExamplesAsync(email, styleExamples, cancellationToken);
        var retryIssues = Array.Empty<string>();

        for (var attempt = 1; attempt <= MaxAttempts; attempt++)
        {
            try
            {
                var request = new ChatCompletionRequest(
                    _options.Model,
                    new[]
                    {
                        new ChatMessage(
                            "system",
                            "You draft email replies. Return only the email reply body. Do not add markdown, code fences, or a subject line."),
                        new ChatMessage("user", BuildPrompt(email, styleProfile, selectedExamples, analysis, analysis.RequiresPersonalConfirmation, promptBody, senderFirstName, replyShape, replyShapeLabel, mustAddressAsks, retryIssues, _options.AvoidPhrasesMode))
                    },
                    0.25,
                    220);

                using var httpRequest = new HttpRequestMessage(
                    HttpMethod.Post,
                    BuildChatCompletionsUri(_options.BaseUrl))
                {
                    Content = JsonContent.Create(request, options: JsonOptions)
                };

                httpRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _options.ApiKey);

                using var response = await _httpClient.SendAsync(httpRequest, cancellationToken);
                var responseText = await response.Content.ReadAsStringAsync(cancellationToken);

                if (!response.IsSuccessStatusCode)
                {
                    if (IsTransientStatusCode((int)response.StatusCode) && attempt < MaxAttempts)
                    {
                        await DelayBeforeRetryAsync(attempt, response.StatusCode.ToString(), cancellationToken);
                        continue;
                    }

                    throw new InvalidOperationException(
                        $"OpenAI-compatible LLM call failed with status {(int)response.StatusCode}: {responseText}");
                }

                var completion = JsonSerializer.Deserialize<ChatCompletionResponse>(responseText, JsonOptions);
                var content = completion?.Choices.FirstOrDefault()?.Message?.Content;

                if (string.IsNullOrWhiteSpace(content))
                {
                    throw new InvalidOperationException("LLM response did not contain any draft content.");
                }

                var cleanedDraft = CleanDraft(content);
                cleanedDraft = _greetingPolicy.NormalizeGreeting(cleanedDraft, email.From);
                var draftIssues = DetectDraftIssues(cleanedDraft);

                if (draftIssues.Count == 0)
                {
                    var userMarkerWords = (styleProfile.ObservedDiscourseMarkers ?? Array.Empty<DiscourseMarkerObservation>())
                        .Select(static m => m.Marker)
                        .ToArray();

                    var filterResult = await _avoidFilter.EvaluateAsync(
                        cleanedDraft,
                        replyShape,
                        userMarkerWords,
                        cancellationToken);

                    if (filterResult.Status != AvoidFilterStatus.Rejected)
                    {
                        return cleanedDraft;
                    }

                    draftIssues = new List<string>
                    {
                        $"embedding filter matched family '{filterResult.MatchedFamilyId}' on sentence \"{filterResult.OffendingSentence}\" (cosine {filterResult.Cosine:F3}). Rewrite that sentence in the user's voice — direct, natural, no template."
                    };
                }

                if (attempt < MaxAttempts)
                {
                    retryIssues = draftIssues.ToArray();
                    _logger.LogInformation(
                        "Generated draft contained style issues ({Issues}). Retrying with stricter guidance.",
                        string.Join(", ", retryIssues));
                    continue;
                }

                _logger.LogWarning(
                    "Returning a draft that still contains style issues after {MaxAttempts} attempt(s): {Issues}.",
                    MaxAttempts,
                    string.Join(", ", draftIssues));

                return cleanedDraft;
            }
            catch (HttpRequestException ex) when (attempt < MaxAttempts)
            {
                _logger.LogWarning(
                    ex,
                    "Transient HTTP failure while calling the LLM on attempt {Attempt} of {MaxAttempts}.",
                    attempt,
                    MaxAttempts);

                await DelayBeforeRetryAsync(attempt, "HttpRequestException", cancellationToken);
            }
            catch (OperationCanceledException ex) when (!cancellationToken.IsCancellationRequested && attempt < MaxAttempts)
            {
                _logger.LogWarning(
                    ex,
                    "LLM request timed out on attempt {Attempt} of {MaxAttempts}.",
                    attempt,
                    MaxAttempts);

                await DelayBeforeRetryAsync(attempt, "Timeout", cancellationToken);
            }
        }

        throw new InvalidOperationException("LLM draft generation failed after exhausting retry attempts.");
    }

    private static Uri BuildChatCompletionsUri(string baseUrl)
    {
        var trimmed = baseUrl.TrimEnd('/');

        if (trimmed.EndsWith("/chat/completions", StringComparison.OrdinalIgnoreCase))
        {
            return new Uri(trimmed, UriKind.Absolute);
        }

        if (trimmed.EndsWith("/v1", StringComparison.OrdinalIgnoreCase))
        {
            return new Uri($"{trimmed}/chat/completions", UriKind.Absolute);
        }

        return new Uri($"{trimmed}/v1/chat/completions", UriKind.Absolute);
    }

    internal static string BuildPromptForTesting(
        IncomingEmail email,
        StyleProfile styleProfile,
        IReadOnlyList<StyleExample> styleExamples,
        EmailRequestAnalysis analysis,
        bool requiresPersonalConfirmation,
        string promptBody,
        string? senderFirstName,
        string replyShape,
        string replyShapeLabel,
        IReadOnlyList<string> mustAddressAsks,
        IReadOnlyList<string> retryIssues,
        AvoidPhrasesMode avoidPhrasesMode = AvoidPhrasesMode.StaticFallback) =>
        BuildPrompt(email, styleProfile, styleExamples, analysis, requiresPersonalConfirmation, promptBody, senderFirstName, replyShape, replyShapeLabel, mustAddressAsks, retryIssues, avoidPhrasesMode);

    private static string BuildPrompt(
        IncomingEmail email,
        StyleProfile styleProfile,
        IReadOnlyList<StyleExample> styleExamples,
        EmailRequestAnalysis analysis,
        bool requiresPersonalConfirmation,
        string promptBody,
        string? senderFirstName,
        string replyShape,
        string replyShapeLabel,
        IReadOnlyList<string> mustAddressAsks,
        IReadOnlyList<string> retryIssues,
        AvoidPhrasesMode avoidPhrasesMode)
    {
        var adaptive = BuildAdaptiveStyleGuidance(styleProfile);
        var adaptiveBlock = "Adaptive style guidance derived from the learned profile:" + Environment.NewLine + adaptive;
        var favoredPhrasesSection = BuildFavoredPhrasesSection(replyShape, styleProfile.FavoredPhrases);
        string styleGuidance;
        if (styleExamples.Count == 0)
        {
            styleGuidance = string.IsNullOrEmpty(favoredPhrasesSection)
                ? adaptiveBlock
                : adaptiveBlock + Environment.NewLine + Environment.NewLine + favoredPhrasesSection;
        }
        else
        {
            // Examples are already pre-selected by GenerateDraftAsync (similarity-ranked or random fallback).
            var selectedExamples = styleExamples;
            var voiceSignals = BuildVoiceSignals(
                selectedExamples,
                styleProfile.ObservedDiscourseMarkers ?? Array.Empty<DiscourseMarkerObservation>());
            var voiceBlock = string.IsNullOrWhiteSpace(voiceSignals)
                ? string.Empty
                : "Concrete voice signals observed in this user's recent replies:" + Environment.NewLine + voiceSignals + Environment.NewLine + Environment.NewLine;
            styleGuidance = adaptiveBlock + Environment.NewLine + Environment.NewLine +
                            voiceBlock +
                            favoredPhrasesSection +
                            BuildExampleGuidance(selectedExamples);
        }
        var replyShapeGuidance = BuildIntentGuidance(replyShape, requiresPersonalConfirmation);
        var greetingRule = string.IsNullOrWhiteSpace(senderFirstName)
            ? "Greeting: usually omit it entirely, or use just \"Hi,\" with a comma. Do NOT add a person name."
            : $"Sender's first name is \"{senderFirstName}\". Strongly prefer just \"Hi,\" with no name - the user almost always omits names in greetings. Only use the name if the email is unambiguously personal AND a name fits naturally. Never invent another name from the body.";

        var retrySection = retryIssues.Count == 0
            ? string.Empty
            : $"""

The previous draft had these problems and must be rewritten without them:
- {string.Join(Environment.NewLine + "- ", retryIssues)}
""";

        var asksSection = mustAddressAsks.Count == 0
            ? "- No specific asks were extracted. Stay tightly grounded to the source email."
            : "- Must address these asks in the reply:" + Environment.NewLine + string.Join(
                Environment.NewLine,
                mustAddressAsks.Select(static ask => $"- {ask}"));

        var analysisSection = analysis.DecisionBranches.Count == 0
            ? "- No decision branches extracted."
            : "- Decision branches extracted:" + Environment.NewLine + string.Join(
                Environment.NewLine,
                analysis.DecisionBranches.Select(branch => $"- {branch.Summary} [{string.Join(", ", branch.ViableReplyShapes)}]"));

        var avoidPhrasesSection = BuildAvoidPhrasesSection(replyShape, styleProfile.AvoidPhrases, avoidPhrasesMode);

        var personalConfirmationDirective = requiresPersonalConfirmation
            ? """

CRITICAL OUTPUT REQUIREMENT (overrides everything below, including style examples):
This reply requires facts only the recipient personally knows - their availability, location/property details, experience, qualifications, or willingness to commit.
You DO NOT know any of these. Every such fact in the reply MUST be a [placeholder] - even when the sender's email seems to name or imply them.

GOOD (scheduling): "Yes, I can do [time] at [location]."
GOOD (property):   "There [is/are] [number] dehumidifier(s) at [property address]."
GOOD (experience): "I have [years] of experience in [area]." | "I [have/don't have] experience in this area."
BAD : "Yes, I can work tomorrow at Ipswich FC from 16:30 to 23:15." (echoes sender's specifics as confirmed fact)
BAD : "We have one dehumidifier at [property address]." (the count is also unknown - must also be [placeholder])
BAD : "I have catering experience." (asserts a capability the user has not stated)

This rule wins against any natural "Yes I can..." or "I have..." phrasing in the style examples.

"""
            : string.Empty;

        return
$"""
You are writing this reply AS the user — not as an AI assistant composing something for them to use. Write like they actually write: direct, natural, no templates. Match their tone and voice so closely that the recipient reads it as them, not someone else. Follow the examples below. Write in the user's learned reply style.
{personalConfirmationDirective}
Reply intent:
- Reply shape: {replyShape}
- Reply shape label: {replyShapeLabel}
- Reply shape guidance: {replyShapeGuidance}

Follow these rules exactly:
- Sound like a person quickly replying from their phone - not like a customer-service template or formal correspondence.
- When the reply-shape TONE conflicts with the style profile or examples below, follow the reply-shape TONE.
- Get to the point immediately.
- Prefer a direct question, confirmation, or concrete next step when appropriate.
- Casual-professional is good. Corporate, polished, or "helpful assistant" sounding is bad.
- Do not restate or summarize the sender's email back to them.
- Do not use filler such as "Thank you for reaching out", "I appreciate", "Looking forward", or "Best regards".
- Do not praise the sender or their project unless there is a very specific genuine reason.
- Do not add a sign-off or signature unless the email is clearly formal and truly needs one.
- Avoid narrating portal or link-clicking actions unless that is genuinely the clearest reply.
- Do not introduce dates, times, numbers, URLs, names, or commitments that are not present in the source email.
- Availability, experience claims, acceptance or rejection of an offer, and opinions or ratings you cannot know must always use an explicit [placeholder]. Do not infer these from context. Examples: "I can do [time]", "I [have/don't have] experience in this area", "I'm [interested/not interested] in the role".
- {greetingRule}
- If you use a greeting, keep it brief - just "{styleProfile.Greeting},". Adding a recipient name is rare for this user; omit it unless clearly natural.

Selected style segment: {styleProfile.SegmentKey}

{styleGuidance}

Extracted reply-planning analysis:
{asksSection}
{analysisSection}
- Stated deadlines: {(analysis.StatedDeadlines.Count == 0 ? "none" : string.Join(", ", analysis.StatedDeadlines))}
- Urgency: {analysis.Urgency}
{avoidPhrasesSection}
Sender: {email.From.Address}
Subject: {email.Subject}
Message-ID: {email.MessageId}

Email body:
{promptBody}
{retrySection}
""";
    }

    private const int SimilarityQueryTextMaxChars = 300;
    private const int SimilaritySubjectMinChars = 10;
    private const int SimilarityBodyMinChars = 50;

    private static readonly Regex ReplyForwardPrefixRegex = new(
        @"^\s*(re|fwd|fw|aw)\s*:\s*",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);

    private async Task<IReadOnlyList<StyleExample>> SelectExamplesAsync(
        IncomingEmail email,
        IReadOnlyList<StyleExample> styleExamples,
        CancellationToken cancellationToken)
    {
        if (styleExamples.Count == 0)
        {
            return Array.Empty<StyleExample>();
        }

        if (!_options.SimilarExamples.Enabled || styleExamples.Count <= 3)
        {
            return SelectPromptExamples(email.MessageId, styleExamples);
        }

        var queryText = BuildSimilarityQueryText(email);
        if (queryText is null)
        {
            return SelectPromptExamples(email.MessageId, styleExamples);
        }

        try
        {
            var ranked = await RankExamplesBySimilarityAsync(
                queryText,
                styleExamples,
                _embeddingClient,
                _options.SimilarExamples.TopN,
                cancellationToken);

            _logger.LogInformation(
                "Selected {Count} similar example(s) for UID {Uid} (top-N={TopN}).",
                ranked.Count,
                email.ImapUid,
                _options.SimilarExamples.TopN);

            return ranked;
        }
        catch (Exception ex) when (ex is HttpRequestException or InvalidOperationException or OperationCanceledException && !cancellationToken.IsCancellationRequested)
        {
            _logger.LogWarning(
                ex,
                "Similar-examples embedding failed; falling back to seeded random selection.");
            return SelectPromptExamples(email.MessageId, styleExamples);
        }
    }

    internal static async Task<IReadOnlyList<StyleExample>> RankExamplesBySimilarityAsync(
        string queryText,
        IReadOnlyList<StyleExample> styleExamples,
        IEmbeddingClient embeddingClient,
        int topN,
        CancellationToken cancellationToken)
    {
        if (styleExamples.Count == 0)
        {
            return Array.Empty<StyleExample>();
        }

        var corpus = styleExamples
            .Select(static e => Truncate(e.ExampleBody ?? string.Empty, SimilarityQueryTextMaxChars))
            .ToArray();

        var inputs = new string[corpus.Length + 1];
        inputs[0] = queryText;
        Array.Copy(corpus, 0, inputs, 1, corpus.Length);

        var vectors = await embeddingClient.EmbedAsync(inputs, cancellationToken);
        if (vectors.Count != inputs.Length)
        {
            throw new InvalidOperationException(
                $"Expected {inputs.Length} vectors from similarity batch embed, got {vectors.Count}.");
        }

        var queryVector = vectors[0];
        var requestedTopN = Math.Min(Math.Max(topN, 1), styleExamples.Count);

        return styleExamples
            .Select((example, index) => new
            {
                Example = example,
                Cosine = AvoidPhraseEmbeddingFilter.Cosine(queryVector, vectors[index + 1])
            })
            .OrderByDescending(static r => r.Cosine)
            .ThenByDescending(static r => r.Example.SentAtUtc)
            .Take(requestedTopN)
            .Select(r => r.Example)
            .ToArray();
    }

    internal static string? BuildSimilarityQueryTextForTesting(IncomingEmail email) =>
        BuildSimilarityQueryText(email);

    private static string? BuildSimilarityQueryText(IncomingEmail email)
    {
        var subject = ReplyForwardPrefixRegex.Replace(email.Subject ?? string.Empty, string.Empty).Trim();
        if (subject.Length >= SimilaritySubjectMinChars)
        {
            return Truncate(subject, SimilarityQueryTextMaxChars);
        }

        var body = (email.BodyText ?? string.Empty).Trim();
        if (body.Length >= SimilarityBodyMinChars)
        {
            return Truncate(body, SimilarityQueryTextMaxChars);
        }

        return null;
    }

    private static string Truncate(string value, int maxChars) =>
        value.Length <= maxChars ? value : value[..maxChars];

    internal static IReadOnlyList<StyleExample> SelectPromptExamples(
        string messageId,
        IReadOnlyList<StyleExample> styleExamples)
    {
        if (styleExamples.Count <= 3)
        {
            return styleExamples
                .OrderByDescending(static example => example.SentAtUtc)
                .ToArray();
        }

        var seed = ComputeStableSeed(messageId);
        var maximumCount = Math.Min(5, styleExamples.Count);
        var desiredCount = 3 + (seed % (maximumCount - 2));
        var random = new Random(seed);
        var remaining = styleExamples.ToList();
        var selected = new List<StyleExample>(desiredCount);

        while (selected.Count < desiredCount && remaining.Count > 0)
        {
            var index = random.Next(remaining.Count);
            selected.Add(remaining[index]);
            remaining.RemoveAt(index);
        }

        return selected
            .OrderByDescending(static example => example.SentAtUtc)
            .ToArray();
    }

    private static int ComputeStableSeed(string value)
    {
        unchecked
        {
            var hash = (int)2166136261;

            foreach (var character in value)
            {
                hash ^= character;
                hash *= 16777619;
            }

            return hash & int.MaxValue;
        }
    }

    private static string BuildExampleGuidance(IReadOnlyList<StyleExample> examples)
    {
        var blocks = examples.Select(static example =>
            $"""
[RE: {example.SubjectHint}]
{example.ExampleBody}
""");

        return "Style examples - how this user actually writes replies. Match their tone, length, and phrasing. Do not copy verbatim." +
               Environment.NewLine +
               Environment.NewLine +
               string.Join(Environment.NewLine + Environment.NewLine, blocks);
    }

    private static readonly System.Text.RegularExpressions.Regex LowercaseIMidSentenceRegex =
        new(@"(?<=\S\s)i\b",
            System.Text.RegularExpressions.RegexOptions.CultureInvariant | System.Text.RegularExpressions.RegexOptions.Compiled);

    private static readonly System.Text.RegularExpressions.Regex VoiceWordRegex =
        new(@"\b[\p{L}\p{N}']+\b",
            System.Text.RegularExpressions.RegexOptions.CultureInvariant | System.Text.RegularExpressions.RegexOptions.Compiled);

    private static readonly string[] EmojiWhitelist =
    [
        "\U0001F64F", // 🙏
        "\U0001F44D", // 👍
        "\U0001F60A", // 😊
        "\u2764\uFE0F", // ❤️
        "\u2764", // ❤ (unqualified)
        "\U0001F64C"  // 🙌
    ];

    internal static string BuildVoiceSignalsForTesting(IReadOnlyList<StyleExample> examples) =>
        BuildVoiceSignals(examples, Array.Empty<DiscourseMarkerObservation>());

    internal static string BuildVoiceSignalsForTesting(
        IReadOnlyList<StyleExample> examples,
        IReadOnlyList<DiscourseMarkerObservation> observedMarkers) =>
        BuildVoiceSignals(examples, observedMarkers);

    private static string BuildVoiceSignals(
        IReadOnlyList<StyleExample> examples,
        IReadOnlyList<DiscourseMarkerObservation> observedMarkers)
    {
        if (examples.Count == 0)
        {
            return string.Empty;
        }

        var bodies = examples
            .Select(example => example.ExampleBody ?? string.Empty)
            .Where(body => !string.IsNullOrWhiteSpace(body))
            .ToArray();

        if (bodies.Length == 0)
        {
            return string.Empty;
        }

        var sampleCount = bodies.Length;
        var lowercaseIThreshold = sampleCount >= 10 ? 0.35 : 0.50;
        var emojiThreshold = sampleCount >= 10 ? 0.15 : 0.20;
        const double trailingQuestionThreshold = 0.40;

        var lowercaseIHits = bodies.Count(body => LowercaseIMidSentenceRegex.IsMatch(body));
        var trailingQuestionHits = bodies.Count(body => body.TrimEnd().EndsWith("?", StringComparison.Ordinal));
        var emojiHits = bodies.Count(body => EmojiWhitelist.Any(emoji => body.Contains(emoji, StringComparison.Ordinal)));

        var lowercaseIRate = (double)lowercaseIHits / sampleCount;
        var trailingQuestionRate = (double)trailingQuestionHits / sampleCount;
        var emojiRate = (double)emojiHits / sampleCount;

        var wordCounts = bodies
            .Select(body => VoiceWordRegex.Matches(body).Count)
            .Where(count => count > 0)
            .OrderBy(count => count)
            .ToArray();

        var lines = new List<string>(4);

        if (lowercaseIRate >= lowercaseIThreshold)
        {
            lines.Add("- The user writes lowercase \"i\" mid-sentence - keep this when it fits.");
        }

        if (trailingQuestionRate >= trailingQuestionThreshold)
        {
            lines.Add("- The user often ends with a short question.");
        }

        if (emojiRate >= emojiThreshold)
        {
            lines.Add("- A single emoji at the end is occasionally natural (do not force one).");
        }

        if (wordCounts.Length > 0)
        {
            var median = wordCounts[wordCounts.Length / 2];
            lines.Add($"- Median reply length: {median} words.");
        }

        if (observedMarkers.Count >= 2)
        {
            var markerList = string.Join(
                ", ",
                observedMarkers
                    .OrderByDescending(static m => m.Rate)
                    .Take(6)
                    .Select(static m => $"\"{m.Marker}\""));
            lines.Add($"- The user often uses: {markerList} - fold these in where they fit naturally.");
        }

        return lines.Count == 0 ? string.Empty : string.Join(Environment.NewLine, lines);
    }

    private const int MaxAvoidPhrases = 7;
    private const int MaxFavoredPhrases = 3;

    private static string BuildFavoredPhrasesSection(
        string replyShape,
        IReadOnlyDictionary<string, IReadOnlyList<string>>? favored)
    {
        if (favored is null || !favored.TryGetValue(replyShape, out var phrases) || phrases.Count == 0)
        {
            return string.Empty;
        }

        var capped = phrases
            .Where(static p => !string.IsNullOrWhiteSpace(p))
            .Take(MaxFavoredPhrases)
            .ToArray();

        if (capped.Length == 0)
        {
            return string.Empty;
        }

        var bulletList = string.Join(
            Environment.NewLine,
            capped.Select(static phrase => $"- \"{phrase}\""));

        return "Phrases the user actually writes when replying in this shape - use one only if it fits naturally, do not force:" + Environment.NewLine +
               bulletList + Environment.NewLine + Environment.NewLine;
    }

    private static string BuildAvoidPhrasesSection(
        string replyShape,
        IReadOnlyDictionary<string, IReadOnlyList<string>>? profileAvoidPhrases,
        AvoidPhrasesMode mode)
    {
        if (mode == AvoidPhrasesMode.Disabled)
        {
            return string.Empty;
        }

        IReadOnlyList<string> phrases = Array.Empty<string>();

        if (profileAvoidPhrases is not null
            && profileAvoidPhrases.TryGetValue(replyShape, out var learned)
            && learned.Count > 0)
        {
            phrases = learned;
        }
        else
        {
            phrases = StaticAvoidPhrases.ForShape(replyShape);
        }

        if (phrases.Count == 0)
        {
            return string.Empty;
        }

        var capped = phrases
            .Where(static p => !string.IsNullOrWhiteSpace(p))
            .Take(MaxAvoidPhrases)
            .ToArray();

        if (capped.Length == 0)
        {
            return string.Empty;
        }

        var bulletList = string.Join(
            Environment.NewLine,
            capped.Select(static phrase => $"- \"{phrase}\""));

        return Environment.NewLine +
               "The user's writing style avoids phrases like:" + Environment.NewLine +
               bulletList + Environment.NewLine +
               "Avoid these and equivalent generic constructions. Match the user's voice from the style examples above." +
               Environment.NewLine;
    }

    private static string BuildIntentGuidance(string replyShape, bool requiresPersonalConfirmation) =>
        replyShape switch
        {
            ReplyShapes.DirectAnswer =>
                "TONE: answer the actual question directly, no preamble. " +
                "Answer the sender directly in the first sentence. Add only essential follow-up detail. " +
                "If the answer depends on information you cannot know (availability, property details, credentials, preferences), " +
                "write it with an explicit [placeholder] - e.g. \"Yes, I can do [time]\" - never invent or assert a fact as true. " +
                "Do not ask a clarifying question if the ask is already specific enough to act on. " +
                "If you cannot fulfill the ask without more context, acknowledge the ask and commit to acting on it rather than asking the sender a question back." +
                (requiresPersonalConfirmation
                    ? " IMPORTANT: This email asks you to confirm personal availability or attendance. " +
                      "You CANNOT know the sender's actual schedule. Use [placeholder] for every specific time, date, location, or confirmation of willingness - even if the email names them. " +
                      "Example: \"Yes, I can do [date/time] at [location].\" Never assert as fact."
                    : string.Empty),
            ReplyShapes.Acknowledge =>
                "TONE: acknowledge receipt only - no questions, no commitments. " +
                "If the email is informational - an update, notification, or FYI - acknowledge it in one or two sentences. " +
                "If the email is an offer, invitation, or request for availability, defer: indicate you will check and come back rather than passively noting it. " +
                "Do not state a stance (interested, not interested, available, unavailable) - use [placeholder] if the shape forces one.",
            ReplyShapes.AcknowledgeAndAsk =>
                "TONE: acknowledge briefly, then ask the one clarifying question that unblocks the next step. " +
                "Acknowledge the information, then ask one specific clarifying question if it helps move things forward.",
            ReplyShapes.ConfirmAndClose =>
                "TONE: confirm understanding, close the loop, no further action requested. " +
                "Confirm briefly and close the loop without opening unnecessary follow-up. Do not assert availability, attendance, or acceptance without a [placeholder] if those facts are not known.",
            ReplyShapes.ConfirmAndRequest =>
                "TONE: confirm understanding, then make one specific request. " +
                "Confirm the update briefly, then request one specific missing detail or next-step clarification.",
            ReplyShapes.Decline =>
                "TONE: decline politely, no long explanation, no apology spiral. " +
                "Decline clearly and briefly without over-explaining. " +
                "If the decline involves your availability or capacity, use a [placeholder] rather than asserting a specific fact " +
                "e.g. use [tomorrow] or [the proposed time] rather than stating a specific date as fact.",
            _ =>
                "Write the single best-fit reply for the likely user intent."
        };
    private static string BuildAdaptiveStyleGuidance(StyleProfile styleProfile)
    {
        var guidance = new List<string>();

        if (styleProfile.GreetingUsageRate < 0.2)
        {
            guidance.Add("- Usually skip the greeting.");
        }
        else if (styleProfile.GreetingUsageRate > 0.7)
        {
            guidance.Add($"- A greeting is usually natural. When used, prefer \"{styleProfile.Greeting}\".");
        }

        guidance.Add($"- Typical reply length is around {styleProfile.TypicalSentenceCountMin}-{styleProfile.TypicalSentenceCountMax} sentences.");

        if (styleProfile.QuestionEndingRate > 0.5)
        {
            guidance.Add("- Ending with a direct question is often natural for this user.");
        }
        else if (styleProfile.QuestionEndingRate < 0.15)
        {
            guidance.Add("- Do not force a question if a direct answer is enough.");
        }

        if (styleProfile.ExclamationUsageRate < 0.05)
        {
            guidance.Add("- The user almost never uses exclamation marks.");
        }
        else if (styleProfile.ExclamationUsageRate > 0.30)
        {
            guidance.Add("- Exclamation marks can be natural for this user.");
        }

        if (styleProfile.GratitudeUsageRate < 0.1)
        {
            guidance.Add("- Avoid gratitude phrases unless they are clearly natural here.");
        }
        else if (styleProfile.GratitudeUsageRate > 0.5)
        {
            guidance.Add("- Brief thanks can be natural for this user.");
        }

        if (styleProfile.SignoffUsageRate < 0.15)
        {
            guidance.Add("- Usually do not add a sign-off.");
        }
        else if (styleProfile.SignoffUsageRate > 0.5)
        {
            guidance.Add($"- A sign-off can be natural, usually \"{styleProfile.Closing}\" when the reply is more formal.");
        }

        if (styleProfile.ContractionUsageRate > 0.6)
        {
            guidance.Add("- Contractions are natural for this user.");
        }
        else if (styleProfile.ContractionUsageRate < 0.2)
        {
            guidance.Add("- A slightly more formal register is natural for this user.");
        }

        if (styleProfile.FragmentUsageRate > 0.3)
        {
            guidance.Add("- Fragments or very short sentences can be natural when they fit.");
        }

        if (styleProfile.ExplicitNextStepRate > 0.5)
        {
            guidance.Add("- If a next step is implied, stating it directly can be natural for this user.");
        }

        guidance.Add(styleProfile.FormalityScore >= 0.65
            ? "- Lean more formal when the email context calls for it."
            : styleProfile.FormalityScore <= 0.35
                ? "- A more conversational tone is natural for this user."
                : "- Keep the tone in the middle: clear and natural, not stiff.");

        return string.Join(Environment.NewLine, guidance);
    }

    private static string BuildMockDraft(IncomingEmail email, StyleProfile styleProfile, string intentLabel, IReadOnlyList<string> mustAddressAsks)
    {
        var recipientName = ExtractRecipientName(email.From.Address);
        var subjectFragment = string.IsNullOrWhiteSpace(email.Subject)
            ? "your message"
            : $"your note about \"{email.Subject}\"";

        var signatureBlock = string.IsNullOrWhiteSpace(styleProfile.Signature)
            ? "Email Copilot"
            : styleProfile.Signature;

        return
$"""
{styleProfile.Greeting} {recipientName},

[{intentLabel}] Thanks for {subjectFragment}. {(mustAddressAsks.Count == 0 ? "I received it and will review the details shortly." : $"I'll respond on: {string.Join("; ", mustAddressAsks)}.")}

{styleProfile.Closing},
{signatureBlock}
""".Trim();
    }

    private static string ExtractRecipientName(string fromAddress)
    {
        var localPart = fromAddress.Split('@', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault();
        if (string.IsNullOrWhiteSpace(localPart))
        {
            return "there";
        }

        var normalized = localPart.Replace('.', ' ').Replace('_', ' ').Trim();
        return string.IsNullOrWhiteSpace(normalized)
            ? "there"
            : char.ToUpperInvariant(normalized[0]) + normalized[1..];
    }

    private static string CleanDraft(string content)
    {
        var trimmed = content.Trim();

        if (trimmed.StartsWith("```", StringComparison.Ordinal) &&
            trimmed.EndsWith("```", StringComparison.Ordinal))
        {
            var lines = trimmed.Split('\n').Skip(1).ToList();
            if (lines.Count > 0 && lines[^1].Trim() == "```")
            {
                lines.RemoveAt(lines.Count - 1);
            }

            return string.Join('\n', lines).Trim();
        }

        return NormalizeLineEndings(trimmed);
    }

    private static string NormalizeLineEndings(string value) =>
        value.Replace("\r\n", "\n", StringComparison.Ordinal).Trim();

    internal static IReadOnlyList<string> DetectDraftIssuesForTesting(string draft) =>
        DetectDraftIssues(draft);

    private static List<string> DetectDraftIssues(string draft)
    {
        var issues = new List<string>();

        if (CountSentences(draft) > 4)
        {
            issues.Add("too many sentences");
        }

        if (CountWords(draft) > 120)
        {
            issues.Add("too long");
        }

        foreach (var (pattern, label) in AiismRules())
        {
            if (Regex.IsMatch(draft, pattern, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant))
            {
                issues.Add(label);
            }
        }

        return issues
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static IEnumerable<(string Pattern, string Label)> AiismRules()
    {
        yield return (@"thank you for (reaching out|sharing|your (email|message|note))", "filler gratitude opener");
        yield return (@"\bi (appreciate|commend|value)\b", "hollow appreciation");
        yield return (@"looking forward to", "vague forward-looking filler");
        yield return (@"please (don't hesitate|feel free) to", "corporate boilerplate");
        yield return (@"best regards", "formal sign-off");
        yield return (@"hope this (email )?finds you", "generic greeting filler");
        yield return (@"positively impact", "corporate jargon");
        yield return (@"at your earliest convenience", "passive filler");
        yield return (@"impact your team'?s projects", "template-like project phrasing");
        yield return (@"\blet me know if (you need|there is|I can|there'?s anything)", "filler offer of further help");
        yield return (@"\bi hope this (helps|is helpful|answers)", "AI closer filler");
        yield return (@"\bfeel free to (reach out|ask|contact)", "AI offer-of-help filler");
    }

    private static int CountWords(string value) =>
        Regex.Matches(value, @"\b[\p{L}\p{N}']+\b", RegexOptions.CultureInvariant).Count;

    private static int CountSentences(string value) =>
        Regex.Matches(value, @"[.!?](?:\s|$)", RegexOptions.CultureInvariant).Count;

    private static string TruncateForPrompt(string bodyText, out bool wasTruncated)
    {
        if (bodyText.Length <= MaxPromptBodyCharacters)
        {
            wasTruncated = false;
            return bodyText;
        }

        wasTruncated = true;
        return bodyText[..MaxPromptBodyCharacters].TrimEnd() + Environment.NewLine + Environment.NewLine + "[Message truncated]";
    }

    private static bool IsTransientStatusCode(int statusCode) =>
        statusCode == 408 || statusCode == 429 || statusCode >= 500;

    private async Task DelayBeforeRetryAsync(int attempt, string reason, CancellationToken cancellationToken)
    {
        var delay = TimeSpan.FromSeconds(Math.Pow(2, attempt));
        _logger.LogWarning(
            "Retrying LLM call after {Reason}. Waiting {DelaySeconds} second(s) before the next attempt.",
            reason,
            delay.TotalSeconds);

        await Task.Delay(delay, cancellationToken);
    }

    private sealed record ChatCompletionRequest(
        [property: JsonPropertyName("model")] string Model,
        [property: JsonPropertyName("messages")] IReadOnlyList<ChatMessage> Messages,
        [property: JsonPropertyName("temperature")] double Temperature,
        [property: JsonPropertyName("max_tokens")] int MaxTokens);

    private sealed record ChatMessage(
        [property: JsonPropertyName("role")] string Role,
        [property: JsonPropertyName("content")] string Content);

    private sealed class ChatCompletionResponse
    {
        [JsonPropertyName("choices")]
        public List<ChatChoice> Choices { get; init; } = [];
    }

    private sealed class ChatChoice
    {
        [JsonPropertyName("message")]
        public ChatMessage? Message { get; init; }
    }
}
