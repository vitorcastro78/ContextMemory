using ContextMemory.Core.Configuration;
using ContextMemory.Core.Contracts;
using ContextMemory.Core.Models;
using Microsoft.Extensions.Options;

namespace ContextMemory.Api.Hosting;

public sealed class AppConfigBootstrapHostedService : IHostedService
{
    private readonly IAppConfigStore _appConfigStore;
    private readonly ContextMemoryOptions _options;

    public AppConfigBootstrapHostedService(
        IAppConfigStore appConfigStore,
        IOptions<ContextMemoryOptions> options)
    {
        _appConfigStore = appConfigStore;
        _options = options.Value;
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        foreach (var (appId, entry) in _options.Apps)
        {
            var seed = new AppRuntimeConfig
            {
                AppId = appId,
                BasePersona = entry.SystemPrompt,
                BusinessRules = GetDefaultBusinessRules(appId),
                FormatRules = GetDefaultFormatRules(),
                DefaultLanguage = entry.DefaultLanguage,
                MaxHistoryMessages = entry.MaxHistoryMessages > 0
                    ? entry.MaxHistoryMessages
                    : _options.MaxHistoryMessages,
                WikiChunksTopK = entry.WikiChunksTopK > 0
                    ? entry.WikiChunksTopK
                    : _options.WikiChunksTopK,
                SimilarityThreshold = entry.SimilarityThreshold > 0
                    ? entry.SimilarityThreshold
                    : _options.SimilarityThreshold
            };

            _appConfigStore.EnsureProfileExists(appId, seed);
            EnsureContentRules(appId);
            await _appConfigStore.ReloadAsync(appId, cancellationToken).ConfigureAwait(false);
        }
    }

    private void EnsureContentRules(string appId)
    {
        var profilesRoot = Path.Combine(
            Path.GetFullPath(_options.DataPath, _options.ContentRootPath),
            "app-profiles");
        var rulesPath = Path.Combine(profilesRoot, appId, "content-rules.json");
        if (File.Exists(rulesPath))
            return;

        Directory.CreateDirectory(Path.GetDirectoryName(rulesPath)!);
        var rules = appId.Contains("kyc", StringComparison.OrdinalIgnoreCase)
            ? """
              {
                "blockedTopics": ["concorrentes", "preços internos"],
                "requiredDisclaimer": "Esta resposta não constitui aconselhamento jurídico vinculativo.",
                "maxInputLength": 8000,
                "maxResponseLength": 4000,
                "enforceLanguage": "pt-PT"
              }
              """
            : """
              {
                "blockedTopics": [],
                "maxInputLength": 8000,
                "maxResponseLength": 4000
              }
              """;
        File.WriteAllText(rulesPath, rules);
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    private static string GetDefaultBusinessRules(string appId) =>
        appId.Contains("kyc", StringComparison.OrdinalIgnoreCase)
            ? """
              - Nunca fornecer aconselhamento jurídico vinculativo.
              - Citar sempre a base regulatória quando aplicável.
              - Assinalar riscos de compliance quando identificados.
              """
            : string.Empty;

    private static string GetDefaultFormatRules() =>
        """
        - Usa markdown quando apropriado.
        - Destaca termos regulatórios relevantes.
        """;
}
