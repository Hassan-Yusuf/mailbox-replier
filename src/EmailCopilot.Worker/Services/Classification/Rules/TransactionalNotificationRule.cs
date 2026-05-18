namespace EmailCopilot.Worker;

public sealed class TransactionalNotificationRule : BuiltInClassificationRule
{
    public override string RuleName => "BUILT_IN:TRANSACTIONAL_NOTIFICATION";
    public override string ReasonCode => ClassificationReasonCodes.TransactionalNotification;
    public override string Description =>
        "Order, receipt, delivery, payment, or other transactional notification.";
    public override RuleCategory Category => RuleCategory.Transactional;

    public override RuleEvaluation Evaluate(IncomingEmail email)
    {
        var fromAddress = email.From.Address;
        var subject = email.Subject.Trim().ToLowerInvariant();
        var body = email.BodyText.Trim().ToLowerInvariant();

        if (ContainsAny(
                fromAddress,
                "docusign.net",
                "receipt",
                "receipts",
                "shipment-tracking",
                "tracking@",
                "delivery@",
                "dispatch@",
                "orders@",
                "billing@",
                "payments@"))
        {
            return Matched(RuleName, ReasonCode, $"address={fromAddress}");
        }

        if (ContainsAny(
                subject,
                "approved",
                "declined",
                "one-time passcode",
                "one time passcode",
                "verification code",
                "security code",
                "login code",
                "ordered:",
                "order confirmation",
                "statement is now available",
                "account activity statement",
                "monthly account statement",
                "online payment confirmation",
                "visitor confirmation",
                "booking confirmation",
                "payment confirmation",
                "your booking",
                "your order",
                "your payment",
                "your invoice",
                "policy update",
                "privacy notice",
                "user agreement",
                "terms and conditions",
                "your rent payment is due",
                "new message from us",
                "ride receipt",
                "receipt",
                "invoice",
                "parcel",
                "arriving today",
                "out for delivery",
                "delivered today",
                "shipment",
                "tracking",
                "package",
                "order update",
                "recording titled",
                "ready to watch",
                "assigned to your team",
                "please check in with the carrier",
                "online check-in is now open",
                "signed:",
                "otp",
                "passcode",
                "security alert"))
        {
            return Matched(RuleName, ReasonCode, $"subject={subject}");
        }

        if (subject.Contains("order", StringComparison.Ordinal) &&
            subject.Contains("confirm", StringComparison.Ordinal))
        {
            return Matched(RuleName, ReasonCode, $"subject={subject}");
        }

        if (ContainsAny(
                body,
                "use this code to sign in",
                "one-time passcode",
                "verification code",
                "generate statements",
                "account statement is available as an attachment",
                "payment confirmation",
                "your payment reference is",
                "we've sent a new message to your online banking digital inbox",
                "view or edit order",
                "here's your ride receipt",
                "track your package",
                "delivery update",
                "order total",
                "tax invoice",
                "receipt attached",
                "trip receipt",
                "qr code ready for the visit",
                "track your parcel",
                "your parcel",
                "parcel is on its way",
                "out for delivery",
                "watch your video",
                "ready to watch",
                "assigned to your team",
                "online check-in is now open",
                "check in with the carrier",
                "check in online",
                "thank you for signing your documents online today",
                "if you did not sign the document using docusign"))
        {
            return Matched(RuleName, ReasonCode, "transactional-body");
        }

        return NotMatched(RuleName, ReasonCode, $"subject={subject}");
    }
}
