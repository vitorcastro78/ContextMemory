using ContextMemory.Core.Contracts;
using ContextMemory.Core.Models;
using Microsoft.EntityFrameworkCore;

namespace ContextMemory.Core.Persistence.Postgres;

public sealed class PostgresAuditLog : IAuditLog
{
    private readonly IDbContextFactory<ContextMemoryDbContext> _dbFactory;

    public PostgresAuditLog(IDbContextFactory<ContextMemoryDbContext> dbFactory) =>
        _dbFactory = dbFactory;

    public async Task AppendAsync(AuditLogEntry entry, CancellationToken cancellationToken = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        db.AuditLogs.Add(new AuditLogEntity
        {
            AppId = entry.AppId,
            UserId = entry.UserId,
            Phase = entry.Phase,
            Reason = entry.Reason,
            Timestamp = entry.Timestamp
        });
        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<AuditLogEntry>> GetByAppAsync(
        string appId,
        int limit = 100,
        CancellationToken cancellationToken = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        var rows = await db.AuditLogs
            .AsNoTracking()
            .Where(a => a.AppId == appId)
            .OrderByDescending(a => a.Timestamp)
            .Take(limit)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        return rows.Select(r => new AuditLogEntry
        {
            AppId = r.AppId,
            UserId = r.UserId,
            Phase = r.Phase,
            Reason = r.Reason,
            Timestamp = r.Timestamp
        }).ToList();
    }
}
