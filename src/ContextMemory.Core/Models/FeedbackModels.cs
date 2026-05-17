using System.Text.Json.Serialization;

namespace ContextMemory.Core.Models;

public record FeedbackRequest
{
    [JsonPropertyName("messageId")]
    public required string MessageId { get; init; }

    [JsonPropertyName("score")]
    public int Score { get; init; }

    [JsonPropertyName("reason")]
    public string? Reason { get; init; }
}

public record FeedbackEntry
{
    public required string MessageId { get; init; }
    public required string AppId { get; init; }
    public required string UserId { get; init; }
    public int Score { get; init; }
    public string? Reason { get; init; }
    public DateTimeOffset Timestamp { get; init; }
    public bool IsImplicit { get; init; }
}
