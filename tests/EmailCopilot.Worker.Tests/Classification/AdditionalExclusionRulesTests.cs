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
