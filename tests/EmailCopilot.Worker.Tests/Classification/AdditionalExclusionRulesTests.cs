namespace EmailCopilot.Worker.Tests;

[TestFixture]
public sealed class AdditionalExclusionRulesTests
{
    private readonly BuiltInExclusionClassifier _classifier = new();

    [Test]
    public async Task Should_skip_customer_feedback_survey_email()
    {
        var email = CreateEmail(
            "team@e.thortful.com",
            "thortful",
            "Did we miss the mark?",
            "Tell us how we did and rate your experience with your recent card order.");

        var result = await _classifier.ClassifyAsync(email);

        Assert.That(result.RequiresReply, Is.False);
        Assert.That(result.ReasonCode, Is.EqualTo(ClassificationReasonCodes.FeedbackSurveyRequest));
    }

    [Test]
    public async Task Should_not_skip_plain_human_email_as_customer_feedback_survey()
    {
        var email = CreateEmail(
            "hayley@acreproperties.com",
            "Hayley",
            "Dehumidifiers",
            "Hi Hassan, can you confirm whether there is a dehumidifier at the property?");

        var result = await _classifier.ClassifyAsync(email);

        Assert.That(result.RequiresReply, Is.True);
        Assert.That(result.ReasonCode, Is.EqualTo(ClassificationReasonCodes.DefaultReply));
    }

    [Test]
    public async Task Should_skip_satisfaction_questionnaire_email()
    {
        var email = CreateEmail(
            "research@progressivepartnership.co.uk",
            "Progressive Partnership",
            "Satisfaction with SLC",
            "Please complete a short questionnaire about your experience with SLC.");

        var result = await _classifier.ClassifyAsync(email);

        Assert.That(result.RequiresReply, Is.False);
        Assert.That(result.ReasonCode, Is.EqualTo(ClassificationReasonCodes.FeedbackSurveyRequest));
    }

    [Test]
    public async Task Should_skip_undecoded_mime_body()
    {
        var email = CreateEmail(
            "mailer@example.com",
            "Mailer",
            "Encoded body",
            "Hello=3Cbr=3EYour code is=20here=3D42");

        var result = await _classifier.ClassifyAsync(email);

        Assert.That(result.RequiresReply, Is.False);
        Assert.That(result.ReasonCode, Is.EqualTo(ClassificationReasonCodes.UndecodedBody));
    }

    [Test]
    public async Task Should_not_skip_plain_text_body_as_undecoded_mime()
    {
        var email = CreateEmail(
            "person@example.com",
            "Person",
            "Question",
            "Hello there, can you send the notes from yesterday?");

        var result = await _classifier.ClassifyAsync(email);

        Assert.That(result.RequiresReply, Is.True);
        Assert.That(result.ReasonCode, Is.EqualTo(ClassificationReasonCodes.DefaultReply));
    }

    [Test]
    public async Task Should_not_flag_url_query_strings_as_undecoded_mime()
    {
        // Regression: URL query params like "?ref_=fed_yo_default" contain "=fe", "=fa", "=fb"
        // which the old naive =[0-9A-Fa-f]{2} regex matched as QP escapes. Decoded Amazon/Quora/
        // LinkedIn bodies were being skipped as UNDECODED_BODY.
        var body =
            "Your Orders https://www.amazon.co.uk/gp/css/order-history?ref_=fed_yo_default\n" +
            "Your Account https://www.amazon.co.uk/your-account?ref_=fed_ya_default\n" +
            "Buy Again https://www.amazon.co.uk/gp/buyagain?ref_=fed_bia_default\n" +
            "Your package was dispatched!";

        var email = CreateEmail("shipment-tracking@amazon.co.uk", "Amazon", "Dispatched", body);

        var result = await _classifier.ClassifyAsync(email);

        Assert.That(result.ReasonCode, Is.Not.EqualTo(ClassificationReasonCodes.UndecodedBody));
    }

    [Test]
    public async Task Should_flag_soft_line_break_as_undecoded_mime()
    {
        // A literal "=" at end of line followed by newline is the unambiguous QP soft-line-break
        // marker. Even a single occurrence is enough to flag the body as undecoded.
        var email = CreateEmail(
            "mailer@example.com",
            "Mailer",
            "Encoded",
            "This line continues onto=\nthe next one without a real break.");

        var result = await _classifier.ClassifyAsync(email);

        Assert.That(result.RequiresReply, Is.False);
        Assert.That(result.ReasonCode, Is.EqualTo(ClassificationReasonCodes.UndecodedBody));
    }

    [Test]
    public async Task Should_flag_sparse_latin1_qp_escapes_as_undecoded_mime()
    {
        // Latin-1 / Windows-1252 QP bodies have isolated high-byte escapes (no consecutive
        // multi-byte UTF-8 pairs). Example: "caf=E9", "r=E9sum=E9", "Price =A325" appear
        // in many older mailers. Three or more such tokens in URL-stripped prose still
        // count as undecoded.
        var email = CreateEmail(
            "mailer@example.com",
            "Mailer",
            "Encoded",
            "Bonjour, voici votre caf=E9 et r=E9sum=E9 pour aujourd'hui.");

        var result = await _classifier.ClassifyAsync(email);

        Assert.That(result.RequiresReply, Is.False);
        Assert.That(result.ReasonCode, Is.EqualTo(ClassificationReasonCodes.UndecodedBody));
    }

    [Test]
    public async Task Should_flag_genuine_qp_escape_tokens_as_undecoded_mime()
    {
        // Real QP escapes: =20 (space), =A0 (non-breaking space), =E2 (UTF-8 lead byte).
        // Three or more such tokens in a body indicate an undecoded MIME part.
        var email = CreateEmail(
            "mailer@example.com",
            "Mailer",
            "Encoded",
            "Hello=20world=A0this=20is=E2=80=99 undecoded body content.");

        var result = await _classifier.ClassifyAsync(email);

        Assert.That(result.RequiresReply, Is.False);
        Assert.That(result.ReasonCode, Is.EqualTo(ClassificationReasonCodes.UndecodedBody));
    }

    [Test]
    public async Task Should_skip_prize_giveaway_subject_without_confusable_letters()
    {
        var email = CreateEmail(
            "customerservice@mh.familyaginglifecare.com",
            "Customer Service",
            "Congratulations! Claim your Free Yeti Rambler Tumbler#CABH19",
            "SELECT YOUR LOCAL\r\n\r\nDUNHAM'S STORE\r\n\r\ndf");

        var result = await _classifier.ClassifyAsync(email);

        Assert.That(result.RequiresReply, Is.False);
        Assert.That(result.ReasonCode, Is.EqualTo(ClassificationReasonCodes.SuspiciousOrSpam));
    }

    [Test]
    public async Task Should_skip_scare_tactic_phishing_subject()
    {
        var email = CreateEmail(
            "customerservice@mh.familyaginglifecare.com",
            "Customer Service",
            "We've received 62 complaints about your Email -",
            "SELECT YOUR LOCAL\r\n\r\nDUNHAM'S STORE\r\n\r\ndf");

        var result = await _classifier.ClassifyAsync(email);

        Assert.That(result.RequiresReply, Is.False);
        Assert.That(result.ReasonCode, Is.EqualTo(ClassificationReasonCodes.SuspiciousOrSpam));
    }

    private static IncomingEmail CreateEmail(
        string fromAddress,
        string displayName,
        string subject,
        string bodyText) =>
        new(
            1,
            Guid.NewGuid().ToString("N"),
            EmailAddress.FromParts(fromAddress, displayName),
            subject,
            bodyText,
            DateTimeOffset.UtcNow,
            false,
            false,
            string.Empty,
            false);
}
