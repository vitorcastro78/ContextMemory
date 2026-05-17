namespace ContextMemory.Core.Models;

public sealed class VectorEntry
{
    public required float[] Vector { get; init; }
    public required string Text { get; init; }
    public required string Source { get; init; }
    public required string AppId { get; init; }
}
