using MemoryPack;

namespace ContextMemory.Core.Models;

[MemoryPackable]
public partial class SemanticMemoryPayload
{
    public List<SemanticFactEntry> Facts { get; set; } = [];
}

[MemoryPackable]
public partial class SemanticFactEntry
{
    public string Text { get; set; } = string.Empty;
    public float[] Vector { get; set; } = [];
    public DateTimeOffset LearnedAt { get; set; }
}
