using System.Collections.Concurrent;
using ContextMemory.Core.Contracts;
using ContextMemory.Core.Models;

namespace ContextMemory.Core.CompanyBrain;

public sealed class ProcessEmbeddingIndex
{
    private readonly IEmbeddingEngine _embeddingEngine;
    private readonly ConcurrentDictionary<string, IReadOnlyList<ProcessEmbeddingEntry>> _cache = new(StringComparer.Ordinal);

    public ProcessEmbeddingIndex(IEmbeddingEngine embeddingEngine) =>
        _embeddingEngine = embeddingEngine;

    public void Invalidate(string companyId) =>
        _cache.TryRemove(companyId, out _);

    public void Rebuild(string companyId, IReadOnlyList<CompanyProcess> processes)
    {
        if (!_embeddingEngine.IsAvailable || processes.Count == 0)
        {
            _cache[companyId] = [];
            return;
        }

        var texts = processes.Select(ProcessMatcher.BuildEmbeddingText).ToList();
        var vectors = _embeddingEngine.EmbedBatchAsync(texts).GetAwaiter().GetResult();
        var entries = new List<ProcessEmbeddingEntry>(processes.Count);

        for (var i = 0; i < processes.Count; i++)
            entries.Add(new ProcessEmbeddingEntry(processes[i].ProcessId, vectors[i]));

        _cache[companyId] = entries;
    }

    public IReadOnlyList<ProcessEmbeddingEntry>? TryGet(string companyId) =>
        _cache.TryGetValue(companyId, out var entries) ? entries : null;

    public float[]? EmbedQuery(string query)
    {
        if (!_embeddingEngine.IsAvailable || string.IsNullOrWhiteSpace(query))
            return null;

        return _embeddingEngine.EmbedAsync(query.Trim()).GetAwaiter().GetResult();
    }
}
