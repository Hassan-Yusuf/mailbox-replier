// Run via: dotnet run --project src/EmailCopilot.Worker -- --diagnose-avoid-filter
// Bypasses IMAP/OAuth/drafting; only embeds known AIism vs centroid pairs and prints cosines.
// Used to calibrate AvoidFilter.CosineThreshold against text-embedding-3-small.

using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace EmailCopilot.Worker.Diagnostics;

public static class TestAvoidFilter
{
    public static async Task<int> RunAsync(string apiKey, string model = "text-embedding-3-small")
    {
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            Console.Error.WriteLine("Embedding:ApiKey is empty. Set it in user-secrets and retry.");
            return 1;
        }

        // Each candidate is checked against ALL applicable family seeds (max-cosine wins).
        var candidates = new (string FamilyId, string Candidate)[]
        {
            ("schedule-check-stall", "I'll check my schedule and get back to you about the shifts."),
            ("schedule-check-stall", "I'll check my availability for those shifts and get back to you."),
            ("schedule-check-stall", "I'll check on that and get back to you."),
            ("schedule-check-stall", "I'll check if I'm available and get back to you."),
            ("polite-confirmation-closer", "Let me know if that works."),
            ("polite-confirmation-closer", "Please let me know if that works."),
            ("polite-confirmation-closer", "Yeah that works, anyway speak soon."),
            ("generic-warm-closer", "Thanks for thinking of me."),
        };

        var seedsByFamily = AvoidPhraseEmbeddings.Families.ToDictionary(f => f.FamilyId, f => f.SeedTexts);

        using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
        var jsonOptions = new JsonSerializerOptions(JsonSerializerDefaults.Web)
        {
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
        };

        Console.WriteLine($"Model: {model}");
        Console.WriteLine($"Threshold: 0.82 (default)");
        Console.WriteLine();
        Console.WriteLine($"{"family",-32} {"max-cos",-10} {"verdict",-10} candidate (best-seed)");
        Console.WriteLine(new string('-', 130));

        foreach (var (familyId, candidate) in candidates)
        {
            if (!seedsByFamily.TryGetValue(familyId, out var seeds))
            {
                Console.Error.WriteLine($"FAIL: unknown familyId {familyId}");
                return 2;
            }

            var inputs = seeds.Concat(new[] { candidate }).ToArray();
            var request = new EmbeddingRequest(model, inputs);

            using var httpRequest = new HttpRequestMessage(HttpMethod.Post, new Uri("https://api.openai.com/v1/embeddings"))
            {
                Content = JsonContent.Create(request, options: jsonOptions)
            };
            httpRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);

            using var response = await http.SendAsync(httpRequest);
            var body = await response.Content.ReadAsStringAsync();
            if (!response.IsSuccessStatusCode)
            {
                Console.Error.WriteLine($"FAIL {familyId}: {(int)response.StatusCode} {body}");
                return 2;
            }

            var parsed = JsonSerializer.Deserialize<EmbeddingResponse>(body, jsonOptions);
            var vectors = parsed?.Data
                .OrderBy(d => d.Index)
                .Select(d => d.Embedding ?? Array.Empty<float>())
                .ToArray() ?? Array.Empty<float[]>();
            if (vectors.Length != inputs.Length)
            {
                Console.Error.WriteLine($"FAIL {familyId}: expected {inputs.Length} vectors, got {vectors.Length}");
                return 2;
            }

            var candidateVector = vectors[^1];
            var bestCos = 0.0;
            var bestSeed = string.Empty;
            for (var i = 0; i < seeds.Count; i++)
            {
                var c = Cosine(vectors[i], candidateVector);
                if (c > bestCos)
                {
                    bestCos = c;
                    bestSeed = seeds[i];
                }
            }
            var verdict = bestCos >= 0.82 ? "REJECT" : "PASS";
            Console.WriteLine($"{familyId,-32} {bestCos,-10:F3} {verdict,-10} {candidate}  [vs \"{bestSeed}\"]");
        }

        return 0;
    }

    private static double Cosine(float[] a, float[] b)
    {
        if (a.Length != b.Length || a.Length == 0) return 0.0;
        double dot = 0, magA = 0, magB = 0;
        for (var i = 0; i < a.Length; i++)
        {
            dot += a[i] * b[i];
            magA += a[i] * a[i];
            magB += b[i] * b[i];
        }
        if (magA == 0 || magB == 0) return 0.0;
        return dot / (Math.Sqrt(magA) * Math.Sqrt(magB));
    }

    private sealed record EmbeddingRequest(
        [property: JsonPropertyName("model")] string Model,
        [property: JsonPropertyName("input")] IReadOnlyList<string> Input);

    private sealed record EmbeddingResponse(
        [property: JsonPropertyName("data")] IReadOnlyList<EmbeddingData> Data);

    private sealed record EmbeddingData(
        [property: JsonPropertyName("index")] int Index,
        [property: JsonPropertyName("embedding")] float[]? Embedding);
}
