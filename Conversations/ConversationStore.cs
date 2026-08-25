using System.Collections.Concurrent;

namespace PatchlabWhatsAppBot.Conversations;

public class ConversationStore
{
    private readonly ConcurrentDictionary<string, ConversationSession> _sessions = new();

    public ConversationSession GetOrCreate(string phoneNumber)
    {
        return _sessions.GetOrAdd(phoneNumber, _ => new ConversationSession());
    }

    /// <summary>
    /// Looks up a session without creating one. Used by background callbacks
    /// (e.g. PhotoWaitCoordinator's timers) that must not resurrect a session
    /// that was already reset by the time they fire.
    /// </summary>
    public ConversationSession? Peek(string phoneNumber)
    {
        return _sessions.TryGetValue(phoneNumber, out var session) ? session : null;
    }

    public void Reset(string phoneNumber)
    {
        _sessions.TryRemove(phoneNumber, out _);
    }
}