using PatchlabWhatsAppBot.Data;

namespace PatchlabWhatsAppBot.Conversations;

public class ConversationSession
{
    public string CellphoneNumber { get; set; } = "";
    public ConversationState State { get; set; } = ConversationState.New;

    public string? FirstName { get; set; }
    public string? LastName { get; set; }
    public string? Area { get; set; }
    public string? IssueText { get; set; }
    public TicketType? TicketType { get; set; }

    // Relative file paths (see ITicketPhotoStorage) for photos already
    // downloaded and saved while AwaitingPhotos, before the ticket they'll
    // belong to has been created. Turned into TicketPhotos rows once the
    // ticket is actually persisted (see PendingTicketFinalizer).
    public List<string> PendingPhotoPaths { get; set; } = new();

    public bool DetailsFromSavedProfile { get; set; }

    public string? SelectedTicketNumber { get; set; }
    public string? SelectedTicketId { get; set; }
}