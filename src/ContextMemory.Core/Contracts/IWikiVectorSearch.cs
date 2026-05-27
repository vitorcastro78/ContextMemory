namespace ContextMemory.Core.Contracts;

public record WikiVectorHit(string Source, string Content, float Similarity);

public interface IWikiVectorSearch
{
    Task<IReadOnlyList<WikiVectorHit>> SearchLearnedAsync(
        string appId,
        string queryText,
        int topK,
        float threshold,
        CancellationToken cancellationToken = default);
}
