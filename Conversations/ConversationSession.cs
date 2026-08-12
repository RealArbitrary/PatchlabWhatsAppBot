namespace PatchlabWhatsAppBot.Conversations;

public class ConversationSession
{
    public ConversationState State { get; set; } = ConversationState.New;
    public string? IssueText { get; set; }
    public string? TicketNumber { get; set; }
}