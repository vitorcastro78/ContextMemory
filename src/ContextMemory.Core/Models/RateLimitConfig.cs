using System.Text.Json.Serialization;

namespace ContextMemory.Core.Models;

public record RateLimitConfig
{
    [JsonPropertyName("requestsPerMinute")]
    public int RequestsPerMinute { get; init; } = 60;

    [JsonPropertyName("tokensPerMinute")]
    public int TokensPerMinute { get; init; } = 100_000;

    [JsonPropertyName("userRequestsPerMinute")]
    public int UserRequestsPerMinute { get; init; } = 30;
}
