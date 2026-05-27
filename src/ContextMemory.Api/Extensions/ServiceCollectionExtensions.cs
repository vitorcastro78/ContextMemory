using ContextMemory.Adapters;
using ContextMemory.Api.Hosting;
using ContextMemory.Core.Configuration;
using ContextMemory.Core.Contracts;
using ContextMemory.Core.Billing;
using ContextMemory.Core.Engine;
using ContextMemory.Core.Feedback;
using ContextMemory.Core.Knowledge;
using ContextMemory.Core.KnowledgeLoop;
using ContextMemory.Core.Memory;
using ContextMemory.Core.Observability;
using ContextMemory.Core.Persistence;
using ContextMemory.Core.Profile;
using ContextMemory.Core.RateLimiting;
using ContextMemory.Core.Safety;
using ContextMemory.Core.Tools;
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
            services.AddSingleton<IKnowledgeLoopStore, FileKnowledgeLoopStore>();
        }

        services.AddSingleton<IAppRegistrationService, AppRegistrationService>();
        services.AddSingleton<IIntentDetector, IntentDetector>();
        services.AddSingleton<IProfileLearner, ProfileLearner>();
        services.AddSingleton<ISessionSummarizer, SessionSummarizer>();
        services.AddSingleton<WikiLoader>();
        services.AddSingleton<VectorStore>();
        services.AddSingleton<SimilaritySearch>();
        if (usePostgres)
        {
            var connectionString = configuration.GetConnectionString("ContextMemory")
                ?? throw new InvalidOperationException("ConnectionStrings:ContextMemory is required for Postgres.");
            services.AddSingleton<IPgVectorStore>(sp =>
                new PgVectorStore(connectionString, sp.GetRequiredService<ILogger<PgVectorStore>>()));
        }
        else
        {
            services.AddSingleton<IPgVectorStore, FileVectorStoreAdapter>();
        }
        services.AddSingleton<IWikiVectorSearch, WikiVectorSearchAdapter>();
        services.AddSingleton<IWikiIndexService, WikiIndexService>();
        services.AddSingleton<ConversationEvaluator>();
        services.AddSingleton<KnowledgeExtractor>();
        services.AddSingleton<KnowledgeMerger>();
        services.AddSingleton<WikiIngestionService>();
        services.AddSingleton<IKnowledgeLoop, KnowledgeLoopOrchestrator>();
        services.AddSingleton<IPlanStore, PlanStore>();
        services.AddSingleton<QuotaEnforcer>();
        services.AddSingleton<IEmbeddingEngine, OnnxEmbeddingEngine>();
        services.AddSingleton<PromptComposer>();
        services.AddSingleton<ToolCallParser>();
        services.AddSingleton<IToolRegistry, ToolRegistry>();
        services.AddSingleton<BuiltinToolsRegistrar>();
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
        services.AddHostedService<KnowledgeLoopBackgroundService>();

        return services;
    }
}
