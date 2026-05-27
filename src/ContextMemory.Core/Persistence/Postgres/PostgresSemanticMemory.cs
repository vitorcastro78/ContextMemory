using System.Collections.Concurrent;
using ContextMemory.Core.Contracts;
using ContextMemory.Core.Knowledge;
using ContextMemory.Core.Models;
using Microsoft.EntityFrameworkCore;

namespace ContextMemory.Core.Persistence.Postgres;

public sealed class PostgresSemanticMemory : ISemanticMemory
{
    private readonly IDbContextFactory<ContextMemoryDbContext> _dbFactory;
    private readonly IEmbeddingEngine _embeddingEngine;
    private readonly SimilaritySearch _similaritySearch;
    private readonly ConcurrentDictionary<string, SemaphoreSlim> _locks = new();

    public PostgresSemanticMemory(
        IDbContextFactory<ContextMemoryDbContext> dbFactory,
        IEmbeddingEngine embeddingEngine,
        SimilaritySearch similaritySearch)
    {
        _dbFactory = dbFactory;
        _embeddingEngine = embeddingEngine;
        _similaritySearch = similaritySearch;
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
            var normalized = factText.Trim();
            await using var db = await _dbFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);

            var existingTexts = await db.SemanticFacts
                .AsNoTracking()
                .Where(f => f.AppId == appId && f.UserId == userId)
                .Select(f => f.Text)
                .ToListAsync(cancellationToken)
                .ConfigureAwait(false);

            if (existingTexts.Any(t => string.Equals(t, normalized, StringComparison.OrdinalIgnoreCase)))
                return;

            var vector = await _embeddingEngine.EmbedAsync(normalized, cancellationToken).ConfigureAwait(false);
            db.SemanticFacts.Add(new SemanticFactEntity
            {
                AppId = appId,
                UserId = userId,
                Text = normalized,
                Vector = vector,
                LearnedAt = DateTimeOffset.UtcNow
            });
            await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
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
            await using var db = await _dbFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
            var facts = await db.SemanticFacts
                .AsNoTracking()
                .Where(f => f.AppId == appId && f.UserId == userId)
                .ToListAsync(cancellationToken)
                .ConfigureAwait(false);

            if (facts.Count == 0)
                return [];

            var queryVector = await _embeddingEngine.EmbedAsync(query, cancellationToken).ConfigureAwait(false);
            var entries = facts
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

    private SemaphoreSlim GetLock(string appId, string userId) =>
        _locks.GetOrAdd($"{appId}:{userId}", _ => new SemaphoreSlim(1, 1));
}
