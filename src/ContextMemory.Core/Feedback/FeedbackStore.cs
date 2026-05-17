using System.Collections.Concurrent;
using System.Text.Json;
using ContextMemory.Core.Configuration;
using ContextMemory.Core.Contracts;
using ContextMemory.Core.Models;
using Microsoft.Extensions.Options;

namespace ContextMemory.Core.Feedback;

public sealed class FeedbackStore : IFeedbackStore
{
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase, WriteIndented = true };
    private readonly string _feedbackRoot;
    private readonly ConcurrentDictionary<string, SemaphoreSlim> _locks = new();

    public FeedbackStore(IOptions<ContextMemoryOptions> options)
    {
        _feedbackRoot = Path.Combine(
            Path.GetFullPath(options.Value.DataPath, options.Value.ContentRootPath),
            "feedback");
        Directory.CreateDirectory(_feedbackRoot);
    }

    public async Task RecordAsync(FeedbackEntry entry, CancellationToken cancellationToken = default)
    {
        var gate = GetLock(entry.AppId);
        await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var list = await LoadAsync(entry.AppId, cancellationToken).ConfigureAwait(false);
            list.Add(entry);
            await SaveAsync(entry.AppId, list, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            gate.Release();
        }
    }

    public async Task<IReadOnlyList<FeedbackEntry>> GetByAppAsync(string appId, CancellationToken cancellationToken = default)
    {
        var gate = GetLock(appId);
        await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            return await LoadAsync(appId, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            gate.Release();
        }
    }

    public async Task<double> GetAverageScoreAsync(string appId, CancellationToken cancellationToken = default)
    {
        var entries = await GetByAppAsync(appId, cancellationToken).ConfigureAwait(false);
        if (entries.Count == 0)
            return 0;

        return entries.Average(e => e.Score);
    }

    public async Task<int> CountNegativeByReasonAsync(
        string appId,
        string reasonContains,
        CancellationToken cancellationToken = default)
    {
        var entries = await GetByAppAsync(appId, cancellationToken).ConfigureAwait(false);
        return entries.Count(e =>
            e.Score < 0
            && e.Reason is not null
            && e.Reason.Contains(reasonContains, StringComparison.OrdinalIgnoreCase));
    }

    private async Task<List<FeedbackEntry>> LoadAsync(string appId, CancellationToken cancellationToken)
    {
        var path = GetPath(appId);
        if (!File.Exists(path))
            return [];

        var json = await File.ReadAllTextAsync(path, cancellationToken).ConfigureAwait(false);
        return JsonSerializer.Deserialize<List<FeedbackEntry>>(json, JsonOptions) ?? [];
    }

    private async Task SaveAsync(string appId, List<FeedbackEntry> entries, CancellationToken cancellationToken)
    {
        var path = GetPath(appId);
        await File.WriteAllTextAsync(path, JsonSerializer.Serialize(entries, JsonOptions), cancellationToken)
            .ConfigureAwait(false);
    }

    private string GetPath(string appId) => Path.Combine(_feedbackRoot, $"{appId}.json");
    private SemaphoreSlim GetLock(string appId) => _locks.GetOrAdd(appId, _ => new SemaphoreSlim(1, 1));
}
