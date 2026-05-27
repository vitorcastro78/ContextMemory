using ContextMemory.Adapters;
using ContextMemory.Api.Hosting;
using ContextMemory.Core.Configuration;
using ContextMemory.Core.Contracts;
using ContextMemory.Core.Engine;
using ContextMemory.Core.Feedback;
using ContextMemory.Core.Knowledge;
using ContextMemory.Core.Memory;
using ContextMemory.Core.Observability;
using ContextMemory.Core.Persistence;
using ContextMemory.Core.Profile;
using ContextMemory.Core.RateLimiting;
using ContextMemory.Core.Safety;
using ContextMemory.Embeddings;
using ContextMemory.Embeddings.Configuration;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace ContextMemory.Api.Extensions;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddContextMemory(this IServiceCollection services, IConfiguration configuration)
    {
        var usePostgres = PersistenceProviders.IsPostgres(
            configuration.GetSection(ContextMemoryOptions.SectionName)["PersistenceProvider"]);

        services.Configure<OllamaAdapterOptions>(configuration.GetSection(ContextMemoryOptions.SectionName));
        services.Configure<LmStudioAdapterOptions>(configuration.GetSection(ContextMemoryOptions.SectionName));

        if (usePostgres)
        {
            services.AddContextMemoryPersistence(configuration);
        }
        else
        {
            services.AddSingleton<IAppRegistry, AppRegistry>();
            services.AddSingleton<IAppConfigStore, AppConfigStore>();
            services.AddSingleton<IUserProfileStore, UserProfileStore>();
            services.AddSingleton<ISemanticMemory, SemanticMemory>();
            services.AddSingleton<IConversationMemory, ConversationMemory>();
            services.AddSingleton<IFeedbackStore, FeedbackStore>();
            services.AddSingleton<IContentRulesStore, ContentRulesStore>();
            services.AddSingleton<IAuditLog, AuditLog>();
            services.AddSingleton<IMemoryAdminService, MemoryAdminService>();
        }

        services.AddSingleton<IAppRegistrationService, AppRegistrationService>();
        services.AddSingleton<IIntentDetector, IntentDetector>();
        services.AddSingleton<IProfileLearner, ProfileLearner>();
        services.AddSingleton<ISessionSummarizer, SessionSummarizer>();
        services.AddSingleton<WikiLoader>();
        services.AddSingleton<VectorStore>();
        services.AddSingleton<SimilaritySearch>();
        services.AddSingleton<IWikiIndexService, WikiIndexService>();
        services.AddSingleton<IEmbeddingEngine, OnnxEmbeddingEngine>();
        services.AddSingleton<PromptComposer>();
        services.AddSingleton<IContextEngine, ContextEngine>();

        services.AddSingleton<IImplicitFeedbackDetector, ImplicitFeedbackDetector>();
        services.AddSingleton<IFeedbackProcessor, FeedbackProcessor>();
        services.AddSingleton<IMessageIdTracker, MessageIdTracker>();

        services.AddSingleton<IContentFilter, ContentFilter>();
        services.AddSingleton<ITelemetryCollector, TelemetryCollector>();
        services.AddSingleton<IRateLimitService, RateLimitService>();

        services.AddHttpClient<OllamaAdapter>((sp, client) =>
        {
            var timeout = sp.GetRequiredService<Microsoft.Extensions.Options.IOptions<ContextMemoryOptions>>().Value
                .OllamaRequestTimeoutSeconds;
            client.Timeout = TimeSpan.FromSeconds(Math.Max(30, timeout));
        });
        services.AddHttpClient<LmStudioAdapter>();
        services.AddHttpClient<OpenAiAdapter>();
        services.AddSingleton<ILlmAdapterResolver, LlmAdapterResolver>();

        services.AddHostedService<AppConfigBootstrapHostedService>();
        if (!usePostgres)
            services.AddHostedService<AppConfigWatcherHostedService>();
        services.AddHostedService<WikiWatcherHostedService>();

        return services;
    }
}
