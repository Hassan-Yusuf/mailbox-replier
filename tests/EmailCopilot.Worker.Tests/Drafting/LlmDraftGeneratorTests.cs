namespace EmailCopilot.Worker.Tests;

[TestFixture]
public sealed class LlmDraftGeneratorTests
{
    [Test]
    public void Prompt_should_include_adaptive_length_and_style_rules()
    {
        var prompt = LlmDraftGenerator.BuildPromptForTesting(
            CreateEmail(),
            CreateStyleProfile(),
            [],
            CreateAnalysis(),
            false,
            "Can you confirm if that viewing time works for you?",
            null,
            ReplyShapes.AcknowledgeAndAsk,
            "Acknowledge and ask",
            ["Can you confirm if that viewing time works for you?"],
            []);

        Assert.That(prompt, Does.Contain("Adaptive style guidance derived from the learned profile"));
        Assert.That(prompt, Does.Contain("the user's learned reply style"));
        Assert.That(prompt, Does.Contain("Avoid narrating portal or link-clicking actions"));
        Assert.That(prompt, Does.Contain("Typical reply length is around 1-2 sentences"));
        Assert.That(prompt, Does.Contain("Reply shape label: Acknowledge and ask"));
        Assert.That(prompt, Does.Contain("Selected style segment: domain:loc8me.co.uk"));
    }

    [Test]
    public void Prompt_should_include_blanket_placeholder_rule()
    {
        var prompt = LlmDraftGenerator.BuildPromptForTesting(
            CreateEmail(),
            CreateStyleProfile(),
            [],
            EmailRequestAnalysis.Empty,
            false,
            "Can you confirm if that viewing time works for you?",
            null,
            ReplyShapes.Acknowledge,
            "Acknowledge only",
            [],
            []);

        Assert.That(prompt, Does.Contain("Availability, experience claims, acceptance or rejection of an offer"));
    }

    [Test]
    public void Draft_issue_detection_should_still_flag_general_aiisms()
    {
        var issues = LlmDraftGenerator.DetectDraftIssuesForTesting(
            "Hi,\n\nThank you for reaching out. I appreciate your message and look forward to hearing from you.");

        Assert.That(issues, Has.Some.EqualTo("filler gratitude opener"));
        Assert.That(issues, Has.Some.EqualTo("hollow appreciation"));
    }

    [Test]
    public void Draft_issue_detection_should_flag_filler_offer_of_further_help()
    {
        var issues = LlmDraftGenerator.DetectDraftIssuesForTesting(
            "Hi,\n\nPlease let me know if you need any further information from me.");

        Assert.That(issues, Is.Not.Empty);
        Assert.That(issues, Has.Some.EqualTo("filler offer of further help"));
    }

    [Test]
    public void AiIsm_should_flag_bare_let_me_know_offer()
    {
        var issues = LlmDraftGenerator.DetectDraftIssuesForTesting(
            "Hi,\n\nLet me know if there's anything else I can help with.");

        Assert.That(issues, Has.Some.EqualTo("filler offer of further help"));
    }

    [Test]
    public void AiIsm_should_flag_please_let_me_know_offer()
    {
        var issues = LlmDraftGenerator.DetectDraftIssuesForTesting(
            "Hi,\n\nPlease let me know if you need anything else from me.");

        Assert.That(issues, Has.Some.EqualTo("filler offer of further help"));
    }

    [Test]
    public void Draft_issue_detection_should_not_flag_clean_direct_reply()
    {
        var issues = LlmDraftGenerator.DetectDraftIssuesForTesting(
            "Yes, I'll look into it and come back to you.");

        Assert.That(issues, Is.Empty);
    }

    [Test]
    public void Prompt_should_allow_more_detail_for_detailed_profiles()
    {
        var detailedProfile = new StyleProfile(
            "relationship-professional",
            "Hi",
            "Best regards",
            "professional and detailed",
            string.Empty,
            [],
            22.0,
            0.8,
            0.75,
            0.35,
            0.02,
            0.25,
            0.15,
            0.02,
            0.3,
            2,
            4,
            0.82,
            25,
            DateTimeOffset.UtcNow);

        var prompt = LlmDraftGenerator.BuildPromptForTesting(
            CreateEmail(),
            detailedProfile,
            [],
            CreateAnalysis(),
            false,
            "Could you send over the agenda and let me know whether there are any materials I should review beforehand?",
            "Sophie",
            ReplyShapes.DirectAnswer,
            "Direct answer",
            ["Could you send over the agenda?", "Let me know whether there are any materials I should review beforehand?"],
            []);

        Assert.That(prompt, Does.Contain("Typical reply length is around 2-4 sentences"));
        Assert.That(prompt, Does.Contain("A sign-off can be natural"));
        Assert.That(prompt, Does.Contain("Answer the sender directly in the first sentence"));
    }

    [Test]
    public void Prompt_should_include_confirm_and_request_guidance_when_that_intent_is_selected()
    {
        var prompt = LlmDraftGenerator.BuildPromptForTesting(
            CreateEmail(),
            CreateStyleProfile(),
            [],
            CreateAnalysis(),
            false,
            "The hearing is no longer going ahead.",
            "Sophie",
            ReplyShapes.ConfirmAndRequest,
            "Confirm and request detail",
            ["Can you confirm the next step?"],
            []);

        Assert.That(prompt, Does.Contain("Reply shape: CONFIRM_AND_REQUEST"));
        Assert.That(prompt, Does.Contain("Reply shape label: Confirm and request detail"));
        Assert.That(prompt, Does.Contain("request one specific missing detail or next-step clarification"));
    }

    [Test]
    public void Prompt_should_include_defer_guidance_for_acknowledge_shape()
    {
        var prompt = LlmDraftGenerator.BuildPromptForTesting(
            CreateEmail(),
            CreateStyleProfile(),
            [],
            CreateAnalysis(),
            false,
            "A role is available if you're interested.",
            "Sophie",
            ReplyShapes.Acknowledge,
            "Acknowledge only",
            ["Let me know if you're interested."],
            []);

        Assert.That(prompt, Does.Contain("defer"));
    }

    [Test]
    public void Prompt_should_include_example_block_when_examples_are_provided()
    {
        var prompt = LlmDraftGenerator.BuildPromptForTesting(
            CreateEmail(),
            CreateStyleProfile(),
            CreateStyleExamples(),
            CreateAnalysis(),
            false,
            "Can you confirm if that viewing time works for you?",
            "Sophie",
            ReplyShapes.DirectAnswer,
            "Direct answer",
            ["Can you confirm if that viewing time works for you?"],
            []);

        Assert.That(prompt, Does.Contain("Style examples - how this user actually writes replies."));
        Assert.That(prompt, Does.Contain("[RE:"));
        Assert.That(prompt, Does.Contain("Adaptive style guidance derived from the learned profile"));
    }

    [Test]
    public void Prompt_should_fall_back_to_adaptive_guidance_when_no_examples_are_provided()
    {
        var prompt = LlmDraftGenerator.BuildPromptForTesting(
            CreateEmail(),
            CreateStyleProfile(),
            [],
            CreateAnalysis(),
            false,
            "Can you confirm if that viewing time works for you?",
            null,
            ReplyShapes.AcknowledgeAndAsk,
            "Acknowledge and ask",
            ["Can you confirm if that viewing time works for you?"],
            []);

        Assert.That(prompt, Does.Contain("Adaptive style guidance derived from the learned profile"));
        Assert.That(prompt, Does.Not.Contain("[RE:"));
    }

    [Test]
    public void DirectAnswer_prompt_contains_strong_placeholder_instruction_when_personal_confirmation_true()
    {
        var prompt = LlmDraftGenerator.BuildPromptForTesting(
            CreateEmail(),
            CreateStyleProfile(),
            [],
            CreateAnalysis(requiresPersonalConfirmation: true),
            true,
            "Can you work 8am-4pm tomorrow at the cafe in Braintree?",
            "Sophie",
            ReplyShapes.DirectAnswer,
            "Direct answer",
            ["Can you work 8am-4pm tomorrow at the cafe in Braintree?"],
            []);

        Assert.That(prompt, Does.Contain("You CANNOT know"));
    }

    [Test]
    public void Prompt_should_include_phone_reply_voice_and_shape_override_rules()
    {
        var prompt = LlmDraftGenerator.BuildPromptForTesting(
            CreateEmail(),
            CreateStyleProfile(),
            [],
            CreateAnalysis(),
            false,
            "Can you confirm if that viewing time works for you?",
            null,
            ReplyShapes.AcknowledgeAndAsk,
            "Acknowledge and ask",
            ["Can you confirm if that viewing time works for you?"],
            []);

        Assert.That(prompt, Does.Contain("Sound like a person quickly replying from their phone"));
        Assert.That(prompt, Does.Contain("When the reply-shape TONE conflicts with the style profile"));
    }

    [Test]
    public void Each_reply_shape_should_include_a_tone_prefix()
    {
        var shapes = new[]
        {
            (ReplyShapes.DirectAnswer, "TONE: answer the actual question directly"),
            (ReplyShapes.Acknowledge, "TONE: acknowledge receipt only"),
            (ReplyShapes.AcknowledgeAndAsk, "TONE: acknowledge briefly, then ask the one clarifying question"),
            (ReplyShapes.ConfirmAndClose, "TONE: confirm understanding, close the loop"),
            (ReplyShapes.ConfirmAndRequest, "TONE: confirm understanding, then make one specific request"),
            (ReplyShapes.Decline, "TONE: decline politely")
        };

        foreach (var (shape, expectedTone) in shapes)
        {
            var prompt = LlmDraftGenerator.BuildPromptForTesting(
                CreateEmail(),
                CreateStyleProfile(),
                [],
                CreateAnalysis(),
                false,
                "Body text",
                "Sender",
                shape,
                shape,
                [],
                []);

            Assert.That(prompt, Does.Contain(expectedTone), $"shape {shape} missing tone prefix");
        }
    }

    [Test]
    public void Prompt_includes_adaptive_guidance_even_when_examples_present()
    {
        var prompt = LlmDraftGenerator.BuildPromptForTesting(
            CreateEmail(),
            CreateStyleProfile(),
            CreateStyleExamples(),
            CreateAnalysis(),
            false,
            "Can you confirm if that viewing time works for you?",
            "Sophie",
            ReplyShapes.DirectAnswer,
            "Direct answer",
            ["Can you confirm if that viewing time works for you?"],
            []);

        Assert.That(prompt, Does.Contain("Adaptive style guidance derived from the learned profile"));
        Assert.That(prompt, Does.Contain("Typical reply length is around"));
        Assert.That(prompt, Does.Contain("[RE:"));
    }

    [Test]
    public void Voice_signals_lowercase_i_emitted_when_pattern_present()
    {
        var examples = Enumerable.Range(0, 10)
            .Select(index => new StyleExample(
                "domain:example.com",
                $"Thanks, i will check that and come back to you shortly. Reply {index}.",
                $"S{index}",
                new DateTimeOffset(2026, 4, 1, 9, 0, 0, TimeSpan.Zero).AddDays(index)))
            .ToList();

        var signals = LlmDraftGenerator.BuildVoiceSignalsForTesting(examples);

        Assert.That(signals, Does.Contain("lowercase \"i\" mid-sentence"));
    }

    [Test]
    public void Voice_signals_not_emitted_when_thresholds_not_met()
    {
        var examples = Enumerable.Range(0, 39)
            .Select(index =>
            {
                var body = index == 0
                    ? "Thanks, i will check that and come back to you shortly."
                    : "Thanks, will check that and come back to you shortly.";
                return new StyleExample(
                    "domain:example.com",
                    body,
                    $"S{index}",
                    new DateTimeOffset(2026, 4, 1, 9, 0, 0, TimeSpan.Zero).AddDays(index));
            })
            .ToList();

        var signals = LlmDraftGenerator.BuildVoiceSignalsForTesting(examples);

        Assert.That(signals, Does.Not.Contain("lowercase \"i\" mid-sentence"));
    }

    [Test]
    public void Voice_signals_emit_discourse_markers_when_two_or_more_observed()
    {
        var examples = new List<StyleExample>
        {
            new("domain:example.com", "Yes, that works.", "S1", new DateTimeOffset(2026, 4, 1, 9, 0, 0, TimeSpan.Zero)),
            new("domain:example.com", "Sounds good.", "S2", new DateTimeOffset(2026, 4, 2, 9, 0, 0, TimeSpan.Zero))
        };
        var markers = new List<DiscourseMarkerObservation>
        {
            new("yeah", 0.34),
            new("anyway", 0.12),
            new("tbh", 0.08)
        };

        var signals = LlmDraftGenerator.BuildVoiceSignalsForTesting(examples, markers);

        Assert.That(signals, Does.Contain("The user often uses:"));
        Assert.That(signals, Does.Contain("\"yeah\""));
        Assert.That(signals, Does.Contain("\"anyway\""));
    }

    [Test]
    public void Voice_signals_do_not_emit_discourse_markers_below_two_observed()
    {
        var examples = new List<StyleExample>
        {
            new("domain:example.com", "Yes, that works.", "S1", new DateTimeOffset(2026, 4, 1, 9, 0, 0, TimeSpan.Zero))
        };
        var markers = new List<DiscourseMarkerObservation>
        {
            new("yeah", 0.34)
        };

        var signals = LlmDraftGenerator.BuildVoiceSignalsForTesting(examples, markers);

        Assert.That(signals, Does.Not.Contain("The user often uses:"));
    }

    [Test]
    public void Voice_signals_median_word_count_present()
    {
        var examples = new List<StyleExample>
        {
            new("domain:example.com", "Yes, that works.", "S1", new DateTimeOffset(2026, 4, 1, 9, 0, 0, TimeSpan.Zero)),
            new("domain:example.com", "Sounds good to me, will do.", "S2", new DateTimeOffset(2026, 4, 2, 9, 0, 0, TimeSpan.Zero)),
            new("domain:example.com", "I'll send the file shortly.", "S3", new DateTimeOffset(2026, 4, 3, 9, 0, 0, TimeSpan.Zero))
        };

        var signals = LlmDraftGenerator.BuildVoiceSignalsForTesting(examples);

        Assert.That(signals, Does.Match(@"Median reply length: \d+ words\."));
    }

    [Test]
    public void Voice_signals_emoji_whitelist_only()
    {
        var examples = Enumerable.Range(0, 10)
            .Select(index => new StyleExample(
                "domain:example.com",
                $"Thanks, will sort it out today. \U0001F680 Reply {index}.",
                $"S{index}",
                new DateTimeOffset(2026, 4, 1, 9, 0, 0, TimeSpan.Zero).AddDays(index)))
            .ToList();

        var signals = LlmDraftGenerator.BuildVoiceSignalsForTesting(examples);

        Assert.That(signals, Does.Not.Contain("emoji"));
    }

    [Test]
    public void Voice_signals_whitelisted_emoji_triggers_signal()
    {
        var examples = Enumerable.Range(0, 10)
            .Select(index => new StyleExample(
                "domain:example.com",
                $"Much thanks for sorting that one. \U0001F64F Reply {index}.",
                $"S{index}",
                new DateTimeOffset(2026, 4, 1, 9, 0, 0, TimeSpan.Zero).AddDays(index)))
            .ToList();

        var signals = LlmDraftGenerator.BuildVoiceSignalsForTesting(examples);

        Assert.That(signals, Does.Contain("single emoji at the end"));
    }

    [Test]
    public void DirectAnswer_prompt_does_not_contain_strong_placeholder_when_personal_confirmation_false()
    {
        var prompt = LlmDraftGenerator.BuildPromptForTesting(
            CreateEmail(),
            CreateStyleProfile(),
            [],
            CreateAnalysis(requiresPersonalConfirmation: false),
            false,
            "Can you send the readings by Friday?",
            "Sophie",
            ReplyShapes.DirectAnswer,
            "Direct answer",
            ["Can you send the readings by Friday?"],
            []);

        Assert.That(prompt, Does.Not.Contain("You CANNOT know"));
    }

    private static IncomingEmail CreateEmail() =>
        new(
            1,
            Guid.NewGuid().ToString("N"),
            EmailAddress.FromParts("enquiries@loc8me.co.uk", "Enquiries"),
            "Notification of Viewing",
            "Can you confirm if the viewing time works for you?",
            DateTimeOffset.UtcNow,
            false,
            false,
            string.Empty,
            false);

    private static StyleProfile CreateStyleProfile() =>
        new(
            "domain:loc8me.co.uk",
            "Hi",
            "Thank you",
            "professional and concise",
            string.Empty,
            [],
            8.0,
            0.7,
            0.1,
            0.45,
            0.02,
            0.05,
            0.55,
            0.2,
            0.25,
            1,
            2,
            0.4,
            12,
            DateTimeOffset.UtcNow);

    private static EmailRequestAnalysis CreateAnalysis(bool requiresPersonalConfirmation = false) =>
        new(
            [new EmailAsk("Can you confirm if that viewing time works for you?", AskTypes.Question, false)],
            [],
            [],
            UrgencyLevels.Low,
            requiresPersonalConfirmation);

    private static IReadOnlyList<StyleExample> CreateStyleExamples() =>
    [
        new(
            "domain:loc8me.co.uk",
            "Hi Sophie,\n\nYes, that works for me. I'll be there around 2.",
            "Viewing time",
            new DateTimeOffset(2026, 4, 17, 9, 0, 0, TimeSpan.Zero)),
        new(
            "domain:loc8me.co.uk",
            "Hi,\n\nI'll send that over shortly once I have it ready.",
            "Sending documents",
            new DateTimeOffset(2026, 4, 16, 10, 0, 0, TimeSpan.Zero)),
        new(
            "domain:loc8me.co.uk",
            "Hi Sam,\n\nThat should be fine from my side.",
            "Availability",
            new DateTimeOffset(2026, 4, 15, 11, 0, 0, TimeSpan.Zero)),
        new(
            "domain:loc8me.co.uk",
            "Hi,\n\nI'll check and come back to you later today.",
            "Checking details",
            new DateTimeOffset(2026, 4, 14, 12, 0, 0, TimeSpan.Zero))
    ];
}
