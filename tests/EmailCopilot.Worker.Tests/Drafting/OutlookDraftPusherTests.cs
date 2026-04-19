using MimeKit;

namespace EmailCopilot.Worker.Tests;

[TestFixture]
public sealed class OutlookDraftPusherTests
{
    [Test]
    public void Should_prefix_subject_with_re_when_missing()
    {
        var message = OutlookDraftPusher.CreateDraftMessage(
            "owner@example.com",
            CreateDraftSetDetail(subject: "Broken chair", sourceMessageId: "<message-1@example.com>"),
            CreateDraftVariantDetail());

        Assert.That(message.Subject, Is.EqualTo("Re: Broken chair"));
    }

    [TestCase("Re: Broken chair")]
    [TestCase("RE: Broken chair")]
    [TestCase("re: Broken chair")]
    [TestCase("Re : Broken chair")]
    public void Should_not_duplicate_re_prefix_when_already_present(string subject)
    {
        var message = OutlookDraftPusher.CreateDraftMessage(
            "owner@example.com",
            CreateDraftSetDetail(subject: subject, sourceMessageId: "<message-2@example.com>"),
            CreateDraftVariantDetail());

        Assert.That(message.Subject, Is.EqualTo(subject));
    }

    [Test]
    public void Should_populate_threading_headers_from_source_message_id()
    {
        const string sourceMessageId = "<message-3@example.com>";

        var message = OutlookDraftPusher.CreateDraftMessage(
            "owner@example.com",
            CreateDraftSetDetail(subject: "Question", sourceMessageId: sourceMessageId),
            CreateDraftVariantDetail());

        Assert.That(message.InReplyTo, Is.EqualTo(sourceMessageId.Trim('<', '>')));
        Assert.That(message.References, Has.Count.EqualTo(1));
        Assert.That(message.References[0], Is.EqualTo(sourceMessageId.Trim('<', '>')));
    }

    [Test]
    public void Should_build_plain_text_message_body_and_addresses()
    {
        var message = OutlookDraftPusher.CreateDraftMessage(
            "owner@example.com",
            CreateDraftSetDetail(subject: "Question", fromAddress: "sender@example.com", sourceMessageId: "<message-4@example.com>"),
            CreateDraftVariantDetail(body: "Hi,\n\nHere is the draft."));

        Assert.That(message.From.Mailboxes.Single().Address, Is.EqualTo("owner@example.com"));
        Assert.That(message.To.Mailboxes.Single().Address, Is.EqualTo("sender@example.com"));
        Assert.That(message.Body, Is.TypeOf<TextPart>());
        Assert.That(((TextPart)message.Body).Text, Is.EqualTo("Hi,\r\n\r\nHere is the draft."));
    }

    private static DraftSetDetail CreateDraftSetDetail(
        string subject,
        string sourceMessageId,
        string fromAddress = "sender@example.com") =>
        new(
            1,
            fromAddress,
            subject,
            DateTimeOffset.UtcNow,
            DateTimeOffset.UtcNow,
            DraftSetStatuses.Pending,
            sourceMessageId,
            null,
            null,
            [CreateDraftVariantDetail()]);

    private static DraftVariantDetail CreateDraftVariantDetail(string body = "Hi,\n\nDraft body.") =>
        new(
            11,
            ReplyShapes.DirectAnswer,
            "Direct answer",
            0.82,
            body,
            null);
}
