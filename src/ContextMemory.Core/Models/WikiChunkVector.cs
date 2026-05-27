namespace ContextMemory.Core.Models;

public record WikiChunkVector
{
    public required string Source { get; init; }
    public required string HeaderPath { get; init; }
    public required string Content { get; init; }
    public required float[] Vector { get; init; }
    public bool IsLearned { get; init; }
    public float Similarity { get; init; }
}
