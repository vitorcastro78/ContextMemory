using ContextMemory.Core.Configuration;
using ContextMemory.Core.Contracts;
using ContextMemory.Core.Feedback;
using ContextMemory.Core.KnowledgeLoop;
using ContextMemory.Core.Memory;
using ContextMemory.Core.Persistence;
using ContextMemory.Core.Persistence.Postgres;
using ContextMemory.Core.Profile;
using ContextMemory.Core.Safety;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace ContextMemory.Api.Extensions;

public static class PersistenceServiceCollectionExtensions
{
    public static IServiceCollection AddContextMemoryPersistence(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        var provider = configuration.GetSection(ContextMemoryOptions.SectionName)["PersistenceProvider"];
        if (!PersistenceProviders.IsPostgres(provider))
            return services;

        var connectionString = configuration.GetConnectionString("ContextMemory");
        if (string.IsNullOrWhiteSpace(connectionString))
            throw new InvalidOperationException(
                "ContextMemory:PersistenceProvider is Postgres but ConnectionStrings:ContextMemory is missing.");

        services.AddDbContextFactory<ContextMemoryDbContext>(options =>
            options.UseNpgsql(connectionString, npgsql =>
                npgsql.MigrationsAssembly(typeof(Program).Assembly.GetName().Name)));

        services.AddSingleton<IAppRegistry, PostgresAppRegistry>();
        services.AddSingleton<IAppConfigStore, PostgresAppConfigStore>();
        services.AddSingleton<IUserProfileStore, PostgresUserProfileStore>();
        services.AddSingleton<ISemanticMemory, PostgresSemanticMemory>();
        services.AddSingleton<IConversationMemory, PostgresConversationMemory>();
        services.AddSingleton<IFeedbackStore, PostgresFeedbackStore>();
        services.AddSingleton<IContentRulesStore, PostgresContentRulesStore>();
        services.AddSingleton<IAuditLog, PostgresAuditLog>();
        services.AddSingleton<IMemoryAdminService, PostgresMemoryAdminService>();
        services.AddSingleton<IKnowledgeLoopStore, PostgresKnowledgeLoopStore>();

        services.AddSingleton<IPostgresHealthCheck, PostgresHealthCheck>();
        services.AddHostedService<DatabaseInitializerHostedService>();

        return services;
    }
}

internal sealed class PostgresHealthCheck : IPostgresHealthCheck
{
    private readonly IDbContextFactory<ContextMemoryDbContext> _dbFactory;

    public PostgresHealthCheck(IDbContextFactory<ContextMemoryDbContext> dbFactory) =>
        _dbFactory = dbFactory;

    public async Task<bool> CanConnectAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            await using var db = await _dbFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
            return await db.Database.CanConnectAsync(cancellationToken).ConfigureAwait(false);
        }
        catch
        {
            return false;
        }
    }
}

internal sealed class DatabaseInitializerHostedService : IHostedService
{
    private readonly IDbContextFactory<ContextMemoryDbContext> _dbFactory;

    public DatabaseInitializerHostedService(IDbContextFactory<ContextMemoryDbContext> dbFactory) =>
        _dbFactory = dbFactory;

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        await db.Database.MigrateAsync(cancellationToken).ConfigureAwait(false);
        await db.Database.ExecuteSqlRawAsync("CREATE EXTENSION IF NOT EXISTS vector", cancellationToken)
            .ConfigureAwait(false);
        await db.Database.ExecuteSqlRawAsync(
            """
            CREATE TABLE IF NOT EXISTS wiki_chunks (
                id BIGSERIAL PRIMARY KEY,
                app_id VARCHAR(64) NOT NULL,
                source TEXT NOT NULL,
                header_path TEXT NOT NULL,
                content TEXT NOT NULL,
                embedding vector(384),
                is_learned BOOLEAN NOT NULL DEFAULT FALSE,
                created_at TIMESTAMPTZ NOT NULL DEFAULT NOW()
            );
            CREATE INDEX IF NOT EXISTS wiki_chunks_app_idx ON wiki_chunks (app_id);
            """,
            cancellationToken).ConfigureAwait(false);
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
