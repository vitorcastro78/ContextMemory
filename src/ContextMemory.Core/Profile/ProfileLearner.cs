using ContextMemory.Core.Contracts;
using Microsoft.Extensions.Logging;

namespace ContextMemory.Core.Profile;

public sealed class ProfileLearner : IProfileLearner
{
    private readonly IUserProfileStore _userProfileStore;
    private readonly ISemanticMemory _semanticMemory;
    private readonly ILogger<ProfileLearner> _logger;

    public ProfileLearner(
        IUserProfileStore userProfileStore,
        ISemanticMemory semanticMemory,
        ILogger<ProfileLearner> logger)
    {
        _userProfileStore = userProfileStore;
        _semanticMemory = semanticMemory;
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

    private async Task LearnFromTurnAsync(
        string appId,
        string userId,
        string userMessage,
        string assistantMessage)
    {
        try
        {
            var facts = ExtractFacts(userMessage, assistantMessage);
            if (facts.Count == 0)
                return;

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
        catch (Exception ex)
        {
            _logger.LogError(ex, "Profile learning failed for {AppId}/{UserId}", appId, userId);
        }
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
