using ContextMemory.Core.Models;

namespace ContextMemory.Core.Contracts;

public interface ILlmAdapter
{
    Task<OllamaResponse> ChatAsync(OllamaRequest request, CancellationToken cancellationToken = default);
    IAsyncEnumerable<OllamaResponse> ChatStreamAsync(OllamaRequest request, CancellationToken cancellationToken = default);
    Task<OllamaResponse> GenerateAsync(OllamaGenerateRequest request, CancellationToken cancellationToken = default);
    Task<bool> IsHealthyAsync(CancellationToken cancellationToken = default);
}
