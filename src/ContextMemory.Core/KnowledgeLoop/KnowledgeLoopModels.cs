using ContextMemory.Core.Models;

namespace ContextMemory.Core.KnowledgeLoop;

public sealed class KnowledgeLoopEntry
{
    public required string SessionId { get; init; }
    public required string AppId { get; init; }
    public required string UserId { get; init; }
    public required List<OllamaMessage> Messages { get; set; }
    public ConversationEvaluationResult? Evaluation { get; set; }
    public KnowledgeLoopStatus Status { get; set; }
    public string? IngestedPath { get; set; }
    public string? FailureReason { get; set; }
    public DateTimeOffset CreatedAt { get; init; }
    public DateTimeOffset? ProcessedAt { get; set; }
}

public record ConversationEvaluationResult
{
    public bool HasNewKnowledge { get; init; }
    public float Score { get; init; }
    public string Reasoning { get; init; } = string.Empty;
    public List<string> ExtractedTopics { get; init; } = [];
}

public record ExtractedKnowledge
{
    public required string AppId { get; init; }
    public required string UserId { get; init; }
    public required string Title { get; init; }
    public required string Content { get; init; }
    public required string Category { get; init; }
    public float Confidence { get; init; }
    public DateTimeOffset ExtractedAt { get; init; }
    public string SourceSessionId { get; init; } = string.Empty;
}

public record KnowledgeLoopStats
{
    public int SessionsEvaluated { get; init; }
    public int SessionsApproved { get; init; }
    public int ChunksCreated { get; init; }
    public int ChunksMerged { get; init; }
    public int ChunksRejected { get; init; }
    public DateTimeOffset? LastRunAt { get; init; }
}

public enum MergeAction
{
    Created,
    Merged
}

public record MergeResult
{
    public MergeAction Action { get; init; }
    public required string TargetPath { get; init; }
    public required string Content { get; init; }
}
