namespace ContextMemory.Core.Models;

public sealed class ChatPipelineResult
{
    public OllamaResponse? Response { get; init; }
    public string? MessageId { get; init; }
    public bool IsBlocked { get; init; }
    public int StatusCode { get; init; } = 200;
    public string? ErrorBody { get; init; }
    public bool RagHit { get; init; }
    public int EstimatedPromptTokens { get; init; }
    public int EstimatedCompletionTokens { get; init; }
}
