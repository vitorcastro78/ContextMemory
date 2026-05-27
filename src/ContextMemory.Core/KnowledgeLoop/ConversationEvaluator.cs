using System.Text.Json;
using ContextMemory.Core.Contracts;
using ContextMemory.Core.Models;
using Microsoft.Extensions.Logging;

namespace ContextMemory.Core.KnowledgeLoop;

public sealed class ConversationEvaluator
{
    private const string EvaluationPromptTemplate = """
        Analisa a seguinte conversa e determina se contém conhecimento de domínio
        novo e reutilizável (factos, procedimentos, definições, exemplos concretos).

        Responde APENAS com JSON válido, sem texto adicional:
        {
          "has_new_knowledge": true|false,
          "score": 0.0-1.0,
          "reasoning": "explicação em 1-2 frases",
          "topics": ["tópico1", "tópico2"]
        }

        Critérios para score > 0.6 (aprovação):
        - Contém factos específicos do domínio não óbvios
        - Descreve procedimentos ou passos concretos
        - Esclarece conceitos com exemplos reais
        - Resolve um problema de forma documentável

        Critérios para rejeição (score < 0.4):
        - Apenas saudações ou perguntas genéricas
        - Informação já conhecida e trivial
        - Conversas pessoais sem conteúdo de domínio
        - Erros ou desinformação

        CONVERSA:
        {conversation}
        """;

    private readonly ILlmAdapterResolver _adapterResolver;
    private readonly IAppConfigStore _appConfigStore;
    private readonly ILogger<ConversationEvaluator> _logger;

    public ConversationEvaluator(
        ILlmAdapterResolver adapterResolver,
        IAppConfigStore appConfigStore,
        ILogger<ConversationEvaluator> logger)
    {
        _adapterResolver = adapterResolver;
        _appConfigStore = appConfigStore;
        _logger = logger;
    }

    public async Task<ConversationEvaluationResult> EvaluateAsync(
        string appId,
        IReadOnlyList<OllamaMessage> messages,
        CancellationToken cancellationToken = default)
    {
        var config = _appConfigStore.GetConfig(appId);
        var adapter = _adapterResolver.Resolve(config.LlmBackend);

        var transcript = FormatTranscript(messages);
        var prompt = EvaluationPromptTemplate.Replace("{conversation}", transcript, StringComparison.Ordinal);

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

            return ParseEvaluationResponse(response.Response ?? string.Empty);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "KnowledgeLoop evaluation failed for {AppId}", appId);
            return new ConversationEvaluationResult { HasNewKnowledge = false, Score = 0 };
        }
    }

    private static string FormatTranscript(IReadOnlyList<OllamaMessage> messages) =>
        string.Join("\n", messages
            .Where(m => m.Role is "user" or "assistant")
            .Select(m => $"[{m.Role.ToUpperInvariant()}]: {m.Content}"));

    private static ConversationEvaluationResult ParseEvaluationResponse(string json)
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
            return new ConversationEvaluationResult
            {
                HasNewKnowledge = root.TryGetProperty("has_new_knowledge", out var h) && h.GetBoolean(),
                Score = root.TryGetProperty("score", out var s) ? s.GetSingle() : 0,
                Reasoning = root.TryGetProperty("reasoning", out var r) ? r.GetString() ?? string.Empty : string.Empty,
                ExtractedTopics = root.TryGetProperty("topics", out var topics)
                    ? topics.EnumerateArray()
                        .Select(e => e.GetString() ?? string.Empty)
                        .Where(t => !string.IsNullOrEmpty(t))
                        .ToList()
                    : []
            };
        }
        catch
        {
            return new ConversationEvaluationResult { HasNewKnowledge = false, Score = 0 };
        }
    }
}
