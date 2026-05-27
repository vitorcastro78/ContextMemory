using System.Text.Json;
using ContextMemory.Core.Contracts;
using ContextMemory.Core.KnowledgeLoop;
using Microsoft.EntityFrameworkCore;

namespace ContextMemory.Core.Persistence.Postgres;

public sealed class PostgresKnowledgeLoopStore : IKnowledgeLoopStore
{
    private readonly IDbContextFactory<ContextMemoryDbContext> _dbFactory;

    public PostgresKnowledgeLoopStore(IDbContextFactory<ContextMemoryDbContext> dbFactory) =>
        _dbFactory = dbFactory;

    public async Task SaveEntryAsync(KnowledgeLoopEntry entry, CancellationToken cancellationToken = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        var existing = await db.KnowledgeLoopEntries
            .FirstOrDefaultAsync(e => e.SessionId == entry.SessionId, cancellationToken)
            .ConfigureAwait(false);

        if (existing is null)
        {
            db.KnowledgeLoopEntries.Add(ToEntity(entry));
        }
        else
        {
            existing.Status = entry.Status.ToString();
            existing.EvaluationJson = JsonSerializer.Serialize(entry.Evaluation, PostgresJson.CamelCase);
            existing.MessagesJson = JsonSerializer.Serialize(entry.Messages, PostgresJson.CamelCase);
            existing.IngestedPath = entry.IngestedPath;
            existing.FailureReason = entry.FailureReason;
            existing.ProcessedAt = entry.ProcessedAt;
        }

        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<KnowledgeLoopEntry>> GetPendingAsync(
        string appId,
        KnowledgeLoopStatus status,
        CancellationToken cancellationToken = default) =>
        await GetByAppAsync(appId, status, cancellationToken).ConfigureAwait(false);

    public async Task<IReadOnlyList<KnowledgeLoopEntry>> GetByAppAsync(
        string appId,
        KnowledgeLoopStatus? status,
        CancellationToken cancellationToken = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        var query = db.KnowledgeLoopEntries.AsNoTracking().Where(e => e.AppId == appId);
        if (status is not null)
            query = query.Where(e => e.Status == status.Value.ToString());

        var rows = await query.OrderByDescending(e => e.CreatedAt).ToListAsync(cancellationToken).ConfigureAwait(false);
        return rows.Select(ToModel).ToList();
    }

    public async Task<KnowledgeLoopEntry?> GetBySessionIdAsync(
        string sessionId,
        CancellationToken cancellationToken = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        var row = await db.KnowledgeLoopEntries
            .AsNoTracking()
            .FirstOrDefaultAsync(e => e.SessionId == sessionId, cancellationToken)
            .ConfigureAwait(false);
        return row is null ? null : ToModel(row);
    }

    public async Task UpdateStatusAsync(
        string sessionId,
        KnowledgeLoopStatus status,
        string? ingestedPath = null,
        string? failureReason = null,
        CancellationToken cancellationToken = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        var row = await db.KnowledgeLoopEntries
            .FirstOrDefaultAsync(e => e.SessionId == sessionId, cancellationToken)
            .ConfigureAwait(false);
        if (row is null)
            return;

        row.Status = status.ToString();
        row.IngestedPath = ingestedPath ?? row.IngestedPath;
        row.FailureReason = failureReason ?? row.FailureReason;
        row.ProcessedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task<KnowledgeLoopStats> GetStatsAsync(string appId, CancellationToken cancellationToken = default)
    {
        var entries = await GetByAppAsync(appId, null, cancellationToken).ConfigureAwait(false);
        return new KnowledgeLoopStats
        {
            SessionsEvaluated = entries.Count,
            SessionsApproved = entries.Count(e =>
                e.Status is KnowledgeLoopStatus.PendingExtraction
                    or KnowledgeLoopStatus.Ingested
                    or KnowledgeLoopStatus.PendingReview),
            ChunksCreated = entries.Count(e => e.Status == KnowledgeLoopStatus.Ingested),
            ChunksMerged = 0,
            ChunksRejected = entries.Count(e => e.Status == KnowledgeLoopStatus.Rejected),
            LastRunAt = entries.MaxBy(e => e.ProcessedAt ?? e.CreatedAt)?.ProcessedAt
                        ?? entries.MaxBy(e => e.CreatedAt)?.CreatedAt
        };
    }

    public async Task<int> CountIngestedTodayAsync(string appId, CancellationToken cancellationToken = default)
    {
        var today = DateTimeOffset.UtcNow.Date;
        await using var db = await _dbFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        return await db.KnowledgeLoopEntries
            .CountAsync(
                e => e.AppId == appId
                     && e.Status == nameof(KnowledgeLoopStatus.Ingested)
                     && (e.ProcessedAt ?? e.CreatedAt) >= today,
                cancellationToken)
            .ConfigureAwait(false);
    }

    private static KnowledgeLoopEntryEntity ToEntity(KnowledgeLoopEntry entry) => new()
    {
        SessionId = entry.SessionId,
        AppId = entry.AppId,
        UserId = entry.UserId,
        Status = entry.Status.ToString(),
        MessagesJson = JsonSerializer.Serialize(entry.Messages, PostgresJson.CamelCase),
        EvaluationJson = JsonSerializer.Serialize(entry.Evaluation, PostgresJson.CamelCase),
        IngestedPath = entry.IngestedPath,
        FailureReason = entry.FailureReason,
        CreatedAt = entry.CreatedAt,
        ProcessedAt = entry.ProcessedAt
    };

    private static KnowledgeLoopEntry ToModel(KnowledgeLoopEntryEntity row)
    {
        var messages = JsonSerializer.Deserialize<List<ContextMemory.Core.Models.OllamaMessage>>(row.MessagesJson, PostgresJson.CamelCase) ?? [];
        var evaluation = string.IsNullOrWhiteSpace(row.EvaluationJson)
            ? null
            : JsonSerializer.Deserialize<ConversationEvaluationResult>(row.EvaluationJson, PostgresJson.CamelCase);

        return new KnowledgeLoopEntry
        {
            SessionId = row.SessionId,
            AppId = row.AppId,
            UserId = row.UserId,
            Messages = messages,
            Evaluation = evaluation,
            Status = Enum.Parse<KnowledgeLoopStatus>(row.Status),
            IngestedPath = row.IngestedPath,
            FailureReason = row.FailureReason,
            CreatedAt = row.CreatedAt,
            ProcessedAt = row.ProcessedAt
        };
    }
}

public sealed class KnowledgeLoopEntryEntity
{
    public string SessionId { get; set; } = string.Empty;
    public string AppId { get; set; } = string.Empty;
    public string UserId { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public string MessagesJson { get; set; } = "[]";
    public string? EvaluationJson { get; set; }
    public string? IngestedPath { get; set; }
    public string? FailureReason { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? ProcessedAt { get; set; }
}
