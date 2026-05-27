using System.Text.RegularExpressions;
using ContextMemory.Core.Contracts;
using ContextMemory.Core.Models;
using Microsoft.Extensions.Logging;

namespace ContextMemory.Core.KnowledgeLoop;

public sealed class KnowledgeMerger
{
    private const float MergeThreshold = 0.85f;
    private readonly IPgVectorStore _vectorStore;
    private readonly IEmbeddingEngine _embeddingEngine;
    private readonly ILlmAdapterResolver _adapterResolver;
    private readonly IAppConfigStore _appConfigStore;
    private readonly ILogger<KnowledgeMerger> _logger;

    public KnowledgeMerger(
        IPgVectorStore vectorStore,
        IEmbeddingEngine embeddingEngine,
        ILlmAdapterResolver adapterResolver,
        IAppConfigStore appConfigStore,
        ILogger<KnowledgeMerger> logger)
    {
        _vectorStore = vectorStore;
        _embeddingEngine = embeddingEngine;
        _adapterResolver = adapterResolver;
        _appConfigStore = appConfigStore;
        _logger = logger;
    }

    public async Task<MergeResult> MergeOrCreateAsync(
        string appId,
        ExtractedKnowledge knowledge,
        CancellationToken cancellationToken = default)
    {
        var queryText = knowledge.Title + "\n" + knowledge.Content;
        var queryVector = await _embeddingEngine.EmbedAsync(queryText, cancellationToken).ConfigureAwait(false);
        var similar = await _vectorStore
            .SearchLearnedAsync(appId, queryVector, topK: 3, threshold: MergeThreshold, cancellationToken)
            .ConfigureAwait(false);

        if (similar.Count > 0)
        {
            var best = similar[0];
            var merged = await MergeWithLlmAsync(appId, best.Content, knowledge.Content, cancellationToken)
                .ConfigureAwait(false);

            return new MergeResult
            {
                Action = MergeAction.Merged,
                TargetPath = best.Source,
                Content = merged
            };
        }

        return new MergeResult
        {
            Action = MergeAction.Created,
            TargetPath = GenerateWikiPath(knowledge),
            Content = knowledge.Content
        };
    }

    private async Task<string> MergeWithLlmAsync(
        string appId,
        string existing,
        string newContent,
        CancellationToken cancellationToken)
    {
        var config = _appConfigStore.GetConfig(appId);
        var adapter = _adapterResolver.Resolve(config.LlmBackend);

        var prompt = $"""
            Merge os dois documentos de conhecimento abaixo num único documento
            coerente em Markdown. Elimina redundâncias, mantém todos os factos
            únicos de ambos. Responde APENAS com o markdown resultante.

            DOCUMENTO EXISTENTE:
            {existing}

            NOVO CONHECIMENTO:
            {newContent}
            """;

        try
        {
            var response = await adapter.GenerateAsync(
                new OllamaGenerateRequest
                {
                    Model = config.LlmModel,
                    Prompt = prompt,
                    Stream = false
                },
                cancellationToken).ConfigureAwait(false);

            return response.Response ?? existing;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "LLM merge failed for {AppId}, keeping existing content", appId);
            return existing + "\n\n" + newContent;
        }
    }

    private static string GenerateWikiPath(ExtractedKnowledge k)
    {
        var slug = Regex.Replace(k.Title.ToLowerInvariant(), @"[^a-z0-9]+", "-").Trim('-');
        if (string.IsNullOrEmpty(slug))
            slug = "knowledge";
        var date = k.ExtractedAt.ToString("yyyy-MM");
        return $"learned/{date}-{slug}.md";
    }
}
