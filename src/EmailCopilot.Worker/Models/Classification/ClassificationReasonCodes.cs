namespace EmailCopilot.Worker;

public static class ClassificationReasonCodes
{
    public const string DefaultReply = "DEFAULT_REPLY";
    public const string NoReplySender = "NO_REPLY_SENDER";
    public const string ListOrBroadcastMail = "LIST_OR_BROADCAST_MAIL";
    public const string BroadcastFormattedMail = "BROADCAST_FORMATTED_MAIL";
    public const string TransactionalNotification = "TRANSACTIONAL_NOTIFICATION";
    public const string SelfServiceAction = "SELF_SERVICE_ACTION";
    public const string WorkflowAcknowledgement = "WORKFLOW_ACKNOWLEDGEMENT";
    public const string AutomatedSender = "AUTOMATED_SENDER";
    public const string BulkOrPromotional = "BULK_OR_PROMOTIONAL";
    public const string SocialOrDigest = "SOCIAL_OR_DIGEST";
    public const string SuspiciousOrSpam = "SUSPICIOUS_OR_SPAM";
    public const string FeedbackSurveyRequest = "FEEDBACK_SURVEY_REQUEST";
    public const string UndecodedBody = "UNDECODED_BODY";
    public const string DraftIneligible = "DRAFT_INELIGIBLE";
    public const string UserExclusionRule = "USER_EXCLUSION_RULE";
}
