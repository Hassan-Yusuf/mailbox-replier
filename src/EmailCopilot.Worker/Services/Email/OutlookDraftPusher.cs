using MailKit;
using MailKit.Net.Imap;
using MailKit.Security;
using MimeKit;
using MimeKit.Utils;
using Microsoft.Extensions.Options;
using System.Net.Sockets;
using System.Text.RegularExpressions;

namespace EmailCopilot.Worker;

public sealed class OutlookDraftPusher : IOutlookDraftPusher
{
    private const string MicrosoftOAuthDeviceCodeMode = "MicrosoftOAuthDeviceCode";
    private const int MaxConnectAttempts = 3;
    private static readonly Regex ReplyPrefixRegex = new(@"^\s*re\s*:", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private readonly IDraftStore _draftStore;
    private readonly ImapOptions _imapOptions;
    private readonly MicrosoftOAuthTokenProvider _tokenProvider;
    private readonly ILogger<OutlookDraftPusher> _logger;

    public OutlookDraftPusher(
        IDraftStore draftStore,
        IOptions<ImapOptions> imapOptions,
        MicrosoftOAuthTokenProvider tokenProvider,
        ILogger<OutlookDraftPusher> logger)
    {
        _draftStore = draftStore;
        _imapOptions = imapOptions.Value;
        _tokenProvider = tokenProvider;
        _logger = logger;
    }

    public async Task PushAsync(long draftSetId, long selectedVariantId, CancellationToken cancellationToken)
    {
        var draftSet = await _draftStore.GetDraftSetByIdAsync(draftSetId, cancellationToken)
            ?? throw new InvalidOperationException($"Draft set {draftSetId} was not found.");

        var selectedVariant = draftSet.Variants.FirstOrDefault(variant => variant.Id == selectedVariantId)
            ?? throw new InvalidOperationException($"Variant {selectedVariantId} does not belong to draft set {draftSetId}.");

        using var client = new ImapClient();

        try
        {
            await ConnectAndAuthenticateAsync(client, cancellationToken);

            var draftsFolder = await ResolveDraftsFolderAsync(client, cancellationToken);
            await draftsFolder.OpenAsync(FolderAccess.ReadWrite, cancellationToken);

            var mimeMessage = CreateDraftMessage(_imapOptions.Username, draftSet, selectedVariant);
            await draftsFolder.AppendAsync(
                mimeMessage,
                MessageFlags.Draft | MessageFlags.Seen,
                cancellationToken);

            _logger.LogInformation(
                "Pushed draft set {DraftSetId} variant {VariantId} into Outlook Drafts.",
                draftSetId,
                selectedVariantId);
        }
        finally
        {
            if (client.IsConnected)
            {
                await client.DisconnectAsync(true, cancellationToken);
            }
        }
    }

    private async Task ConnectAndAuthenticateAsync(ImapClient client, CancellationToken cancellationToken)
    {
        Exception? lastException = null;

        for (var attempt = 1; attempt <= MaxConnectAttempts; attempt++)
        {
            try
            {
                await client.ConnectAsync(
                    _imapOptions.Host,
                    _imapOptions.Port,
                    _imapOptions.UseSsl,
                    cancellationToken);

                if (string.Equals(
                    _imapOptions.AuthenticationMode,
                    MicrosoftOAuthDeviceCodeMode,
                    StringComparison.OrdinalIgnoreCase))
                {
                    var accessToken = await _tokenProvider.GetAccessTokenAsync(_imapOptions.Username, cancellationToken);
                    var oauth2 = new SaslMechanismOAuth2(_imapOptions.Username, accessToken);
                    await client.AuthenticateAsync(oauth2, cancellationToken);
                }
                else
                {
                    await client.AuthenticateAsync(
                        _imapOptions.Username,
                        _imapOptions.Password,
                        cancellationToken);
                }

                return;
            }
            catch (Exception ex) when (attempt < MaxConnectAttempts && IsTransientImapConnectionFailure(ex))
            {
                lastException = ex;
                var delay = TimeSpan.FromSeconds(Math.Pow(2, attempt - 1));

                _logger.LogWarning(
                    ex,
                    "Transient IMAP drafts connection/authentication failure on attempt {Attempt}/{MaxAttempts}. Retrying in {DelaySeconds}s.",
                    attempt,
                    MaxConnectAttempts,
                    delay.TotalSeconds);

                if (client.IsConnected)
                {
                    await client.DisconnectAsync(true, cancellationToken);
                }

                await Task.Delay(delay, cancellationToken);
            }
        }

        throw lastException ?? new InvalidOperationException("Draft push IMAP connection attempts exhausted without a captured exception.");
    }

    private async Task<IMailFolder> ResolveDraftsFolderAsync(ImapClient client, CancellationToken cancellationToken)
    {
        try
        {
            return client.GetFolder(SpecialFolder.Drafts);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(
                ex,
                "Failed to resolve the IMAP special drafts folder. Falling back to common folder names.");
        }

        var personalRoot = client.GetFolder(client.PersonalNamespaces[0]);

        foreach (var folderName in new[] { "Drafts", "Draft", "[Gmail]/Drafts" })
        {
            try
            {
                var folder = await personalRoot.GetSubfolderAsync(folderName, cancellationToken);
                _logger.LogInformation(
                    "Resolved the IMAP drafts folder using fallback folder name {FolderName}.",
                    folderName);

                return folder;
            }
            catch (FolderNotFoundException)
            {
            }
        }

        throw new InvalidOperationException("Could not resolve an IMAP drafts folder for review approval.");
    }

    internal static MimeMessage CreateDraftMessage(
        string mailboxAddress,
        DraftSetDetail draftSet,
        DraftVariantDetail selectedVariant)
    {
        var message = new MimeMessage
        {
            MessageId = MimeUtils.GenerateMessageId(),
            Subject = ReplyPrefixRegex.IsMatch(draftSet.Subject) ? draftSet.Subject : $"Re: {draftSet.Subject}",
            Body = new TextPart("plain")
            {
                Text = selectedVariant.Body,
                ContentTransferEncoding = ContentEncoding.EightBit
            }
        };

        message.From.Add(new MailboxAddress(string.Empty, mailboxAddress));
        message.To.Add(MailboxAddress.Parse(draftSet.FromAddress));

        if (!string.IsNullOrWhiteSpace(draftSet.SourceMessageId))
        {
            message.InReplyTo = draftSet.SourceMessageId;
            message.References.Add(draftSet.SourceMessageId);
        }

        return message;
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
