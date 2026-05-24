using System.Text.Json.Serialization;

namespace ContextMemory.Core.Models;

public record ContextMemoryMetadata
{
    [JsonPropertyName("message_id")]
    public string? MessageId { get; init; }
}
