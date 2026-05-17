using ContextMemory.Core.Models;

namespace ContextMemory.Core.Contracts;

public interface IConversationMemory
{
    Task<IReadOnlyList<OllamaMessage>> GetHistoryAsync(
        string appId,
        string userId,
        int maxMessages,
        CancellationToken cancellationToken = default);

    Task AppendAsync(
        string appId,
        string userId,
        IEnumerable<OllamaMessage> messages,
        int maxMessages,
        CancellationToken cancellationToken = default);

    Task<int> GetMessageCountAsync(
        string appId,
        string userId,
        CancellationToken cancellationToken = default);

    Task ApplySummaryAsync(
        string appId,
        string userId,
        string summary,
        IReadOnlyList<OllamaMessage> remainingMessages,
        CancellationToken cancellationToken = default);
}
