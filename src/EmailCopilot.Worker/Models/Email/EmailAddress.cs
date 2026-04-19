namespace EmailCopilot.Worker;

public sealed record EmailAddress(string Address, string DisplayName)
{
    public string Domain
    {
        get
        {
            var atIndex = Address.LastIndexOf('@');
            if (atIndex < 0 || atIndex == Address.Length - 1)
            {
                return string.Empty;
            }

            return Address[(atIndex + 1)..];
        }
    }

    public static EmailAddress FromParts(string? address, string? displayName)
    {
        var normalizedAddress = (address ?? string.Empty).Trim().ToLowerInvariant();
        var normalizedDisplayName = (displayName ?? string.Empty).Trim();
        return new EmailAddress(normalizedAddress, normalizedDisplayName);
    }

    public bool HasDisplayName => !string.IsNullOrWhiteSpace(DisplayName);
}
