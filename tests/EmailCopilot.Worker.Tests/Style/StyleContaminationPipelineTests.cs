namespace EmailCopilot.Worker.Tests;

[TestFixture]
public sealed class StyleContaminationPipelineTests
{
    [Test]
    public void Authored_body_pipeline_should_stop_at_quote_boundary()
    {
        var pipeline = new StyleAuthoredBodyPipeline();
        var body = """
            Hi Sophie,

            I can do Sunday.

            On Tue, someone wrote:
            > Previous message
            """;

        var extracted = pipeline.Extract(body);

        Assert.That(extracted, Does.Contain("I can do Sunday."));
        Assert.That(extracted, Does.Not.Contain("Previous message"));
    }

    [Test]
    public void Authored_body_pipeline_should_skip_boilerplate_lines()
    {
        var pipeline = new StyleAuthoredBodyPipeline();
        var body = """
            Hi,
            Sent from my iPhone
            Get Outlook for iOS
            Thanks
            """;

        var extracted = pipeline.Extract(body);

        Assert.That(extracted, Does.Contain("Hi,"));
        Assert.That(extracted, Does.Contain("Thanks"));
        Assert.That(extracted, Does.Not.Contain("Sent from my iPhone"));
        Assert.That(extracted, Does.Not.Contain("Get Outlook for iOS"));
    }

    [Test]
    public void Sentence_filter_should_reject_metadata_and_urls()
    {
        var pipeline = new StyleSentenceFilterPipeline();

        Assert.That(pipeline.ShouldKeep("View in browser at https://example.com"), Is.False);
        Assert.That(pipeline.ShouldKeep("Name: Hassan Yusuf"), Is.False);
    }

    [Test]
    public void Sentence_filter_should_reject_known_contamination_markers()
    {
        var pipeline = new StyleSentenceFilterPipeline();

        Assert.That(pipeline.ShouldKeep("Hassan Yusuf shared the folder \"Game Recordings\" with you."), Is.False);
        Assert.That(pipeline.ShouldKeep("And this is a highlight tape from the season before in D3:"), Is.False);
    }

    [Test]
    public void Sentence_filter_should_keep_clean_authored_sentence()
    {
        var pipeline = new StyleSentenceFilterPipeline();

        Assert.That(pipeline.ShouldKeep("I can do Sunday if that still works for you."), Is.True);
    }
}
