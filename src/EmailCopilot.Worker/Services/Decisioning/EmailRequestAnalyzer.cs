using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Options;

namespace EmailCopilot.Worker;

public sealed class EmailRequestAnalyzer : IEmailRequestAnalyzer
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    private readonly HttpClient _httpClient;
    private readonly LlmOptions _options;
    private readonly ILogger<EmailRequestAnalyzer> _logger;

    public EmailRequestAnalyzer(
        HttpClient httpClient,
        IOptions<LlmOptions> options,
        ILogger<EmailRequestAnalyzer> logger)
    {
        _httpClient = httpClient;
        _options = options.Value;
        _logger = logger;
    }

    public async Task<EmailRequestAnalysis> AnalyzeAsync(IncomingEmail email, CancellationToken cancellationToken)
    {
        var heuristic = BuildHeuristicAnalysis(email);

        if (_options.UseMock)
        {
            return heuristic;
        }

        try
        {
            var request = new AnalysisRequest(
                _options.Model,
                [
                    new ChatMessage(
                        "system",
                        "You analyze incoming emails for reply planning. Return only valid JSON with asks, decisionBranches, statedDeadlines, urgency, and requiresPersonalConfirmation."),
                    new ChatMessage("user", BuildPrompt(email))
                ],
                0.1,
                350);

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
                _logger.LogWarning(
                    "Email analysis call failed with status {StatusCode}. Falling back to heuristics.",
                    (int)response.StatusCode);
                return heuristic;
            }

            var completion = JsonSerializer.Deserialize<ChatCompletionResponse>(responseText, JsonOptions);
            var content = completion?.Choices.FirstOrDefault()?.Message?.Content;
            if (string.IsNullOrWhiteSpace(content))
            {
                return heuristic;
            }

            var parsed = TryDeserialize(content) ?? TryDeserialize(ExtractJsonObject(content));
            return parsed is null ? heuristic : Normalize(parsed);
        }
        catch (Exception ex) when (ex is HttpRequestException or InvalidOperationException or JsonException)
        {
            _logger.LogWarning(ex, "Email analysis failed. Falling back to heuristics.");
            return heuristic;
        }
    }

    internal static EmailRequestAnalysis BuildHeuristicAnalysisForTesting(IncomingEmail email) =>
        BuildHeuristicAnalysis(email);

    private static EmailRequestAnalysis BuildHeuristicAnalysis(IncomingEmail email)
    {
        var asks = new List<EmailAsk>();
        var branches = new List<DecisionBranch>();
        var combined = $"{email.Subject}\n{email.BodyText}";

        foreach (var sentence in SplitSentences(email.BodyText))
        {
            var trimmed = sentence.Trim();
            if (string.IsNullOrWhiteSpace(trimmed))
            {
                continue;
            }

            if (trimmed.Contains('?', StringComparison.Ordinal))
            {
                asks.Add(new EmailAsk(trimmed, ClassifyAskType(trimmed), false));
                continue;
            }

            if (Regex.IsMatch(
                    trimmed,
                    @"\b(can you|could you|would you|please|let me know|send (me|over)|confirm|advise)\b",
                    RegexOptions.IgnoreCase | RegexOptions.CultureInvariant))
            {
                asks.Add(new EmailAsk(trimmed, ClassifyAskType(trimmed), false));
            }
        }

        if (Regex.IsMatch(
                combined,
                @"\b(no longer going ahead|cancelled|canceled|won't be going ahead|not going ahead)\b",
                RegexOptions.IgnoreCase | RegexOptions.CultureInvariant))
        {
            branches.Add(new DecisionBranch(
                "The email communicates a cancellation or changed plan.",
                [ReplyShapes.Acknowledge, ReplyShapes.AcknowledgeAndAsk, ReplyShapes.ConfirmAndRequest, ReplyShapes.ConfirmAndClose]));
        }

        if (Regex.IsMatch(
                combined,
                @"\b(invite|invitation|offer|opportunity|available shift|role available|if you're interested|if you are interested)\b",
                RegexOptions.IgnoreCase | RegexOptions.CultureInvariant))
        {
            branches.Add(new DecisionBranch(
                "The email presents an invitation or offer that may be accepted, clarified, or declined.",
                [ReplyShapes.AcknowledgeAndAsk, ReplyShapes.Decline, ReplyShapes.DirectAnswer]));
        }

        if (Regex.IsMatch(
                combined,
                @"\b(confirm(?:ed)?|scheduled|viewing|appointment|hearing|availability|slot|timeline|when do you need)\b",
                RegexOptions.IgnoreCase | RegexOptions.CultureInvariant) &&
            !asks.Any())
        {
            branches.Add(new DecisionBranch(
                "The email conveys logistics where the user may confirm, close, or request detail.",
                [ReplyShapes.Acknowledge, ReplyShapes.ConfirmAndRequest, ReplyShapes.ConfirmAndClose]));
        }

        var deadlines = Regex.Matches(
                combined,
                @"\b(by\s+\w+|tomorrow|today|friday|monday|tuesday|wednesday|thursday|saturday|sunday|\d{1,2}(?::\d{2})?\s*(am|pm))\b",
                RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)
            .Select(match => match.Value.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        var urgency = Regex.IsMatch(combined, @"\b(urgent|asap|today|immediately|by end of day)\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)
            ? UrgencyLevels.High
            : deadlines.Length > 0
                ? UrgencyLevels.Medium
                : UrgencyLevels.Low;

        var requiresPersonalConfirmation =
            Regex.IsMatch(
                email.BodyText,
                @"\b(can you|are you able|will you|would you).{0,60}(work|come in|attend|cover|be available|make it)\b",
                RegexOptions.IgnoreCase | RegexOptions.CultureInvariant) ||
            Regex.IsMatch(
                email.Subject ?? string.Empty,
                @"\b(shift|rota|schedule|availability|cover)\b",
                RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

        return new EmailRequestAnalysis(asks, branches, deadlines, urgency, requiresPersonalConfirmation);
    }

    private static string ClassifyAskType(string text)
    {
        if (Regex.IsMatch(text, @"\b(when|time|slot|availability|schedule|viewing|appointment)\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant))
        {
            return AskTypes.Scheduling;
        }

        if (Regex.IsMatch(text, @"\b(confirm|approve|decline|still interested|available)\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant))
        {
            return AskTypes.Decision;
        }

        return text.Contains('?', StringComparison.Ordinal) ? AskTypes.Question : AskTypes.Request;
    }

    private static IEnumerable<string> SplitSentences(string body) =>
        Regex.Split(body, @"(?<=[.!?])\s+", RegexOptions.CultureInvariant)
            .Where(static sentence => !string.IsNullOrWhiteSpace(sentence));

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

    private static string BuildPrompt(IncomingEmail email) =>
$@"Analyze this incoming email for reply planning.

Return only JSON with this shape:
{{
  ""asks"": [
    {{ ""text"": ""string"", ""askType"": ""QUESTION|REQUEST|SCHEDULING|DECISION|INFORMATION"", ""isOptional"": false }}
  ],
  ""decisionBranches"": [
    {{ ""summary"": ""string"", ""viableReplyShapes"": [""DIRECT_ANSWER"",""ACKNOWLEDGE"",""ACKNOWLEDGE_AND_ASK"",""CONFIRM_AND_CLOSE"",""CONFIRM_AND_REQUEST"",""DECLINE"",""GENERAL_REPLY""] }}
  ],
  ""statedDeadlines"": [""string""],
  ""urgency"": ""UNKNOWN|LOW|MEDIUM|HIGH"",
  ""requiresPersonalConfirmation"": true
}}

""requiresPersonalConfirmation"": true when the email asks the recipient to confirm their own personal availability, capacity, or willingness (scheduling, shift coverage, attendance). false otherwise.

Email subject: {email.Subject}
Sender: {email.From.Address}
Email body:
{email.BodyText}";

    private static AnalysisPayload? TryDeserialize(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        try
        {
            return JsonSerializer.Deserialize<AnalysisPayload>(value, JsonOptions);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static string ExtractJsonObject(string value)
    {
        var start = value.IndexOf('{', StringComparison.Ordinal);
        var end = value.LastIndexOf('}');
        return start >= 0 && end > start ? value[start..(end + 1)] : value;
    }

    private static EmailRequestAnalysis Normalize(AnalysisPayload payload)
    {
        var asks = payload.Asks?
            .Where(static ask => !string.IsNullOrWhiteSpace(ask.Text))
            .Select(ask => new EmailAsk(
                ask.Text.Trim(),
                NormalizeAskType(ask.AskType),
                ask.IsOptional))
            .ToArray() ?? [];

        var branches = payload.DecisionBranches?
            .Where(static branch => !string.IsNullOrWhiteSpace(branch.Summary))
            .Select(branch => new DecisionBranch(
                branch.Summary.Trim(),
                (branch.ViableReplyShapes ?? [])
                    .Where(static shape => !string.IsNullOrWhiteSpace(shape))
                    .Select(static shape => shape.Trim().ToUpperInvariant())
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToArray()))
            .Where(static branch => branch.ViableReplyShapes.Count > 0)
            .ToArray() ?? [];

        var deadlines = payload.StatedDeadlines?
            .Where(static deadline => !string.IsNullOrWhiteSpace(deadline))
            .Select(static deadline => deadline.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray() ?? [];

        return new EmailRequestAnalysis(
            asks,
            branches,
            deadlines,
            NormalizeUrgency(payload.Urgency),
            payload.RequiresPersonalConfirmation);
    }

    private static string NormalizeAskType(string? askType) =>
        askType?.Trim().ToUpperInvariant() switch
        {
            AskTypes.Question => AskTypes.Question,
            AskTypes.Request => AskTypes.Request,
            AskTypes.Scheduling => AskTypes.Scheduling,
            AskTypes.Decision => AskTypes.Decision,
            AskTypes.Information => AskTypes.Information,
            _ => AskTypes.Request
        };

    private static string NormalizeUrgency(string? urgency) =>
        urgency?.Trim().ToUpperInvariant() switch
        {
            UrgencyLevels.Low => UrgencyLevels.Low,
            UrgencyLevels.Medium => UrgencyLevels.Medium,
            UrgencyLevels.High => UrgencyLevels.High,
            _ => UrgencyLevels.Unknown
        };

    private sealed record AnalysisRequest(
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

    private sealed class AnalysisPayload
    {
        [JsonPropertyName("asks")]
        public List<AnalysisAsk>? Asks { get; init; }

        [JsonPropertyName("decisionBranches")]
        public List<AnalysisBranch>? DecisionBranches { get; init; }

        [JsonPropertyName("statedDeadlines")]
        public List<string>? StatedDeadlines { get; init; }

        [JsonPropertyName("urgency")]
        public string? Urgency { get; init; }

        [JsonPropertyName("requiresPersonalConfirmation")]
        public bool RequiresPersonalConfirmation { get; init; }
    }

    private sealed class AnalysisAsk
    {
        [JsonPropertyName("text")]
        public string Text { get; init; } = string.Empty;

        [JsonPropertyName("askType")]
        public string AskType { get; init; } = string.Empty;

        [JsonPropertyName("isOptional")]
        public bool IsOptional { get; init; }
    }

    private sealed class AnalysisBranch
    {
        [JsonPropertyName("summary")]
        public string Summary { get; init; } = string.Empty;

        [JsonPropertyName("viableReplyShapes")]
        public List<string>? ViableReplyShapes { get; init; }
    }
}
