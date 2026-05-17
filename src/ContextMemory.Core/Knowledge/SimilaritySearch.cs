using System.Numerics;
using ContextMemory.Core.Models;

namespace ContextMemory.Core.Knowledge;

public sealed class SimilaritySearch
{
    public IReadOnlyList<WikiChunk> Search(
        IReadOnlyList<VectorEntry> entries,
        float[] queryVector,
        int topK,
        float threshold)
    {
        if (entries.Count == 0 || queryVector.Length == 0)
            return [];

        var scored = new List<(VectorEntry Entry, float Score)>(entries.Count);
        foreach (var entry in entries)
        {
            if (entry.Vector.Length != queryVector.Length)
                continue;

            var score = CosineSimilarity(queryVector, entry.Vector);
            if (score >= threshold)
                scored.Add((entry, score));
        }

        return scored
            .OrderByDescending(s => s.Score)
            .Take(topK)
            .Select(s => new WikiChunk(s.Entry.Text, s.Entry.Source, ExtractHeaderPath(s.Entry.Text)))
            .ToList();
    }

    internal static float CosineSimilarity(ReadOnlySpan<float> a, ReadOnlySpan<float> b)
    {
        if (a.Length != b.Length || a.IsEmpty)
            return 0f;

        var dot = 0f;
        var normA = 0f;
        var normB = 0f;
        var index = 0;

        if (Vector.IsHardwareAccelerated)
        {
            var width = Vector<float>.Count;
            for (; index <= a.Length - width; index += width)
            {
                var va = new Vector<float>(a[index..]);
                var vb = new Vector<float>(b[index..]);
                dot += Vector.Dot(va, vb);
                normA += Vector.Dot(va, va);
                normB += Vector.Dot(vb, vb);
            }
        }

        for (; index < a.Length; index++)
        {
            dot += a[index] * b[index];
            normA += a[index] * a[index];
            normB += b[index] * b[index];
        }

        if (normA <= 0f || normB <= 0f)
            return 0f;

        return dot / (MathF.Sqrt(normA) * MathF.Sqrt(normB));
    }

    private static string ExtractHeaderPath(string text)
    {
        var end = text.IndexOf(']');
        if (end <= 1 || text[0] != '[')
            return string.Empty;

        var headerPart = text[1..end];
        var separator = headerPart.IndexOf(" > ", StringComparison.Ordinal);
        return separator < 0 ? string.Empty : headerPart[(separator + 3)..];
    }
}
