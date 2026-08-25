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
    AwaitingPhotoChoice,
    AwaitingPhotos,
    AwaitingTicketSelection,
    AwaitingTicketFeedback,
    AwaitingUnhappyReason
}