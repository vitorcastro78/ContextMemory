using System.Collections.Concurrent;
using ContextMemory.Core.Contracts;

namespace ContextMemory.Core.Feedback;

public sealed class MessageIdTracker : IMessageIdTracker
{
    private readonly ConcurrentDictionary<string, string> _lastMessageIds = new();

    public string CreateAndTrack(string appId, string userId)
    {
        var id = Guid.NewGuid().ToString("N");
        _lastMessageIds[$"{appId}:{userId}"] = id;
        return id;
    }

    public bool TryGetLast(string appId, string userId, out string? messageId) =>
        _lastMessageIds.TryGetValue($"{appId}:{userId}", out messageId);
}
