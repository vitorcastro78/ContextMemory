using ContextMemory.Core.Models;

namespace ContextMemory.Core.Contracts;

public interface IAuditLog
{
    Task AppendAsync(AuditLogEntry entry, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<AuditLogEntry>> GetByAppAsync(string appId, int limit = 100, CancellationToken cancellationToken = default);
}
