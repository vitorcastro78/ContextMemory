using ContextMemory.Core.Models;

namespace ContextMemory.Core.Contracts;

public interface IKnowledgeSourceConnector
{
    KnowledgeSourceType SourceType { get; }
    Task<KnowledgeSyncResult> SyncAsync(KnowledgeSource source, CancellationToken cancellationToken = default);
}

public record KnowledgeSyncResult
{
    public IReadOnlyList<CompanyProcess> Processes { get; init; } = [];
    public IReadOnlyList<string> Messages { get; init; } = [];
}
