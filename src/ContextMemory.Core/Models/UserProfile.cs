using System.Text.Json.Serialization;

namespace ContextMemory.Core.Models;

public record UserProfileData
{
    [JsonPropertyName("sessionContext")]
    public string? SessionContext { get; init; }

    [JsonPropertyName("facts")]
    public List<UserFact> Facts { get; init; } = [];
}

public record UserFact
{
    [JsonPropertyName("text")]
    public required string Text { get; init; }

    [JsonPropertyName("learnedAt")]
    public DateTimeOffset LearnedAt { get; init; }

    [JsonPropertyName("lastConfirmedAt")]
    public DateTimeOffset LastConfirmedAt { get; init; }

    [JsonPropertyName("confidence")]
    public float Confidence { get; init; }
}
