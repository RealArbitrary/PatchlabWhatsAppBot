using System.Collections.Concurrent;

namespace PatchlabTwilioBot.Conversations;

public class ConversationStore
{
    private readonly ConcurrentDictionary<string, ConversationSession> _sessions = new();

    public ConversationSession GetOrCreate(string phoneNumber)
    {
        return _sessions.GetOrAdd(phoneNumber, _ => new ConversationSession());
    }

    public void Reset(string phoneNumber)
    {
        _sessions.TryRemove(phoneNumber, out _);
    }
}