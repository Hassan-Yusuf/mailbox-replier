namespace EmailCopilot.Worker;

public sealed class ImapOptions
{
    public string AuthenticationMode { get; set; } = "Password";

    public bool EnableProtocolLogging { get; set; }

    public string ProtocolLogPath { get; set; } = "logs/imap-protocol.log";

    public int SelectionWindowSize { get; set; } = 100;

    public int PreferredBatchSize { get; set; } = 500;

    public string Host { get; set; } = string.Empty;

    public int Port { get; set; }

    public bool UseSsl { get; set; }

    public string Username { get; set; } = string.Empty;

    public string Password { get; set; } = string.Empty;
}
