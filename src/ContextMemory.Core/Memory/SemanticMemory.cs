using System.Collections.Concurrent;
using ContextMemory.Core.Configuration;
using ContextMemory.Core.Contracts;
using ContextMemory.Core.Knowledge;
using ContextMemory.Core.Models;
using MemoryPack;
using Microsoft.Extensions.Options;

namespace ContextMemory.Core.Memory;

public sealed class SemanticMemory : ISemanticMemory
{
    private readonly string _semanticRoot;
    private readonly IEmbeddingEngine _embeddingEngine;
    private readonly SimilaritySearch _similaritySearch;
    private readonly ConcurrentDictionary<string, SemaphoreSlim> _locks = new();

    public SemanticMemory(
        IOptions<ContextMemoryOptions> options,
        IEmbeddingEngine embeddingEngine,
        SimilaritySearch similaritySearch)
    {
        _embeddingEngine = embeddingEngine;
        _similaritySearch = similaritySearch;
        _semanticRoot = Path.Combine(
            Path.GetFullPath(options.Value.DataPath, options.Value.ContentRootPath),
            "user-profiles");
        Directory.CreateDirectory(_semanticRoot);
    }

    public async Task StoreFactAsync(
        string appId,
        string userId,
        string factText,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(factText) || !_embeddingEngine.IsAvailable)
            return;

        var gate = GetLock(appId, userId);
        await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var payload = await LoadAsync(appId, userId, cancellationToken).ConfigureAwait(false);
            var normalized = factText.Trim();

            if (payload.Facts.Any(f => string.Equals(f.Text, normalized, StringComparison.OrdinalIgnoreCase)))
                return;

            var vector = await _embeddingEngine.EmbedAsync(normalized, cancellationToken).ConfigureAwait(false);
            payload.Facts.Add(new SemanticFactEntry
            {
                Text = normalized,
                Vector = vector,
                LearnedAt = DateTimeOffset.UtcNow
            });

            await SaveAsync(appId, userId, payload, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            gate.Release();
        }
    }

    public async Task<IReadOnlyList<string>> SearchAsync(
        string appId,
        string userId,
        string query,
        int topK = 3,
        float threshold = 0.55f,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(query) || !_embeddingEngine.IsAvailable)
            return [];

        var gate = GetLock(appId, userId);
        await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var payload = await LoadAsync(appId, userId, cancellationToken).ConfigureAwait(false);
            if (payload.Facts.Count == 0)
                return [];

            var queryVector = await _embeddingEngine.EmbedAsync(query, cancellationToken).ConfigureAwait(false);
            var entries = payload.Facts
                .Select(f => new VectorEntry
                {
                    AppId = appId,
                    Text = f.Text,
                    Source = "semantic",
                    Vector = f.Vector
                })
                .ToList();

            return _similaritySearch
                .Search(entries, queryVector, topK, threshold)
                .Select(c => c.Content)
                .ToList();
        }
        finally
        {
            gate.Release();
        }
    }

    private async Task<SemanticMemoryPayload> LoadAsync(
        string appId,
        string userId,
        CancellationToken cancellationToken)
    {
        var path = GetFilePath(appId, userId);
        if (!File.Exists(path))
            return new SemanticMemoryPayload();

        await using var stream = File.OpenRead(path);
        return await MemoryPackSerializer
            .DeserializeAsync<SemanticMemoryPayload>(stream, cancellationToken: cancellationToken)
            .ConfigureAwait(false)
            ?? new SemanticMemoryPayload();
    }

    private async Task SaveAsync(
        string appId,
        string userId,
        SemanticMemoryPayload payload,
        CancellationToken cancellationToken)
    {
        var appDir = Path.Combine(_semanticRoot, appId);
        Directory.CreateDirectory(appDir);
        var path = GetFilePath(appId, userId);

        await using var stream = File.Create(path);
        await MemoryPackSerializer
            .SerializeAsync(stream, payload, cancellationToken: cancellationToken)
            .ConfigureAwait(false);
    }

    private string GetFilePath(string appId, string userId) =>
        Path.Combine(_semanticRoot, appId, $"{userId}.semantic.bin");

    private SemaphoreSlim GetLock(string appId, string userId) =>
        _locks.GetOrAdd($"{appId}:{userId}", _ => new SemaphoreSlim(1, 1));
}
