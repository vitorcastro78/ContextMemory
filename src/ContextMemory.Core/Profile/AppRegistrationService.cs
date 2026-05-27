using System.Security.Cryptography;
using ContextMemory.Core.Configuration;
using ContextMemory.Core.Contracts;
using ContextMemory.Core.Models;
using ContextMemory.Core.Security;
using Microsoft.Extensions.Options;

namespace ContextMemory.Core.Profile;

public sealed class AppRegistrationService : IAppRegistrationService
{
    private readonly IAppRegistry _appRegistry;
    private readonly IAppConfigStore _appConfigStore;
    private readonly ContextMemoryOptions _options;

    public AppRegistrationService(
        IAppRegistry appRegistry,
        IAppConfigStore appConfigStore,
        IOptions<ContextMemoryOptions> options)
    {
        _appRegistry = appRegistry;
        _appConfigStore = appConfigStore;
        _options = options.Value;
    }

    public Task<RegisterAppResponse> RegisterAsync(
        RegisterAppRequest request,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (string.IsNullOrWhiteSpace(request.AppName) || string.IsNullOrWhiteSpace(request.Domain))
            throw new ArgumentException("appName and domain are required.");

        if (!IdentifierValidator.IsValid(request.Domain))
            throw new ArgumentException("Invalid domain format.");

        var suffix = RandomNumberGenerator.GetHexString(6, lowercase: true);
        var appId = $"{request.Domain}-prod-{suffix}";
        var apiKey = ApiKeyGenerator.CreateLiveKey();

        var wikiPath = !string.IsNullOrWhiteSpace(request.WikiPath)
            ? Path.GetFullPath(request.WikiPath, _options.ContentRootPath)
            : Path.GetFullPath(Path.Combine(_options.WikiPath, appId), _options.ContentRootPath);

        Directory.CreateDirectory(wikiPath);

        var profile = new AppProfile
        {
            AppId = appId,
            ApiKey = apiKey,
            WikiPath = wikiPath,
            DefaultLanguage = request.DefaultLanguage,
            MaxHistoryMessages = _options.MaxHistoryMessages,
            WikiChunksTopK = _options.WikiChunksTopK,
            SimilarityThreshold = _options.SimilarityThreshold
        };

        var record = new RegisteredAppRecord
        {
            AppId = appId,
            ApiKey = apiKey,
            AppName = request.AppName,
            Domain = request.Domain,
            WikiPath = wikiPath,
            RegisteredAt = DateTimeOffset.UtcNow
        };

        if (!_appRegistry.Register(profile, record))
            throw new InvalidOperationException($"App '{appId}' already exists.");

        var seed = new AppRuntimeConfig
        {
            AppId = appId,
            BasePersona = GetDomainPersona(request.Domain, request.PromptPersona),
            BusinessRules = GetDomainBusinessRules(request.Domain),
            FormatRules = GetDomainFormatRules(),
            DefaultLanguage = request.DefaultLanguage,
            LlmModel = request.LlmModel,
            LlmBackend = request.LlmBackend,
            MaxHistoryMessages = _options.MaxHistoryMessages,
            WikiChunksTopK = _options.WikiChunksTopK,
            SimilarityThreshold = _options.SimilarityThreshold
        };

        _appConfigStore.EnsureProfileExists(appId, seed);

        return Task.FromResult(new RegisterAppResponse
        {
            AppId = appId,
            ApiKey = apiKey,
            WikiUploadEndpoint = $"/apps/{appId}/wiki",
            Status = "ready"
        });
    }

    private static string GetDomainPersona(string domain, string promptPersona)
    {
        if (!string.IsNullOrWhiteSpace(promptPersona))
            return promptPersona;

        return domain.ToLowerInvariant() switch
        {
            "kyc" => "És um especialista KYC/AML com experiência em conformidade regulatória.",
            "helpdesk" => "És um agente de suporte técnico experiente e empático.",
            "erp" => "És um consultor funcional de ERP com conhecimento profundo de processos empresariais.",
            _ => "És um assistente especializado no domínio da aplicação."
        };
    }

    private static string GetDomainBusinessRules(string domain) =>
        domain.ToLowerInvariant() switch
        {
            "kyc" => """
                     - Nunca fornecer aconselhamento jurídico vinculativo.
                     - Citar sempre a base regulatória quando aplicável.
                     """,
            "helpdesk" => "- Prioriza soluções práticas e passos reproduzíveis.",
            "erp" => "- Referencia módulos e fluxos standard do ERP quando relevante.",
            _ => string.Empty
        };

    private static string GetDomainFormatRules() =>
        "- Usa markdown quando apropriado.\n- Sê claro e estruturado.";
}
