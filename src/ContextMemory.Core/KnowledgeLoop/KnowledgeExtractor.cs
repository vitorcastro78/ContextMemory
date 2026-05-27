using System.Text.Json;
using ContextMemory.Core.Contracts;
using ContextMemory.Core.Models;
using Microsoft.Extensions.Logging;

namespace ContextMemory.Core.KnowledgeLoop;

public sealed class KnowledgeExtractor
{
    private const string ExtractionPromptTemplate = """
        Com base na conversa abaixo, extrai o conhecimento de domínio reutilizável
        e formata-o como um documento Markdown estruturado.

        Responde APENAS com JSON válido:
        {
          "title": "título conciso do conhecimento (max 60 chars)",
          "category": "procedure|fact|definition|example|troubleshooting",
          "confidence": 0.0-1.0,
          "content": "## Título\n\nConteúdo em markdown...\n\n### Passos\n1. ...\n\n### Notas\n..."
        }

        Regras para o content:
        - Usa headers markdown (##, ###)
        - Remove referências pessoais ("o utilizador disse", "eu respondi")
        - Generaliza para ser reutilizável em contextos futuros
        - Inclui exemplos concretos quando presentes
        - Máximo 800 palavras
        - Língua: {language}

        CONVERSA:
        {conversation}
        """;

    private readonly ILlmAdapterResolver _adapterResolver;
    private readonly IAppConfigStore _appConfigStore;
    private readonly ILogger<KnowledgeExtractor> _logger;

    public KnowledgeExtractor(
        ILlmAdapterResolver adapterResolver,
        IAppConfigStore appConfigStore,
        ILogger<KnowledgeExtractor> logger)
    {
        _adapterResolver = adapterResolver;
        _appConfigStore = appConfigStore;
        _logger = logger;
    }

    public async Task<ExtractedKnowledge?> ExtractAsync(
        string appId,
        string userId,
        string sessionId,
        IReadOnlyList<OllamaMessage> messages,
        CancellationToken cancellationToken = default)
    {
        var config = _appConfigStore.GetConfig(appId);
        var adapter = _adapterResolver.Resolve(config.LlmBackend);

        var prompt = ExtractionPromptTemplate
            .Replace("{conversation}", FormatTranscript(messages), StringComparison.Ordinal)
            .Replace("{language}", config.DefaultLanguage, StringComparison.Ordinal);

        try
        {
            var response = await adapter.GenerateAsync(
                new OllamaGenerateRequest
                {
                    Model = config.LlmModel,
                    Prompt = prompt,
                    Stream = false,
                    Format = "json"
                },
                cancellationToken).ConfigureAwait(false);

            return ParseExtractionResponse(appId, userId, sessionId, response.Response ?? string.Empty);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Knowledge extraction failed for {AppId}/{SessionId}", appId, sessionId);
            return null;
        }
    }

    private static string FormatTranscript(IReadOnlyList<OllamaMessage> messages) =>
        string.Join("\n", messages
            .Where(m => m.Role is "user" or "assistant")
            .Select(m => $"[{m.Role.ToUpperInvariant()}]: {m.Content}"));

    private static ExtractedKnowledge? ParseExtractionResponse(
        string appId,
        string userId,
        string sessionId,
        string json)
    {
        try
        {
            var trimmed = json.Trim();
            var start = trimmed.IndexOf('{');
            var end = trimmed.LastIndexOf('}');
            if (start >= 0 && end > start)
                trimmed = trimmed[start..(end + 1)];

            using var doc = JsonDocument.Parse(trimmed);
            var root = doc.RootElement;
            var title = root.GetProperty("title").GetString() ?? "Conhecimento";
            var content = root.GetProperty("content").GetString() ?? string.Empty;
            if (string.IsNullOrWhiteSpace(content))
                return null;

            return new ExtractedKnowledge
            {
                AppId = appId,
                UserId = userId,
                Title = title,
                Content = content,
                Category = root.TryGetProperty("category", out var c) ? c.GetString() ?? "fact" : "fact",
                Confidence = root.TryGetProperty("confidence", out var conf) ? conf.GetSingle() : 0.7f,
                ExtractedAt = DateTimeOffset.UtcNow,
                SourceSessionId = sessionId
            };
        }
        catch
        {
            return null;
        }
    }
}
