namespace EmailCopilot.Worker.Tests;

[TestFixture]
public sealed class StaticAvoidPhrasesTests
{
    private static readonly string[] AllShapes =
    [
        ReplyShapes.DirectAnswer,
        ReplyShapes.Acknowledge,
        ReplyShapes.AcknowledgeAndAsk,
        ReplyShapes.ConfirmAndClose,
        ReplyShapes.ConfirmAndRequest,
        ReplyShapes.Decline,
        ReplyShapes.GeneralReply
    ];

    [Test]
    public void Every_reply_shape_has_at_least_one_avoid_phrase()
    {
        foreach (var shape in AllShapes)
        {
            var phrases = StaticAvoidPhrases.ForShape(shape);
            Assert.That(phrases, Is.Not.Empty, $"Shape {shape} should have at least one avoid phrase.");
        }
    }

    [Test]
    public void No_duplicate_phrases_within_a_shape()
    {
        foreach (var shape in AllShapes)
        {
            var phrases = StaticAvoidPhrases.ForShape(shape);
            var distinct = phrases.Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
            Assert.That(distinct, Has.Length.EqualTo(phrases.Count), $"Shape {shape} contains duplicate phrases.");
        }
    }

    [Test]
    public void Unknown_shape_returns_empty_list()
    {
        var phrases = StaticAvoidPhrases.ForShape("NOT_A_REAL_SHAPE");

        Assert.That(phrases, Is.Empty);
    }

    [Test]
    public void Null_or_empty_shape_returns_empty_list()
    {
        Assert.That(StaticAvoidPhrases.ForShape(string.Empty), Is.Empty);
        Assert.That(StaticAvoidPhrases.ForShape("   "), Is.Empty);
    }

    [Test]
    public void Direct_answer_blocks_audit_observed_template_closers()
    {
        var phrases = StaticAvoidPhrases.ForShape(ReplyShapes.DirectAnswer);

        Assert.That(phrases, Has.Some.EqualTo("Please let me know if that works"));
        Assert.That(phrases, Has.Some.EqualTo("Please let me know if you need anything else"));
    }

    [Test]
    public void Acknowledge_blocks_audit_observed_get_back_to_you_filler()
    {
        var phrases = StaticAvoidPhrases.ForShape(ReplyShapes.Acknowledge);

        Assert.That(phrases, Has.Some.EqualTo("I'll check my schedule and get back to you"));
        Assert.That(phrases, Has.Some.EqualTo("I'll get back to you shortly"));
    }

    [Test]
    public void Acknowledge_blocks_observed_synonym_evasion_variants()
    {
        var phrases = StaticAvoidPhrases.ForShape(ReplyShapes.Acknowledge);

        Assert.That(phrases, Has.Some.EqualTo("I'll check my availability and get back to you"));
        Assert.That(phrases, Has.Some.EqualTo("I'll check and let you know"));
    }

    [Test]
    public void Direct_answer_blocks_observed_word_drop_evasion_variant()
    {
        var phrases = StaticAvoidPhrases.ForShape(ReplyShapes.DirectAnswer);

        Assert.That(phrases, Has.Some.EqualTo("Please let me know if that works"));
        Assert.That(phrases, Has.Some.EqualTo("Let me know if that works"));
    }

    [Test]
    public void Confirm_and_request_blocks_audit_observed_corporate_clarifications()
    {
        var phrases = StaticAvoidPhrases.ForShape(ReplyShapes.ConfirmAndRequest);

        Assert.That(phrases, Has.Some.EqualTo("Could you please provide more details about"));
        Assert.That(phrases, Has.Some.EqualTo("Could you clarify if there's a specific"));
    }

    [Test]
    public void Decline_blocks_audit_observed_softening_phrases()
    {
        var phrases = StaticAvoidPhrases.ForShape(ReplyShapes.Decline);

        Assert.That(phrases, Has.Some.EqualTo("Let me know if there are other opportunities in the future"));
        Assert.That(phrases, Has.Some.EqualTo("Thanks for understanding"));
    }

    [Test]
    public void General_reply_blocks_audit_observed_corporate_filler()
    {
        var phrases = StaticAvoidPhrases.ForShape(ReplyShapes.GeneralReply);

        Assert.That(phrases, Has.Some.EqualTo("Thank you so much for your email"));
        Assert.That(phrases, Has.Some.EqualTo("I trust this message finds you well"));
        Assert.That(phrases, Has.Some.EqualTo("Should you have any further questions"));
        Assert.That(phrases, Has.Some.EqualTo("Feel free to let me know if you need anything else"));
    }

    [Test]
    public void No_shape_exceeds_seven_avoid_phrases_to_stay_within_prompt_cap()
    {
        foreach (var shape in AllShapes)
        {
            var phrases = StaticAvoidPhrases.ForShape(shape);
            Assert.That(phrases, Has.Count.LessThanOrEqualTo(7), $"Shape {shape} exceeds the 7-phrase prompt cap.");
        }
    }
}
