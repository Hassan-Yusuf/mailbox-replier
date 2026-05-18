namespace EmailCopilot.Worker.Tests;

[TestFixture]
public sealed class StyleProfilePromotionalFilterTests
{
    [Test]
    public void Flags_travel_deal_phrase()
    {
        Assert.That(
            StyleProfileService.LooksLikePromotional("Cheap flights from London to Thessaloniki at Skyscanner"),
            Is.True);
    }

    [Test]
    public void Flags_deals_from_phrase()
    {
        Assert.That(
            StyleProfileService.LooksLikePromotional("Last-minute deals from Manchester to Rome - book now."),
            Is.True);
    }

    [Test]
    public void Flags_product_listing_with_capitalised_label()
    {
        Assert.That(
            StyleProfileService.LooksLikePromotional("Fan: IcyAir 4 in 1 Portable Air Conditioner"),
            Is.True);
    }

    [Test]
    public void Flags_outlook_share_url()
    {
        Assert.That(
            StyleProfileService.LooksLikePromotional("https://outlook.office.com/share/abc123"),
            Is.True);
    }

    [Test]
    public void Does_not_flag_natural_user_reply_with_punctuation()
    {
        Assert.That(
            StyleProfileService.LooksLikePromotional("Hi, i am happy to go through. Much thanks \U0001F64F"),
            Is.False);
    }

    [Test]
    public void Does_not_flag_natural_user_question_reply()
    {
        Assert.That(
            StyleProfileService.LooksLikePromotional("The one today? Will this be rescheduled then or?"),
            Is.False);
    }

    [Test]
    public void Does_not_flag_short_acknowledgement()
    {
        Assert.That(
            StyleProfileService.LooksLikePromotional("Got it, thanks."),
            Is.False);
    }

    [Test]
    public void Does_not_flag_reply_mentioning_a_flight_casually()
    {
        Assert.That(
            StyleProfileService.LooksLikePromotional("I'll be on the flight tomorrow afternoon, should land by 4."),
            Is.False);
    }

    [Test]
    public void Does_not_flag_empty_or_whitespace()
    {
        Assert.That(StyleProfileService.LooksLikePromotional(""), Is.False);
        Assert.That(StyleProfileService.LooksLikePromotional("   "), Is.False);
    }
}
