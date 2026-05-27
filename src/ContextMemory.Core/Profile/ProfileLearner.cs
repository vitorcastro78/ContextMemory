using System.Text.Json;
using ContextMemory.Core.Contracts;
using ContextMemory.Core.Models;
using Microsoft.Extensions.Logging;

namespace ContextMemory.Core.Profile;

public sealed class ProfileLearner : IProfileLearner
{
    private readonly IUserProfileStore _userProfileStore;
    private readonly ISemanticMemory _semanticMemory;
    private readonly IFeedbackStore _feedbackStore;
    private readonly IAppConfigStore _appConfigStore;
    private readonly ILlmAdapterResolver? _adapterResolver;
    private readonly ILogger<ProfileLearner> _logger;

    private const string FactExtractionPrompt = """
        Analisa a seguinte mensagem do utilizador e identifica factos
        reutilizáveis sobre as suas preferências, competências ou contexto de trabalho.

        Responde APENAS com um array JSON de strings (pode ser vazio []):
        ["facto 1", "facto 2"]

        MENSAGEM: {message}
        """;

    public ProfileLearner(
        IUserProfileStore userProfileStore,
        ISemanticMemory semanticMemory,
        IFeedbackStore feedbackStore,
        IAppConfigStore appConfigStore,
        ILogger<ProfileLearner> logger,
        ILlmAdapterResolver? adapterResolver = null)
    {
        _userProfileStore = userProfileStore;
        _semanticMemory = semanticMemory;
        _feedbackStore = feedbackStore;
        _appConfigStore = appConfigStore;
        _adapterResolver = adapterResolver;
        _logger = logger;
    }

    public void LearnFromTurn(
        string appId,
        string userId,
        string userMessage,
        string assistantMessage)
    {
        _ = LearnFromTurnAsync(appId, userId, userMessage, assistantMessage);
    }

    public void LearnFromFeedback(string appId, string userId, int score, string? reason)
    {
        _ = LearnFromFeedbackAsync(appId, userId, score, reason);
    }

    private async Task LearnFromTurnAsync(
        string appId,
        string userId,
        string userMessage,
        string assistantMessage)
    {
        try
        {
            var facts = await ExtractFactsAsync(appId, userMessage, assistantMessage).ConfigureAwait(false);
            if (facts.Count > 0)
            {
                await _userProfileStore
                    .AddOrConfirmFactsAsync(appId, userId, facts, 0.6f, CancellationToken.None)
                    .ConfigureAwait(false);

                foreach (var fact in facts)
                {
                    await _semanticMemory
                        .StoreFactAsync(appId, userId, fact, CancellationToken.None)
                        .ConfigureAwait(false);
                }

                _logger.LogInformation(
                    "Learned {Count} profile fact(s) for {AppId}/{UserId}",
                    facts.Count,
                    appId,
                    userId);
            }

            await ApplyFeedbackHistoryAsync(appId, userId).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Profile learning failed for {AppId}/{UserId}", appId, userId);
        }
    }

    private async Task LearnFromFeedbackAsync(
        string appId,
        string userId,
        int score,
        string? reason)
    {
        try
        {
            if (score > 0 && reason is not null)
            {
                var positiveFact = MapPositiveFeedbackToFact(reason);
                if (positiveFact is not null)
                {
                    await _userProfileStore
                        .AddOrConfirmFactsAsync(appId, userId, [positiveFact], 0.8f, CancellationToken.None)
                        .ConfigureAwait(false);
                }
            }

            if (score < 0)
                await ApplyNegativeFeedbackAsync(appId, userId, reason).ConfigureAwait(false);

            await ApplyFeedbackHistoryAsync(appId, userId).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Feedback learning failed for {AppId}/{UserId}", appId, userId);
        }
    }

    private async Task ApplyFeedbackHistoryAsync(string appId, string userId)
    {
        var entries = await _feedbackStore.GetByAppAsync(appId, CancellationToken.None).ConfigureAwait(false);
        var userEntries = entries
            .Where(e => string.Equals(e.UserId, userId, StringComparison.Ordinal))
            .OrderByDescending(e => e.Timestamp)
            .Take(50)
            .ToList();

        if (userEntries.Count == 0)
            return;

        var negativeLong = userEntries.Count(e =>
            e.Score < 0
            && e.Reason is not null
            && (e.Reason.Contains("long", StringComparison.OrdinalIgnoreCase)
                || e.Reason.Contains("curto", StringComparison.OrdinalIgnoreCase)));

        if (negativeLong >= 3)
        {
            await _userProfileStore
                .AddOrConfirmFactsAsync(appId, userId, ["Prefere respostas curtas e concisas"], 0.9f, CancellationToken.None)
                .ConfigureAwait(false);
        }
    }

    private async Task ApplyNegativeFeedbackAsync(string appId, string userId, string? reason)
    {
        if (reason is not null && (
            reason.Contains("curto", StringComparison.OrdinalIgnoreCase)
            || reason.Contains("long", StringComparison.OrdinalIgnoreCase)))
        {
            var count = await _feedbackStore
                .CountNegativeByReasonAsync(appId, "long", CancellationToken.None)
                .ConfigureAwait(false);

            if (count >= 3)
            {
                await _userProfileStore
                    .AddOrConfirmFactsAsync(appId, userId, ["Prefere respostas curtas e concisas"], 0.85f, CancellationToken.None)
                    .ConfigureAwait(false);
            }
        }

        if (reason is not null && reason.Contains("format", StringComparison.OrdinalIgnoreCase))
        {
            var count = await _feedbackStore
                .CountNegativeByReasonAsync(appId, "format", CancellationToken.None)
                .ConfigureAwait(false);

            if (count >= 3)
            {
                var config = _appConfigStore.GetConfig(appId);
                await _appConfigStore.UpdateAsync(appId, new AppConfigPatchRequest
                {
                    FormatRules = config.FormatRules + "\n- Prioriza respostas em formato de lista numerada."
                }, CancellationToken.None).ConfigureAwait(false);
            }
        }
    }

    private static string? MapPositiveFeedbackToFact(string reason) =>
        reason.ToLowerInvariant() switch
        {
            var r when r.Contains("curto") || r.Contains("concis") => "Prefere respostas curtas e concisas",
            var r when r.Contains("detalh") => "Prefere respostas detalhadas",
            var r when r.Contains("lista") || r.Contains("format") => "Prefere respostas em formato de lista",
            _ => null
        };

    private async Task<List<string>> ExtractFactsAsync(
        string appId,
        string userMessage,
        string assistantMessage)
    {
        if (_adapterResolver is not null)
        {
            try
            {
                var llmFacts = await ExtractFactsWithLlmAsync(appId, userMessage, CancellationToken.None)
                    .ConfigureAwait(false);
                if (llmFacts.Count > 0)
                    return llmFacts;
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "LLM fact extraction failed, using keyword fallback for {AppId}", appId);
            }
        }

        return ExtractFacts(userMessage, assistantMessage);
    }

    private async Task<List<string>> ExtractFactsWithLlmAsync(
        string appId,
        string userMessage,
        CancellationToken cancellationToken)
    {
        var config = _appConfigStore.GetConfig(appId);
        var adapter = _adapterResolver!.Resolve(config.LlmBackend);
        var response = await adapter.GenerateAsync(
            new OllamaGenerateRequest
            {
                Model = config.LlmModel,
                Prompt = FactExtractionPrompt.Replace("{message}", userMessage, StringComparison.Ordinal),
                Stream = false,
                Format = "json"
            },
            cancellationToken).ConfigureAwait(false);

        var json = response.Response ?? "[]";
        var start = json.IndexOf('[');
        var end = json.LastIndexOf(']');
        if (start >= 0 && end > start)
            json = json[start..(end + 1)];

        return JsonSerializer.Deserialize<List<string>>(json) ?? [];
    }

    internal static List<string> ExtractFacts(string userMessage, string assistantMessage)
    {
        var facts = new List<string>();
        var user = userMessage.ToLowerInvariant();

        if (ContainsAny(user, "mais curto", "resumo", "breve", "conciso", "shorter", "brief"))
            facts.Add("Prefere respostas curtas e concisas");

        if (ContainsAny(user, "mais detalh", "explica melhor", "aprofunda", "detailed", "elaborate"))
            facts.Add("Prefere respostas detalhadas");

        if (ContainsAny(user, "em inglês", "em ingles", "in english", "english please"))
            facts.Add("Prefere comunicação em inglês");

        if (ContainsAny(user, "em português", "em portugues", "pt-pt", "pt pt"))
            facts.Add("Prefere comunicação em português");

        if (ContainsAny(user, "pep", "kyc", "aml", "compliance", "due diligence"))
            facts.Add("Trabalha frequentemente com temas KYC/AML e compliance");

        if (ContainsAny(user, "relatório", "relatorio", "report", "parecer"))
            facts.Add("Solicita frequentemente relatórios estruturados");

        if (ContainsAny(user, "lista", "passos", "procedimento", "checklist"))
            facts.Add("Prefere respostas em formato de lista ou procedimento");

        if (assistantMessage.Length > 2500 && ContainsAny(user, "muito longo", "too long", "resume"))
            facts.Add("Prefere respostas curtas e concisas");

        return facts.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
    }

    private static bool ContainsAny(string text, params string[] needles) =>
        needles.Any(n => text.Contains(n, StringComparison.Ordinal));
}
