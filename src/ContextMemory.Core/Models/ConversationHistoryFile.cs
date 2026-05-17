namespace ContextMemory.Core.Models;

public sealed class ConversationHistoryFile
{
    public string? SessionSummary { get; set; }
    public List<OllamaMessage> Messages { get; set; } = [];
}
