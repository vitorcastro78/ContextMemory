using ContextMemory.Core.KnowledgeLoop;
using ContextMemory.Core.Models;

namespace ContextMemory.Core.Contracts;

public interface IKnowledgeLoop
{
    Task EvaluateSessionAsync(
        string appId,
        string userId,
        IReadOnlyList<OllamaMessage> sessionMessages,
        CancellationToken cancellationToken = default);

    Task ProcessPendingAsync(string appId, CancellationToken cancellationToken = default);

    Task<KnowledgeLoopStats> GetStatsAsync(string appId, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<KnowledgeLoopEntry>> GetEntriesAsync(
        string appId,
        KnowledgeLoopStatus? status = null,
        CancellationToken cancellationToken = default);

    Task<bool> ApproveAsync(string sessionId, CancellationToken cancellationToken = default);

    Task<bool> RejectAsync(string sessionId, CancellationToken cancellationToken = default);
}
