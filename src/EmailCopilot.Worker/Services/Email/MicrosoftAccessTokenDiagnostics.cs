using System.Text;
using System.Text.Json;

namespace EmailCopilot.Worker;

public sealed record MicrosoftAccessTokenDiagnostics(
    string Audience,
    string Scope,
    string PreferredUsername,
    string TenantId,
    DateTimeOffset? ExpiresAtUtc)
{
    public static MicrosoftAccessTokenDiagnostics Parse(string accessToken)
    {
        if (string.IsNullOrWhiteSpace(accessToken))
        {
            return new(string.Empty, string.Empty, string.Empty, string.Empty, null);
        }

        var segments = accessToken.Split('.');
        if (segments.Length < 2)
        {
            return new(string.Empty, string.Empty, string.Empty, string.Empty, null);
        }

        try
        {
            var payloadJson = DecodeBase64Url(segments[1]);
            using var document = JsonDocument.Parse(payloadJson);
            var root = document.RootElement;

            var audience = GetString(root, "aud");
            var scope = GetString(root, "scp");
            var preferredUsername = GetString(root, "preferred_username");
            if (string.IsNullOrWhiteSpace(preferredUsername))
            {
                preferredUsername = GetString(root, "upn");
            }

            if (string.IsNullOrWhiteSpace(preferredUsername))
            {
                preferredUsername = GetString(root, "unique_name");
            }

            var tenantId = GetString(root, "tid");
            var expiresAtUtc = GetUnixTime(root, "exp");

            return new(audience, scope, preferredUsername, tenantId, expiresAtUtc);
        }
        catch
        {
            return new(string.Empty, string.Empty, string.Empty, string.Empty, null);
        }
    }

    private static string DecodeBase64Url(string value)
    {
        var normalized = value.Replace('-', '+').Replace('_', '/');
        var padding = normalized.Length % 4;

        if (padding > 0)
        {
            normalized = normalized.PadRight(normalized.Length + (4 - padding), '=');
        }

        var bytes = Convert.FromBase64String(normalized);
        return Encoding.UTF8.GetString(bytes);
    }

    private static string GetString(JsonElement root, string propertyName)
    {
        return root.TryGetProperty(propertyName, out var property) && property.ValueKind == JsonValueKind.String
            ? property.GetString() ?? string.Empty
            : string.Empty;
    }

    private static DateTimeOffset? GetUnixTime(JsonElement root, string propertyName)
    {
        if (!root.TryGetProperty(propertyName, out var property))
        {
            return null;
        }

        if (property.ValueKind == JsonValueKind.Number && property.TryGetInt64(out var unixSeconds))
        {
            return DateTimeOffset.FromUnixTimeSeconds(unixSeconds);
        }

        return null;
    }
}
