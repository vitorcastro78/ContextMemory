using System.Collections.Concurrent;
using System.Text.Json;
using ContextMemory.Core.Configuration;
using ContextMemory.Core.Contracts;
using Microsoft.Extensions.Options;

namespace ContextMemory.Core.KnowledgeLoop;

public sealed class FileKnowledgeLoopStore : IKnowledgeLoopStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true
    };

    private readonly string _root;
    private readonly ConcurrentDictionary<string, SemaphoreSlim> _locks = new();

    public FileKnowledgeLoopStore(IOptions<ContextMemoryOptions> options)
    {
        _root = Path.Combine(
            Path.GetFullPath(options.Value.DataPath, options.Value.ContentRootPath),
            "knowledge-loop");
        Directory.CreateDirectory(_root);
    }

    public async Task SaveEntryAsync(KnowledgeLoopEntry entry, CancellationToken cancellationToken = default)
    {
        var gate = GetLock(entry.AppId);
        await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var list = await LoadAsync(entry.AppId, cancellationToken).ConfigureAwait(false);
            list.RemoveAll(e => e.SessionId == entry.SessionId);
            list.Add(entry);
            await SaveAsync(entry.AppId, list, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            gate.Release();
        }
    }

    public async Task<IReadOnlyList<KnowledgeLoopEntry>> GetPendingAsync(
        string appId,
        KnowledgeLoopStatus status,
        CancellationToken cancellationToken = default)
    {
        var all = await GetByAppAsync(appId, status, cancellationToken).ConfigureAwait(false);
        return all.Where(e => e.Status == status).ToList();
    }

    public async Task<IReadOnlyList<KnowledgeLoopEntry>> GetByAppAsync(
        string appId,
        KnowledgeLoopStatus? status,
        CancellationToken cancellationToken = default)
    {
        var gate = GetLock(appId);
        await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var list = await LoadAsync(appId, cancellationToken).ConfigureAwait(false);
            return status is null
                ? list
                : list.Where(e => e.Status == status.Value).ToList();
        }
        finally
        {
            gate.Release();
        }
    }

    public async Task<KnowledgeLoopEntry?> GetBySessionIdAsync(
        string sessionId,
        CancellationToken cancellationToken = default)
    {
        foreach (var dir in Directory.EnumerateDirectories(_root))
        {
            var appId = Path.GetFileName(dir);
            var gate = GetLock(appId);
            await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                var list = await LoadAsync(appId, cancellationToken).ConfigureAwait(false);
                var found = list.FirstOrDefault(e => e.SessionId == sessionId);
                if (found is not null)
                    return found;
            }
            finally
            {
                gate.Release();
            }
        }

        return null;
    }

    public async Task UpdateStatusAsync(
        string sessionId,
        KnowledgeLoopStatus status,
        string? ingestedPath = null,
        string? failureReason = null,
        CancellationToken cancellationToken = default)
    {
        var entry = await GetBySessionIdAsync(sessionId, cancellationToken).ConfigureAwait(false);
        if (entry is null)
            return;

        entry.Status = status;
        entry.IngestedPath = ingestedPath ?? entry.IngestedPath;
        entry.FailureReason = failureReason ?? entry.FailureReason;
        entry.ProcessedAt = DateTimeOffset.UtcNow;
        await SaveEntryAsync(entry, cancellationToken).ConfigureAwait(false);
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
            ChunksCreated = entries.Count(e =>
                e.Status == KnowledgeLoopStatus.Ingested
                && e.IngestedPath is not null
                && !e.IngestedPath.Contains("merged", StringComparison.OrdinalIgnoreCase)),
            ChunksMerged = entries.Count(e => e.Status == KnowledgeLoopStatus.Ingested && e.Evaluation?.Score > 0.85f),
            ChunksRejected = entries.Count(e => e.Status == KnowledgeLoopStatus.Rejected),
            LastRunAt = entries.MaxBy(e => e.ProcessedAt ?? e.CreatedAt)?.ProcessedAt
                        ?? entries.MaxBy(e => e.CreatedAt)?.CreatedAt
        };
    }

    public async Task<int> CountIngestedTodayAsync(string appId, CancellationToken cancellationToken = default)
    {
        var today = DateTimeOffset.UtcNow.Date;
        var entries = await GetByAppAsync(appId, KnowledgeLoopStatus.Ingested, cancellationToken).ConfigureAwait(false);
        return entries.Count(e => (e.ProcessedAt ?? e.CreatedAt).UtcDateTime.Date == today);
    }

    private async Task<List<KnowledgeLoopEntry>> LoadAsync(string appId, CancellationToken cancellationToken)
    {
        var path = GetPath(appId);
        if (!File.Exists(path))
            return [];

        var json = await File.ReadAllTextAsync(path, cancellationToken).ConfigureAwait(false);
        return JsonSerializer.Deserialize<List<KnowledgeLoopEntry>>(json, JsonOptions) ?? [];
    }

    private async Task SaveAsync(string appId, List<KnowledgeLoopEntry> entries, CancellationToken cancellationToken)
    {
        var dir = Path.Combine(_root, appId);
        Directory.CreateDirectory(dir);
        await File.WriteAllTextAsync(
            GetPath(appId),
            JsonSerializer.Serialize(entries, JsonOptions),
            cancellationToken).ConfigureAwait(false);
    }

    private string GetPath(string appId) => Path.Combine(_root, appId, "entries.json");

    private SemaphoreSlim GetLock(string appId) => _locks.GetOrAdd(appId, _ => new SemaphoreSlim(1, 1));
}
