using System.Text.Json.Serialization;

namespace ContextMemory.Core.Models;

public record PlanPatchRequest
{
    [JsonPropertyName("planId")]
    public string? PlanId { get; init; }
}
