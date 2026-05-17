namespace ContextMemory.Core.Contracts;

public interface ISemanticMemory
{
    Task StoreFactAsync(
        string appId,
        string userId,
        string factText,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<string>> SearchAsync(
        string appId,
        string userId,
        string query,
        int topK = 3,
        float threshold = 0.55f,
        CancellationToken cancellationToken = default);
}
