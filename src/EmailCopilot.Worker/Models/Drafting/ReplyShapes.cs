namespace EmailCopilot.Worker;

public static class ReplyShapes
{
    public const string GeneralReply = "GENERAL_REPLY";
    public const string DirectAnswer = "DIRECT_ANSWER";
    public const string Acknowledge = "ACKNOWLEDGE";
    public const string AcknowledgeAndAsk = "ACKNOWLEDGE_AND_ASK";
    public const string ConfirmAndClose = "CONFIRM_AND_CLOSE";
    public const string ConfirmAndRequest = "CONFIRM_AND_REQUEST";
    public const string Decline = "DECLINE";
}

public static class DraftIntents
{
    public const string GeneralReply = ReplyShapes.GeneralReply;
    public const string DirectAnswer = ReplyShapes.DirectAnswer;
    public const string Acknowledge = ReplyShapes.Acknowledge;
    public const string AcknowledgeAndAsk = ReplyShapes.AcknowledgeAndAsk;
    public const string ConfirmAndClose = ReplyShapes.ConfirmAndClose;
    public const string ConfirmAndRequest = ReplyShapes.ConfirmAndRequest;
    public const string Decline = ReplyShapes.Decline;
}
