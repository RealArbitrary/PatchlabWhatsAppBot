namespace PatchlabWhatsAppBot.Conversations;

public class ConversationSession
{
    public string CellphoneNumber { get; set; } = "";
    public ConversationState State { get; set; } = ConversationState.New;

    public string? FirstName { get; set; }
    public string? LastName { get; set; }
    public string? Area { get; set; }
    public string? IssueText { get; set; }

    public bool DetailsFromSavedProfile { get; set; }

    public string? SelectedTicketNumber { get; set; }
    public string? SelectedTicketId { get; set; }
}