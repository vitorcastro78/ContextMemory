using System.Text.Json.Serialization;

namespace ContextMemory.Core.Models;

public record AppConfigFile
{
    [JsonPropertyName("defaultLanguage")]
    public string DefaultLanguage { get; init; } = "pt-PT";

    [JsonPropertyName("llmModel")]
    public string LlmModel { get; init; } = "llama3.2";

    [JsonPropertyName("llmBackend")]
    public string LlmBackend { get; init; } = "ollama";

    [JsonPropertyName("maxHistoryMessages")]
    public int MaxHistoryMessages { get; init; } = 20;

    [JsonPropertyName("wikiChunksTopK")]
    public int WikiChunksTopK { get; init; } = 5;

    [JsonPropertyName("similarityThreshold")]
    public float SimilarityThreshold { get; init; } = 0.65f;

    [JsonPropertyName("streamingEnabled")]
    public bool StreamingEnabled { get; init; } = true;

    [JsonPropertyName("rateLimits")]
    public RateLimitConfig? RateLimits { get; init; }

    [JsonPropertyName("knowledgeLoopEnabled")]
    public bool KnowledgeLoopEnabled { get; init; }

    [JsonPropertyName("knowledgeLoopMinMessages")]
    public int KnowledgeLoopMinMessages { get; init; } = 6;

    [JsonPropertyName("knowledgeLoopAutoApproveThreshold")]
    public float KnowledgeLoopAutoApproveThreshold { get; init; } = 0.75f;

    [JsonPropertyName("knowledgeLoopManualReviewThreshold")]
    public float KnowledgeLoopManualReviewThreshold { get; init; } = 0.50f;

    [JsonPropertyName("knowledgeLoopMaxChunksPerDay")]
    public int KnowledgeLoopMaxChunksPerDay { get; init; } = 20;

    [JsonPropertyName("toolCallEnabled")]
    public bool ToolCallEnabled { get; init; }

    [JsonPropertyName("toolCallMaxIterations")]
    public int ToolCallMaxIterations { get; init; } = 5;

    [JsonPropertyName("planId")]
    public string PlanId { get; init; } = "pro";
}

public record AppConfigPatchRequest
{
    [JsonPropertyName("defaultLanguage")]
    public string? DefaultLanguage { get; init; }

    [JsonPropertyName("llmModel")]
    public string? LlmModel { get; init; }

    [JsonPropertyName("llmBackend")]
    public string? LlmBackend { get; init; }

    [JsonPropertyName("maxHistoryMessages")]
    public int? MaxHistoryMessages { get; init; }

    [JsonPropertyName("wikiChunksTopK")]
    public int? WikiChunksTopK { get; init; }

    [JsonPropertyName("similarityThreshold")]
    public float? SimilarityThreshold { get; init; }

    [JsonPropertyName("streamingEnabled")]
    public bool? StreamingEnabled { get; init; }

    [JsonPropertyName("basePersona")]
    public string? BasePersona { get; init; }

    [JsonPropertyName("businessRules")]
    public string? BusinessRules { get; init; }

    [JsonPropertyName("formatRules")]
    public string? FormatRules { get; init; }
}
