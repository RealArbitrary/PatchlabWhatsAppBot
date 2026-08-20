namespace PatchlabWhatsAppBot.Conversations;

public enum ConversationState
{
    New,
    AwaitingStartChoice,
    AwaitingReturningCustomerChoice,
    AwaitingName,
    AwaitingArea,
    AwaitingIssue,
    AwaitingTicketSelection,
    AwaitingTicketFeedback,
    AwaitingUnhappyReason
}