namespace ContextMemory.Core.Models;

public record AppRuntimeConfig
{
    public required string AppId { get; init; }
    public string BasePersona { get; init; } = string.Empty;
    public string BusinessRules { get; init; } = string.Empty;
    public string FormatRules { get; init; } = string.Empty;
    public string DefaultLanguage { get; init; } = "pt-PT";
    public string LlmModel { get; init; } = "llama3.2";
    public string LlmBackend { get; init; } = "ollama";
    public int MaxHistoryMessages { get; init; } = 20;
    public int WikiChunksTopK { get; init; } = 5;
    public float SimilarityThreshold { get; init; } = 0.65f;
    public bool StreamingEnabled { get; init; } = true;
    public RateLimitConfig RateLimits { get; init; } = new();
}
