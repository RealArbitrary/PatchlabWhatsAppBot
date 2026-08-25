namespace PatchlabWhatsAppBot.Conversations;

public enum ConversationState
{
    New,
    AwaitingStartChoice,
    AwaitingTicketType,
    AwaitingReturningCustomerChoice,
    AwaitingName,
    AwaitingArea,
    AwaitingIssue,
    AwaitingTicketSelection,
    AwaitingTicketFeedback,
    AwaitingUnhappyReason
}