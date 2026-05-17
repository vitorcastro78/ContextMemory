namespace ContextMemory.Core.Models;

public record AppProfile
{
    public required string AppId { get; init; }
    public required string ApiKey { get; init; }
    public string SystemPrompt { get; init; } = string.Empty;
    public string DefaultLanguage { get; init; } = "pt-PT";
    public string WikiPath { get; init; } = string.Empty;
    public int MaxHistoryMessages { get; init; } = 20;
    public int WikiChunksTopK { get; init; } = 5;
    public float SimilarityThreshold { get; init; } = 0.65f;
}
