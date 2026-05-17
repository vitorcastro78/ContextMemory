using ContextMemory.Core.Models;

namespace ContextMemory.Core.Contracts;

public interface IContextEngine
{
    Task<ChatPipelineResult> ProcessChatAsync(
        string appId,
        string userId,
        OllamaRequest request,
        CancellationToken cancellationToken = default);

    IAsyncEnumerable<OllamaResponse> ProcessChatStreamAsync(
        string appId,
        string userId,
        OllamaRequest request,
        CancellationToken cancellationToken = default);

    Task<ChatPipelineResult> FinalizeStreamAsync(
        string appId,
        string userId,
        OllamaRequest request,
        string assistantContent,
        CancellationToken cancellationToken = default);
}
