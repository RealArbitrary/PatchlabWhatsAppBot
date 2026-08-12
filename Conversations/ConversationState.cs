namespace PatchlabWhatsAppBot.Conversations;

public enum ConversationState
{
    New,                // no state yet — first message
    AwaitingIssue,      // LSF path: we've asked what's wrong, waiting for their reply
    AwaitingClientType, // future: "existing client" vs "new client" branch
    Completed           // flow finished, ready to reset on next message
}