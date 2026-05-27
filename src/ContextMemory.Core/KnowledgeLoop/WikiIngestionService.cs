using ContextMemory.Core.Contracts;
using Microsoft.Extensions.Logging;

namespace ContextMemory.Core.KnowledgeLoop;

public sealed class WikiIngestionService
{
    private readonly IWikiIndexService _wikiIndex;
    private readonly IAppRegistry _appRegistry;
    private readonly ILogger<WikiIngestionService> _logger;

    public WikiIngestionService(
        IWikiIndexService wikiIndex,
        IAppRegistry appRegistry,
        ILogger<WikiIngestionService> logger)
    {
        _wikiIndex = wikiIndex;
        _appRegistry = appRegistry;
        _logger = logger;
    }

    public async Task IngestAsync(
        string appId,
        MergeResult mergeResult,
        CancellationToken cancellationToken = default)
    {
        if (!_appRegistry.TryGetApp(appId, out var app) || app is null)
            throw new InvalidOperationException($"App '{appId}' not found.");

        var wikiPath = Path.GetFullPath(app.WikiPath);
        var learnedDir = Path.Combine(wikiPath, "learned");
        Directory.CreateDirectory(learnedDir);

        var fullPath = Path.Combine(wikiPath, mergeResult.TargetPath.Replace('/', Path.DirectorySeparatorChar));
        var dir = Path.GetDirectoryName(fullPath);
        if (!string.IsNullOrEmpty(dir))
            Directory.CreateDirectory(dir);

        var contentWithMeta = $"""
            ---
            generated: true
            generated_at: {DateTimeOffset.UtcNow:yyyy-MM-ddTHH:mm:ssZ}
            action: {mergeResult.Action.ToString().ToLowerInvariant()}
            ---

            {mergeResult.Content}
            """;

        await File.WriteAllTextAsync(fullPath, contentWithMeta, cancellationToken).ConfigureAwait(false);

        var relativePath = Path.GetRelativePath(wikiPath, fullPath).Replace('\\', '/');
        await _wikiIndex
            .ReindexFileAsync(appId, wikiPath, relativePath, cancellationToken)
            .ConfigureAwait(false);

        _logger.LogInformation(
            "Ingested {Action} knowledge chunk: {Path} for {AppId}",
            mergeResult.Action,
            relativePath,
            appId);
    }
}
