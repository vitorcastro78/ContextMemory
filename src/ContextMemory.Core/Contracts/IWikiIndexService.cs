using ContextMemory.Core.Models;

namespace ContextMemory.Core.Contracts;

public interface IWikiIndexService
{
    Task EnsureIndexedAsync(string appId, string wikiPath, CancellationToken cancellationToken = default);
    Task ReindexFileAsync(
        string appId,
        string wikiPath,
        string relativeFilePath,
        CancellationToken cancellationToken = default);
    Task<IReadOnlyList<WikiChunk>> SearchAsync(
        string appId,
        string query,
        int topK,
        float threshold,
        CancellationToken cancellationToken = default);
}
