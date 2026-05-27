using ContextMemory.Core.KnowledgeLoop;

namespace ContextMemory.Core.Contracts;

public interface IKnowledgeLoopStore
{
    Task SaveEntryAsync(KnowledgeLoopEntry entry, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<KnowledgeLoopEntry>> GetPendingAsync(
        string appId,
        KnowledgeLoopStatus status,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<KnowledgeLoopEntry>> GetByAppAsync(
        string appId,
        KnowledgeLoopStatus? status,
        CancellationToken cancellationToken = default);

    Task<KnowledgeLoopEntry?> GetBySessionIdAsync(
        string sessionId,
        CancellationToken cancellationToken = default);

    Task UpdateStatusAsync(
        string sessionId,
        KnowledgeLoopStatus status,
        string? ingestedPath = null,
        string? failureReason = null,
        CancellationToken cancellationToken = default);

    Task<KnowledgeLoopStats> GetStatsAsync(string appId, CancellationToken cancellationToken = default);

    Task<int> CountIngestedTodayAsync(string appId, CancellationToken cancellationToken = default);
}
