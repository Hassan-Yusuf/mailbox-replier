using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Options;

namespace EmailCopilot.Worker;

public sealed class OpenAIEmbeddingClient : IEmbeddingClient
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    private readonly HttpClient _httpClient;
    private readonly EmbeddingOptions _options;
    private readonly ILogger<OpenAIEmbeddingClient> _logger;

    public OpenAIEmbeddingClient(
        HttpClient httpClient,
        IOptions<EmbeddingOptions> options,
        ILogger<OpenAIEmbeddingClient> logger)
    {
        _httpClient = httpClient;
        _options = options.Value;
        _logger = logger;
    }

    public async Task<IReadOnlyList<float[]>> EmbedAsync(IReadOnlyList<string> inputs, CancellationToken cancellationToken)
    {
        if (inputs.Count == 0)
        {
            return Array.Empty<float[]>();
        }

        var request = new EmbeddingRequest(_options.Model, inputs);

        using var httpRequest = new HttpRequestMessage(HttpMethod.Post, BuildUri(_options.BaseUrl))
        {
            Content = JsonContent.Create(request, options: JsonOptions)
        };

        if (!string.IsNullOrWhiteSpace(_options.ApiKey))
        {
            httpRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _options.ApiKey);
        }

        using var response = await _httpClient.SendAsync(httpRequest, cancellationToken);
        var responseText = await response.Content.ReadAsStringAsync(cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            throw new InvalidOperationException(
                $"Embedding API call failed with status {(int)response.StatusCode}: {responseText}");
        }

        var payload = JsonSerializer.Deserialize<EmbeddingResponse>(responseText, JsonOptions);
        if (payload is null || payload.Data is null)
        {
            throw new InvalidOperationException("Embedding response did not contain any data.");
        }

        var ordered = payload.Data
            .OrderBy(item => item.Index)
            .Select(item => item.Embedding ?? Array.Empty<float>())
            .ToArray();

        if (ordered.Length != inputs.Count)
        {
            _logger.LogWarning(
                "Embedding response returned {ReturnedCount} vectors for {RequestedCount} inputs.",
                ordered.Length,
                inputs.Count);
        }

        return ordered;
    }

    private static Uri BuildUri(string baseUrl)
    {
        var trimmed = (baseUrl ?? string.Empty).TrimEnd('/');
        if (trimmed.EndsWith("/embeddings", StringComparison.OrdinalIgnoreCase))
        {
            return new Uri(trimmed);
        }

        if (trimmed.EndsWith("/v1", StringComparison.OrdinalIgnoreCase))
        {
            return new Uri(trimmed + "/embeddings");
        }

        return new Uri(trimmed + "/v1/embeddings");
    }

    private sealed record EmbeddingRequest(
        [property: JsonPropertyName("model")] string Model,
        [property: JsonPropertyName("input")] IReadOnlyList<string> Input);

    private sealed record EmbeddingResponse(
        [property: JsonPropertyName("data")] IReadOnlyList<EmbeddingItem>? Data);

    private sealed record EmbeddingItem(
        [property: JsonPropertyName("index")] int Index,
        [property: JsonPropertyName("embedding")] float[]? Embedding);
}
