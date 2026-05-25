using ContextMemory.Core.Knowledge;
using ContextMemory.Core.Models;

namespace ContextMemory.Core.CompanyBrain;

public static class ProcessMatcher
{
    private const float SemanticWeight = 2.5f;
    private const float SemanticThreshold = 0.35f;

    public static IReadOnlyList<CompanyProcess> Rank(
        IReadOnlyList<CompanyProcess> processes,
        string query,
        IReadOnlyList<ProcessEmbeddingEntry>? embeddings,
        float[]? queryVector,
        int topK)
    {
        if (processes.Count == 0)
            return [];

        if (string.IsNullOrWhiteSpace(query))
            return processes.Take(topK).ToList();

        var normalizedQuery = query.Trim();
        var embeddingLookup = embeddings?.ToDictionary(e => e.ProcessId, StringComparer.Ordinal)
            ?? new Dictionary<string, ProcessEmbeddingEntry>(StringComparer.Ordinal);

        var scored = processes
            .Select(process =>
            {
                var score = (float)ScoreKeyword(process, normalizedQuery);
                if (queryVector is not null
                    && embeddingLookup.TryGetValue(process.ProcessId, out var entry)
                    && entry.Vector.Length == queryVector.Length)
                {
                    var similarity = SimilaritySearch.CosineSimilarity(queryVector, entry.Vector);
                    if (similarity >= SemanticThreshold)
                        score += similarity * SemanticWeight;
                }

                return new { Process = process, Score = score };
            })
            .OrderByDescending(x => x.Score)
            .ToList();

        var top = scored.Where(x => x.Score > 0).Take(topK).Select(x => x.Process).ToList();
        return top.Count > 0 ? top : processes.Take(topK).ToList();
    }

    public static string BuildEmbeddingText(CompanyProcess process)
    {
        var parts = new List<string> { process.Title, process.Category.ToString() };
        if (!string.IsNullOrWhiteSpace(process.Summary))
            parts.Add(process.Summary);
        parts.AddRange(process.Triggers);
        parts.AddRange(process.Steps.Select(s => s.Action));
        return string.Join(' ', parts.Where(p => !string.IsNullOrWhiteSpace(p)));
    }

    internal static int ScoreKeyword(CompanyProcess process, string query)
    {
        var normalizedQuery = query.ToLowerInvariant();
        var score = 0;

        if (process.Title.Contains(normalizedQuery, StringComparison.OrdinalIgnoreCase))
            score += 5;

        if (!string.IsNullOrWhiteSpace(process.Summary)
            && process.Summary.Contains(normalizedQuery, StringComparison.OrdinalIgnoreCase))
            score += 3;

        foreach (var trigger in process.Triggers)
        {
            if (trigger.Contains(normalizedQuery, StringComparison.OrdinalIgnoreCase)
                || normalizedQuery.Contains(trigger, StringComparison.OrdinalIgnoreCase))
                score += 4;
        }

        foreach (var step in process.Steps)
        {
            if (step.Action.Contains(normalizedQuery, StringComparison.OrdinalIgnoreCase))
                score += 1;
        }

        return score;
    }
}

public sealed record ProcessEmbeddingEntry(string ProcessId, float[] Vector);
