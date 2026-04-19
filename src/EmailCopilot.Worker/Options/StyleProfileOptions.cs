namespace EmailCopilot.Worker;

public sealed class StyleProfileOptions
{
    public bool BootstrapAllSentEmails { get; set; } = true;

    public int BootstrapSentEmailCount { get; set; } = 200;

    public int MinimumRelationshipSampleSize { get; set; } = 12;

    public int MinimumDomainSampleSize { get; set; } = 8;

    public bool RebuildOnStartup { get; set; }

    public string[] PersonalDomains { get; set; } =
    [
        "gmail.com",
        "googlemail.com",
        "outlook.com",
        "hotmail.com",
        "live.com",
        "msn.com",
        "yahoo.com",
        "icloud.com",
        "me.com",
        "proton.me",
        "protonmail.com",
        "gmx.com",
        "aol.com"
    ];

    public string Greeting { get; set; } = string.Empty;

    public string Closing { get; set; } = string.Empty;

    public string Tone { get; set; } = string.Empty;
}
