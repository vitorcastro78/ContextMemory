namespace ContextMemory.Core.Contracts;

public interface IMessageIdTracker
{
    string CreateAndTrack(string appId, string userId);
    bool TryGetLast(string appId, string userId, out string? messageId);
}
