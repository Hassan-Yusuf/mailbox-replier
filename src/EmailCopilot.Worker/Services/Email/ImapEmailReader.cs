using HtmlAgilityPack;
using MailKit;
using MailKit.Net.Imap;
using MailKit.Search;
using MailKit.Security;
using MimeKit;
using Microsoft.Extensions.Options;
using System.Net.Sockets;

namespace EmailCopilot.Worker;

public sealed class ImapEmailReader : IIncomingEmailReader
{
    private const string MicrosoftOAuthDeviceCodeMode = "MicrosoftOAuthDeviceCode";
    private const int MaxSentMessageBodyLength = 12_000;
    private const int MaxConnectAttempts = 3;
    private readonly ImapOptions _options;
    private readonly MicrosoftOAuthTokenProvider _microsoftOAuthTokenProvider;
    private readonly ILogger<ImapEmailReader> _logger;

    public ImapEmailReader(
        IOptions<ImapOptions> options,
        MicrosoftOAuthTokenProvider microsoftOAuthTokenProvider,
        ILogger<ImapEmailReader> logger)
    {
        _options = options.Value;
        _microsoftOAuthTokenProvider = microsoftOAuthTokenProvider;
        _logger = logger;
    }

    public async Task<IReadOnlyList<uint>> GetUnreadUidsAsync(
        IReadOnlySet<uint> excludedImapUids,
        CancellationToken cancellationToken)
    {
        using var protocolLogger = CreateProtocolLogger();
        using var client = await ConnectAuthenticatedClientAsync(protocolLogger, cancellationToken);

        var inbox = client.Inbox;
        await inbox.OpenAsync(FolderAccess.ReadOnly, cancellationToken);

        var unreadUids = await inbox.SearchAsync(SearchQuery.NotSeen, cancellationToken);
        _logger.LogInformation("Found {UnreadCount} unread email(s) in the inbox.", unreadUids.Count);

        var availableUids = unreadUids
            .Where(uid => !excludedImapUids.Contains(uid.Id))
            .Select(uid => uid.Id)
            .Reverse()
            .ToList();

        _logger.LogInformation(
            "Found {CandidateCount} undrafted unread email(s) after excluding {ExcludedCount} already-processed UID(s).",
            availableUids.Count,
            unreadUids.Count - availableUids.Count);

        await client.DisconnectAsync(true, cancellationToken);
        return availableUids;
    }

    public async Task<IReadOnlyList<IncomingEmail>> GetEmailsBatchAsync(
        IReadOnlyList<uint> imapUids,
        CancellationToken cancellationToken)
    {
        using var protocolLogger = CreateProtocolLogger();
        using var client = await ConnectAuthenticatedClientAsync(protocolLogger, cancellationToken);

        var inbox = client.Inbox;
        await inbox.OpenAsync(FolderAccess.ReadOnly, cancellationToken);

        var uniqueIds = imapUids.Select(id => new UniqueId(id)).ToList();

        _logger.LogInformation(
            "Fetching message summaries for {Count} candidate email(s).",
            uniqueIds.Count);

        var summaries = await inbox.FetchAsync(
            uniqueIds,
            MessageSummaryItems.UniqueId | MessageSummaryItems.InternalDate | MessageSummaryItems.Envelope,
            cancellationToken);

        var orderedSummaries = summaries
            .OrderByDescending(summary => summary.InternalDate ?? DateTimeOffset.MinValue)
            .ThenByDescending(summary => summary.UniqueId.Id)
            .ToList();

        var candidates = new List<IncomingEmail>(orderedSummaries.Count);
        var skippedMissingCandidateCount = 0;

        foreach (var summary in orderedSummaries)
        {
            _logger.LogInformation(
                "Loading candidate unread email UID {ImapUid} with Message-ID {MessageId}.",
                summary.UniqueId.Id,
                summary.Envelope?.MessageId ?? "(missing)");

            MimeMessage message;
            try
            {
                message = await inbox.GetMessageAsync(summary.UniqueId, cancellationToken);
            }
            catch (MessageNotFoundException ex)
            {
                skippedMissingCandidateCount++;
                _logger.LogWarning(
                    ex,
                    "Candidate unread email UID {ImapUid} disappeared before it could be fetched. Skipping it.",
                    summary.UniqueId.Id);
                continue;
            }
            catch (ImapProtocolException ex)
            {
                _logger.LogWarning(
                    ex,
                    "IMAP server closed the connection mid-batch at UID {ImapUid}. Returning {Collected} email(s) collected so far; the rest will be fetched on the next run.",
                    summary.UniqueId.Id,
                    candidates.Count);
                break;
            }

            var normalizedBody = NormalizeBody(message, out var bodyFormat);

            _logger.LogInformation(
                "Normalized candidate email UID {ImapUid} using {BodyFormat}.",
                summary.UniqueId.Id,
                bodyFormat);

            var fromMailbox = message.From.Mailboxes.FirstOrDefault();

            candidates.Add(new IncomingEmail(
                summary.UniqueId.Id,
                message.MessageId ?? summary.Envelope?.MessageId ?? string.Empty,
                EmailAddress.FromParts(fromMailbox?.Address ?? message.From.ToString(), fromMailbox?.Name ?? string.Empty),
                message.Subject ?? string.Empty,
                normalizedBody,
                (summary.InternalDate ?? message.Date).ToUniversalTime(),
                !string.IsNullOrWhiteSpace(message.Headers["List-Unsubscribe"]),
                !string.IsNullOrWhiteSpace(message.Headers["List-Id"]),
                message.Headers["Precedence"] ?? string.Empty,
                !string.IsNullOrWhiteSpace(message.Headers["Auto-Submitted"]) &&
                    !string.Equals(message.Headers["Auto-Submitted"], "no", StringComparison.OrdinalIgnoreCase)));
        }

        _logger.LogInformation(
            "Loaded {CandidateCount} candidate unread email(s) in the batch.",
            candidates.Count);

        if (skippedMissingCandidateCount > 0)
        {
            _logger.LogWarning(
                "Skipped {SkippedMissingCandidateCount} unread candidate email(s) because they were no longer available during fetch.",
                skippedMissingCandidateCount);
        }

        try
        {
            await client.DisconnectAsync(true, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "IMAP disconnect after batch fetch threw an exception (connection may already be closed). Ignoring.");
        }

        return candidates;
    }

    public async Task<IReadOnlyList<SentEmailSample>> GetSentEmailsAsync(
        bool scanAll,
        int take,
        CancellationToken cancellationToken)
    {
        using var protocolLogger = CreateProtocolLogger();
        using var client = await ConnectAuthenticatedClientAsync(protocolLogger, cancellationToken);

        var sentFolder = await ResolveSentFolderAsync(client, cancellationToken);
        await sentFolder.OpenAsync(FolderAccess.ReadOnly, cancellationToken);

        var allUids = await sentFolder.SearchAsync(SearchQuery.All, cancellationToken);
        if (allUids.Count == 0)
        {
            _logger.LogWarning("The IMAP sent folder was found, but it does not contain any messages.");
            await client.DisconnectAsync(true, cancellationToken);
            return [];
        }

        var selectedUids = scanAll
            ? allUids.ToList()
            : allUids
                .Reverse()
                .Take(Math.Max(1, take))
                .Reverse()
                .ToList();

        _logger.LogInformation(
            scanAll
                ? "Loading all {TakeCount} sent email(s) from folder {FolderName} for style bootstrap."
                : "Loading up to {TakeCount} recent sent email(s) from folder {FolderName} for style bootstrap.",
            selectedUids.Count,
            sentFolder.FullName);

        var sentEmails = new List<SentEmailSample>(selectedUids.Count);
        var skippedMissingSentCount = 0;

        foreach (var uid in selectedUids)
        {
            MimeMessage message;
            try
            {
                message = await sentFolder.GetMessageAsync(uid, cancellationToken);
            }
            catch (MessageNotFoundException ex)
            {
                skippedMissingSentCount++;
                _logger.LogWarning(
                    ex,
                    "Sent email UID {ImapUid} disappeared before it could be fetched for style bootstrap. Skipping it.",
                    uid.Id);
                continue;
            }

            var normalizedBody = NormalizeBody(message, out _);

            if (string.IsNullOrWhiteSpace(normalizedBody))
            {
                continue;
            }

            var recipientAddress = ResolvePrimaryRecipientAddress(message);
            var recipientDomain = ExtractDomain(recipientAddress);

            sentEmails.Add(new SentEmailSample(
                message.MessageId ?? string.Empty,
                message.Subject ?? string.Empty,
                recipientAddress,
                recipientDomain,
                TruncateSentBody(normalizedBody),
                message.Date.ToUniversalTime()));
        }

        _logger.LogInformation(
            "Collected {CollectedCount} usable sent email sample(s) for style bootstrap.",
            sentEmails.Count);

        if (skippedMissingSentCount > 0)
        {
            _logger.LogWarning(
                "Skipped {SkippedMissingSentCount} sent email(s) during style bootstrap because they were no longer available during fetch.",
                skippedMissingSentCount);
        }

        await client.DisconnectAsync(true, cancellationToken);
        return sentEmails;
    }

    private ProtocolLogger? CreateProtocolLogger()
    {
        if (!_options.EnableProtocolLogging)
        {
            return null;
        }

        var fullPath = Path.GetFullPath(_options.ProtocolLogPath);
        var directory = Path.GetDirectoryName(fullPath);

        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        _logger.LogWarning(
            "IMAP protocol logging is enabled. Inspect {ProtocolLogPath} after the run for server responses.",
            fullPath);

        try
        {
            return new ProtocolLogger(fullPath, false);
        }
        catch (IOException ex)
        {
            _logger.LogWarning(
                ex,
                "IMAP protocol logging could not be initialized because the log file is locked. Continuing without protocol logging for this run.");
            return null;
        }
    }

    private async Task AuthenticateAsync(ImapClient client, CancellationToken cancellationToken)
    {
        if (string.Equals(
            _options.AuthenticationMode,
            MicrosoftOAuthDeviceCodeMode,
            StringComparison.OrdinalIgnoreCase))
        {
            var accessToken = await _microsoftOAuthTokenProvider.GetAccessTokenAsync(
                _options.Username,
                cancellationToken);

            var oauth2 = new SaslMechanismOAuth2(_options.Username, accessToken);
            await client.AuthenticateAsync(oauth2, cancellationToken);
            return;
        }

        await client.AuthenticateAsync(
            _options.Username,
            _options.Password,
            cancellationToken);
    }

    private async Task<ImapClient> ConnectAuthenticatedClientAsync(
        ProtocolLogger? protocolLogger,
        CancellationToken cancellationToken)
    {
        Exception? lastException = null;

        for (var attempt = 1; attempt <= MaxConnectAttempts; attempt++)
        {
            var client = protocolLogger is null
                ? new ImapClient()
                : new ImapClient(protocolLogger);

            try
            {
                await client.ConnectAsync(
                    _options.Host,
                    _options.Port,
                    _options.UseSsl,
                    cancellationToken);

                _logger.LogInformation(
                    "IMAP server advertised authentication mechanisms: {AuthenticationMechanisms}.",
                    client.AuthenticationMechanisms.Count == 0
                        ? "(none)"
                        : string.Join(", ", client.AuthenticationMechanisms.OrderBy(value => value, StringComparer.OrdinalIgnoreCase)));

                await AuthenticateAsync(client, cancellationToken);

                _logger.LogInformation(
                    "Connected and authenticated to IMAP host {Host}:{Port} as {Username} using {AuthenticationMode}.",
                    _options.Host,
                    _options.Port,
                    _options.Username,
                    _options.AuthenticationMode);

                return client;
            }
            catch (Exception ex) when (attempt < MaxConnectAttempts && IsTransientImapConnectionFailure(ex))
            {
                client.Dispose();
                lastException = ex;

                var delay = TimeSpan.FromSeconds(Math.Pow(2, attempt - 1));
                _logger.LogWarning(
                    ex,
                    "Transient IMAP connection/authentication failure on attempt {Attempt}/{MaxAttempts}. Retrying in {DelaySeconds}s.",
                    attempt,
                    MaxConnectAttempts,
                    delay.TotalSeconds);

                await Task.Delay(delay, cancellationToken);
            }
            catch
            {
                client.Dispose();
                throw;
            }
        }

        throw lastException ?? new InvalidOperationException("IMAP connection attempts exhausted without a captured exception.");
    }

    private async Task<IMailFolder> ResolveSentFolderAsync(
        ImapClient client,
        CancellationToken cancellationToken)
    {
        try
        {
            return client.GetFolder(SpecialFolder.Sent);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(
                ex,
                "Failed to resolve the IMAP special sent folder. Falling back to common folder names.");
        }

        var personalRoot = client.GetFolder(client.PersonalNamespaces[0]);

        foreach (var folderName in new[] { "Sent", "Sent Items", "Sent Mail" })
        {
            try
            {
                var folder = await personalRoot.GetSubfolderAsync(folderName, cancellationToken);
                _logger.LogInformation(
                    "Resolved the IMAP sent folder using fallback folder name {FolderName}.",
                    folderName);

                return folder;
            }
            catch (FolderNotFoundException)
            {
            }
        }

        throw new InvalidOperationException("Could not resolve an IMAP sent folder for style bootstrap.");
    }

    private static string NormalizeBody(MimeMessage message, out string bodyFormat)
    {
        if (!string.IsNullOrWhiteSpace(message.TextBody))
        {
            bodyFormat = "text/plain";
            return NormalizeWhitespace(message.TextBody);
        }

        if (!string.IsNullOrWhiteSpace(message.HtmlBody))
        {
            bodyFormat = "html-fallback";
            return NormalizeWhitespace(ConvertHtmlToText(message.HtmlBody));
        }

        bodyFormat = "empty-body";
        return string.Empty;
    }

    private static string NormalizeWhitespace(string value)
    {
        var lines = value
            .Replace("\r\n", "\n", StringComparison.Ordinal)
            .Replace('\r', '\n')
            .Split('\n')
            .Select(line => string.Join(' ', line.Split(' ', StringSplitOptions.RemoveEmptyEntries)).Trim())
            .ToArray();

        var collapsed = new List<string>(lines.Length);
        var previousWasBlank = false;

        foreach (var line in lines)
        {
            if (string.IsNullOrWhiteSpace(line))
            {
                if (!previousWasBlank)
                {
                    collapsed.Add(string.Empty);
                }

                previousWasBlank = true;
                continue;
            }

            collapsed.Add(line);
            previousWasBlank = false;
        }

        return string.Join(Environment.NewLine, collapsed).Trim();
    }

    private static string ConvertHtmlToText(string html)
    {
        var document = new HtmlDocument();
        document.LoadHtml(html);

        RemoveNodes(document, "//script|//style|//head");
        AppendLineBreaksBeforeRemoval(document, "//br");
        AppendLineBreaksAfterRemoval(document, "//p|//div|//li|//tr|//table|//section|//article|//header|//footer|//h1|//h2|//h3|//h4|//h5|//h6");

        return HtmlEntity.DeEntitize(document.DocumentNode.InnerText);
    }

    private static string TruncateSentBody(string bodyText)
    {
        return bodyText.Length <= MaxSentMessageBodyLength
            ? bodyText
            : bodyText[..MaxSentMessageBodyLength].TrimEnd();
    }

    private string ResolvePrimaryRecipientAddress(MimeMessage message)
    {
        var recipients = message.To.Mailboxes
            .Concat(message.Cc.Mailboxes)
            .Select(mailbox => mailbox.Address?.Trim() ?? string.Empty)
            .Where(address => !string.IsNullOrWhiteSpace(address))
            .ToList();

        var ownAddress = _options.Username.Trim();

        var externalRecipient = recipients
            .FirstOrDefault(address => !address.Equals(ownAddress, StringComparison.OrdinalIgnoreCase));

        return externalRecipient ?? recipients.FirstOrDefault() ?? string.Empty;
    }

    private static string ExtractDomain(string emailAddress)
    {
        var atIndex = emailAddress.LastIndexOf('@');
        if (atIndex < 0 || atIndex == emailAddress.Length - 1)
        {
            return string.Empty;
        }

        return emailAddress[(atIndex + 1)..].Trim().ToLowerInvariant();
    }

    private static void RemoveNodes(HtmlDocument document, string xpath)
    {
        var nodes = document.DocumentNode.SelectNodes(xpath);
        if (nodes is null)
        {
            return;
        }

        foreach (var node in nodes.ToList())
        {
            node.Remove();
        }
    }

    private static void AppendLineBreaksBeforeRemoval(HtmlDocument document, string xpath)
    {
        var nodes = document.DocumentNode.SelectNodes(xpath);
        if (nodes is null)
        {
            return;
        }

        foreach (var node in nodes.ToList())
        {
            node.ParentNode?.InsertBefore(document.CreateTextNode(Environment.NewLine), node);
        }
    }

    private static void AppendLineBreaksAfterRemoval(HtmlDocument document, string xpath)
    {
        var nodes = document.DocumentNode.SelectNodes(xpath);
        if (nodes is null)
        {
            return;
        }

        foreach (var node in nodes.ToList())
        {
            node.ParentNode?.InsertAfter(document.CreateTextNode(Environment.NewLine), node);
        }
    }

    private static bool IsTransientImapConnectionFailure(Exception exception)
    {
        return exception switch
        {
            IOException => true,
            SocketException => true,
            OperationCanceledException => false,
            MailKit.Security.AuthenticationException => true,
            System.Security.Authentication.AuthenticationException => true,
            _ when exception.InnerException is not null => IsTransientImapConnectionFailure(exception.InnerException),
            _ => false
        };
    }
}
