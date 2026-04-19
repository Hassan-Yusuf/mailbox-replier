namespace EmailCopilot.Worker.Tests;

[TestFixture]
public sealed class ClassificationRegressionTests
{
    private readonly BuiltInExclusionClassifier _classifier = new();

    public static IEnumerable<TestCaseData> KnownSkipCases()
    {
        yield return new TestCaseData(
            CreateEmail("donotreply@tfl.gov.uk", "TfL", "Your card's been approved"),
            ClassificationReasonCodes.NoReplySender);
        yield return new TestCaseData(
            CreateEmail("auto-confirm@amazon.co.uk", "Amazon", "Ordered: Trust Roha II USB Headset"),
            ClassificationReasonCodes.NoReplySender);
        yield return new TestCaseData(
            CreateEmail("info@trading212.com", "Trading 212", "Account Activity statement"),
            ClassificationReasonCodes.NoReplySender);
        yield return new TestCaseData(
            CreateEmail("bbc@bbc.co.uk", "BBC", "Your opinion matters to the BBC"),
            ClassificationReasonCodes.ListOrBroadcastMail);
        yield return new TestCaseData(
            CreateEmail("thekitchen@dominos.co.uk", "Domino's Pizza", "Order confirmation"),
            ClassificationReasonCodes.TransactionalNotification);
        yield return new TestCaseData(
            CreateEmail("eino.fa.sender@workflow.mail.us2.cloud.oracle.com", "JPMorgan", "We received your job application"),
            ClassificationReasonCodes.WorkflowAcknowledgement);
        yield return new TestCaseData(
            CreateEmail("notification@smartrecruiters.com", "Netcompany", "Thank you for your interest in Netcompany", bodyText: "Dear Hassan,\r\nMany thanks for your application for the position of Junior Software Developer.\r\nYour details have been forwarded to our Recruitment team for review. Unfortunately, due to a high level of interest, we are unable to personally speak with everyone that applies."),
            ClassificationReasonCodes.WorkflowAcknowledgement);
        yield return new TestCaseData(
            CreateEmail("careers@deeset.co.uk", "Deeset", "Action Required. Renewal of consent to be contacted about new vacancies"),
            ClassificationReasonCodes.SelfServiceAction);
        yield return new TestCaseData(
            CreateEmail("reply@linkedin.com", "LinkedIn", "Weekly update"),
            ClassificationReasonCodes.ListOrBroadcastMail);
        yield return new TestCaseData(
            CreateEmail("support@mailer.alpaca.markets", "Alpaca", "Your Monthly Account Statement is now available"),
            ClassificationReasonCodes.NoReplySender);
        yield return new TestCaseData(
            CreateEmail("login@indeed.com", "Indeed", "Indeed one-time passcode"),
            ClassificationReasonCodes.NoReplySender);
        yield return new TestCaseData(
            CreateEmail("mail@eg.expedia.com", "Expedia", "Changes to our One Key loyalty programme"),
            ClassificationReasonCodes.ListOrBroadcastMail);
        yield return new TestCaseData(
            CreateEmail("ebay@info.ebay.co.uk", "eBay", "Updates to our User Privacy Notice 2025"),
            ClassificationReasonCodes.NoReplySender);
        yield return new TestCaseData(
            CreateEmail("newsletter@product.example", "Product News", "Weekly update", bodyText: "View in browser and manage preferences here."),
            ClassificationReasonCodes.ListOrBroadcastMail);
        yield return new TestCaseData(
            CreateEmail("payments@utility.example", "Utility Payments", "Payment confirmation"),
            ClassificationReasonCodes.TransactionalNotification);
        yield return new TestCaseData(
            CreateEmail("yourorder@dpd.co.uk", "DPD", "We're expecting your Musclefood parcel"),
            ClassificationReasonCodes.TransactionalNotification);
        yield return new TestCaseData(
            CreateEmail("store+81569251677@t.shopifyemail.com", "Shopify", "Order MF32971 confirmed"),
            ClassificationReasonCodes.TransactionalNotification);
        yield return new TestCaseData(
            CreateEmail("workatastartup@ycombinator.com", "Work at a Startup", "Your job profile is no longer being shared"),
            ClassificationReasonCodes.SelfServiceAction);
        yield return new TestCaseData(
            CreateEmail("carmen.tempesta@atomlearning.teamtailor-mail.com", "Carmen Tempesta", "Welcome to Atom Learning", bodyText: "Start by introducing yourself on your personal profile. A good and informative profile will help us find a role for you."),
            ClassificationReasonCodes.SelfServiceAction);
        yield return new TestCaseData(
            CreateEmail("support@supabase.com", "Supabase", "OAuth Application Approval", bodyText: "A new OAuth application was authorized to have access to your organization's settings and projects. If this seems unauthorized, you can immediately revoke access."),
            ClassificationReasonCodes.SelfServiceAction);
        yield return new TestCaseData(
            CreateEmail("service@veo.co", "Veo", "The Veo recording titled \"25 Sept 2025, 21:38:41\" was assigned to your team Greenwich Titans Men", bodyText: "The Veo recording titled \"25 Sept 2025, 21:38:41\" was assigned to your team Greenwich Titans Men and is ready to watch. Watch your video."),
            ClassificationReasonCodes.TransactionalNotification);
        yield return new TestCaseData(
            CreateEmail("service@veo.co", "Veo", "Ashley Thomas tagged you in Greenwich Titans v Westminster Warriors on Veo!", bodyText: "Hi Hassan Yusuf\r\n\r\nAshley Thomas tagged you in a video.\r\n\r\nAshley Thomas wrote 'This is ridiculous now!!!! Noone is aware of anything on the floor.'\r\n\r\nWatch the highlight."),
            ClassificationReasonCodes.SocialOrDigest);
        yield return new TestCaseData(
            CreateEmail("support@jobtrain.co.uk", "Jobtrain", "Your information with Roke Manor Research Limited", bodyText: "Our records indicate that you have previously registered for positions with Roke Manor Research Limited and that your last account activity was 2 years ago. If you wish to keep your account active and would like to be considered for future opportunities, please click here."),
            ClassificationReasonCodes.SelfServiceAction);
        yield return new TestCaseData(
            CreateEmail("cs@humanforest.co.uk", "Forest", "Your trip has been automatically parked", bodyText: "Your trip has been automatically parked. You need to open the app to unpause your ride before it ends in 5 minutes."),
            ClassificationReasonCodes.SelfServiceAction);
        yield return new TestCaseData(
            CreateEmail("cs@humanforest.co.uk", "Forest", "Your trip ended automatically", bodyText: "Your trip ended due to 10 minutes of inactivity. If you need help ending your ride or have questions, reach out to Support."),
            ClassificationReasonCodes.SelfServiceAction);
        yield return new TestCaseData(
            CreateEmail("cs@humanforest.co.uk", "Forest", "Earn 1 free minute - Got 30 secs to rate Forest Support?", bodyText: "How helpful was our Support Agent today? Tap to rate."),
            ClassificationReasonCodes.FeedbackSurveyRequest);
        yield return new TestCaseData(
            CreateEmail("service.hft@gardium.com", "Stαrlınk", "Clαım Your Free Stαrlınk Mini Kit", bodyText: "Hi renaeutengler, 68b0t0JCxR35lu \" 8g6OnVdm7K725c Please find zpA968UFrZA7wG attached Kzxw1KIb9wWvFj sheet for your reference. \" \" Aditya Awasthi\"|\"\"Avaya\"|\"1st floor\"|\"Vipul Plaza\""),
            ClassificationReasonCodes.SuspiciousOrSpam);
        yield return new TestCaseData(
            CreateEmail("tickets@kiwi.com", "Kiwi.com", "Booking 688156161: Please check in with the carrier for your flight London → Thessaloniki", bodyText: "Online check-in is now open! Please check in with the carrier for your flight."),
            ClassificationReasonCodes.TransactionalNotification);
        yield return new TestCaseData(
            CreateEmail("membership@e-mail.totum.com", "TOTUM", "We've updated our T&Cs and Privacy Policy", bodyText: "Grab your info inside View Online Privacy Policy Terms and Conditions"),
            ClassificationReasonCodes.BroadcastFormattedMail);
        yield return new TestCaseData(
            CreateEmail("curve@emails.curve.com", "Curve", "We’ve got news: Curve is joining forces with Lloyds Banking Group", bodyText: "Curve is joining forces with Lloyds Banking Group. View online for more information."),
            ClassificationReasonCodes.BroadcastFormattedMail);
        yield return new TestCaseData(
            CreateEmail("dex@meetdex.ai", "Dex", "You've Got Matches", bodyText: "You've got matches for the Senior Backend Engineer role at Accurx."),
            ClassificationReasonCodes.ListOrBroadcastMail);
        yield return new TestCaseData(
            CreateEmail("dse_na4@docusign.net", "DocuSign", "Signed: NORTH AMERICA RESEARCH PARTICIPATION AND DATA COLLECTION AGREEMENT", bodyText: "Hello Hassan Yusuf, Thank you for signing your documents online today. If you did not sign the document using Docusign with Outlier Signatures, please contact support."),
            ClassificationReasonCodes.TransactionalNotification);
        yield return new TestCaseData(
            CreateEmail("automated@airbnb.com", "Airbnb", "Write a review for Sharron", bodyText: "Your feedback is private until they review you too."),
            ClassificationReasonCodes.SelfServiceAction);
        yield return new TestCaseData(
            CreateEmail("hello@hinge.co", "Hinge", "Access your data", bodyText: "Access your data"),
            ClassificationReasonCodes.SelfServiceAction);
        yield return new TestCaseData(
            CreateEmail("info@researchdonors.co.uk", "Research Donors", "Feedback from donation", bodyText: "Thank you for your recent donation and for supporting Biomedical research.\r\n\r\nWe would be very grateful if you could take a few moments of your time to tell us about your experience with us.\r\n\r\nhttps://forms.office.com/Pages/ResponsePage.aspx?id=example"),
            ClassificationReasonCodes.ListOrBroadcastMail);
        yield return new TestCaseData(
            CreateEmail("research@progressivepartnership.co.uk", "Progressive Partnership", "Satisfaction with SLC", bodyText: "Please complete a short questionnaire about your experience with SLC."),
            ClassificationReasonCodes.FeedbackSurveyRequest);
        yield return new TestCaseData(
            CreateEmail("team@e.thortful.com", "thortful", "Did we miss the mark? 🎯", bodyText: "Did we miss the mark? Let us know how we did and rate your experience with your recent card order."),
            ClassificationReasonCodes.FeedbackSurveyRequest);
        yield return new TestCaseData(
            CreateEmail("copilot@infomails.microsoft.com", "Microsoft Copilot", "See what a day with Copilot feels like", bodyText: "See what a day with Copilot feels like and explore the latest product updates."),
            ClassificationReasonCodes.AutomatedSender);
    }

    public static IEnumerable<TestCaseData> KnownReplyCases()
    {
        yield return new TestCaseData(
            CreateEmail("carmel.dennison@code3research.co.uk", "Carmel Dennison", "London - Friendship Pairs - Drinking and Social Occasions"));
        yield return new TestCaseData(
            CreateEmail("ross@loc8me.co.uk", "Ross", "Broken chair"));
        yield return new TestCaseData(
            CreateEmail("lee@cateringelite.co.uk", "Lee", "Shift Available This Sunday"));
        yield return new TestCaseData(
            CreateEmail("sophie@cateringelite.co.uk", "Sophie", "Head Chef Vacancy - Colchester"));
    }

    [TestCaseSource(nameof(KnownSkipCases))]
    public async Task Should_skip_known_bad_cases(IncomingEmail email, string expectedReasonCode)
    {
        var result = await _classifier.ClassifyAsync(email);

        Assert.That(result.RequiresReply, Is.False);
        Assert.That(result.ReasonCode, Is.EqualTo(expectedReasonCode));
        Assert.That(result.Trace.Evaluations, Is.Not.Empty);
        Assert.That(result.DecisionSource, Is.EqualTo("BUILT_IN"));
        Assert.That(result.Trace.Evaluations.Count, Is.GreaterThanOrEqualTo(1));
        Assert.That(result.Trace.Evaluations.Any(evaluation => evaluation.Matched && evaluation.ReasonCode == expectedReasonCode), Is.True);
    }

    [TestCaseSource(nameof(KnownReplyCases))]
    public async Task Should_allow_known_conversational_cases(IncomingEmail email)
    {
        var result = await _classifier.ClassifyAsync(email);

        Assert.That(result.RequiresReply, Is.True);
        Assert.That(result.ReasonCode, Is.EqualTo(ClassificationReasonCodes.DefaultReply));
        Assert.That(result.DecisionSource, Is.EqualTo("BUILT_IN"));
        Assert.That(result.Trace.Evaluations.Count, Is.GreaterThanOrEqualTo(2));
        Assert.That(result.Trace.Evaluations[^1].RuleName, Is.EqualTo("BUILT_IN:DEFAULT_REPLY"));
        Assert.That(result.Trace.Evaluations[^1].Matched, Is.True);
        Assert.That(result.Trace.Evaluations[^1].ReasonCode, Is.EqualTo(ClassificationReasonCodes.DefaultReply));
        Assert.That(result.Trace.Evaluations.Take(result.Trace.Evaluations.Count - 1).Any(evaluation => evaluation.Matched is false), Is.True);
    }

    private static IncomingEmail CreateEmail(
        string fromAddress,
        string displayName,
        string subject,
        string bodyText = "This is a representative test body.",
        bool hasListUnsubscribeHeader = false,
        bool hasListIdHeader = false,
        string precedence = "") =>
        new(
            1,
            Guid.NewGuid().ToString("N"),
            EmailAddress.FromParts(fromAddress, displayName),
            subject,
            bodyText,
            DateTimeOffset.UtcNow,
            hasListUnsubscribeHeader,
            hasListIdHeader,
            precedence,
            false);
}
