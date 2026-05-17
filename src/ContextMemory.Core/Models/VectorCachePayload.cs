using MemoryPack;

namespace ContextMemory.Core.Models;

[MemoryPackable]
public partial class VectorCachePayload
{
    public string AppId { get; set; } = string.Empty;
    public List<VectorCacheEntry> Entries { get; set; } = [];
}

[MemoryPackable]
public partial class VectorCacheEntry
{
    public float[] Vector { get; set; } = [];
    public string Text { get; set; } = string.Empty;
    public string Source { get; set; } = string.Empty;
}
