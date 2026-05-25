namespace ContextMemory.Core.Models;

public record PromptContext
{
    public required AppRuntimeConfig AppConfig { get; init; }
    public required UserProfileData UserProfile { get; init; }
    public IReadOnlyList<WikiChunk> WikiChunks { get; init; } = [];
    public MessageIntent Intent { get; init; } = MessageIntent.General;
    public string? SessionContext { get; init; }
    public IReadOnlyList<CompanyProcess> ExecutableProcesses { get; init; } = [];
}
