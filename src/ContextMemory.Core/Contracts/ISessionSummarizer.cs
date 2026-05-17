namespace ContextMemory.Core.Contracts;

public interface ISessionSummarizer
{
    Task MaybeSummarizeAsync(
        string appId,
        string userId,
        string model,
        string llmBackend,
        CancellationToken cancellationToken = default);
}
