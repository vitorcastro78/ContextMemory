using System.Text.Json.Serialization;

namespace ContextMemory.Core.Models;

public record ContentRules
{
    [JsonPropertyName("blockedTopics")]
    public List<string> BlockedTopics { get; init; } = [];

    [JsonPropertyName("sensitiveTopics")]
    public List<string> SensitiveTopics { get; init; } = [];

    [JsonPropertyName("requiredDisclaimer")]
    public string RequiredDisclaimer { get; init; } = string.Empty;

    [JsonPropertyName("maxInputLength")]
    public int MaxInputLength { get; init; } = 8000;

    [JsonPropertyName("maxResponseLength")]
    public int MaxResponseLength { get; init; } = 8000;

    [JsonPropertyName("enforceLanguage")]
    public string? EnforceLanguage { get; init; }
}

public sealed class ContentFilterResult
{
    public bool IsBlocked { get; init; }
    public string? BlockReason { get; init; }
    public string? ModifiedContent { get; init; }
    public string? ContentToAppend { get; init; }
    public string AuditReason { get; init; } = string.Empty;

    public static ContentFilterResult Pass(string? modified = null) =>
        new() { ModifiedContent = modified };

    public static ContentFilterResult Block(string reason) =>
        new() { IsBlocked = true, BlockReason = reason, AuditReason = reason };
}
