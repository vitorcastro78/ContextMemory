using ContextMemory.Core.Configuration;
using ContextMemory.Core.Models;
using Microsoft.EntityFrameworkCore;

namespace ContextMemory.Core.Persistence.Postgres;

public sealed class ContextMemoryDbContext : DbContext
{
    public ContextMemoryDbContext(DbContextOptions<ContextMemoryDbContext> options)
        : base(options)
    {
    }

    public DbSet<RegisteredAppEntity> RegisteredApps => Set<RegisteredAppEntity>();
    public DbSet<AppProfileEntity> AppProfiles => Set<AppProfileEntity>();
    public DbSet<ConversationHistoryEntity> ConversationHistories => Set<ConversationHistoryEntity>();
    public DbSet<UserProfileEntity> UserProfiles => Set<UserProfileEntity>();
    public DbSet<SemanticFactEntity> SemanticFacts => Set<SemanticFactEntity>();
    public DbSet<FeedbackEntity> Feedback => Set<FeedbackEntity>();
    public DbSet<AuditLogEntity> AuditLogs => Set<AuditLogEntity>();
    public DbSet<KnowledgeLoopEntryEntity> KnowledgeLoopEntries => Set<KnowledgeLoopEntryEntity>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<RegisteredAppEntity>(e =>
        {
            e.ToTable("registered_apps");
            e.HasKey(x => x.AppId);
            e.Property(x => x.AppId).HasMaxLength(64);
        });

        modelBuilder.Entity<AppProfileEntity>(e =>
        {
            e.ToTable("app_profiles");
            e.HasKey(x => x.AppId);
            e.Property(x => x.AppId).HasMaxLength(64);
            e.Property(x => x.ConfigJson).HasColumnType("jsonb");
            e.Property(x => x.ContentRulesJson).HasColumnType("jsonb");
        });

        modelBuilder.Entity<ConversationHistoryEntity>(e =>
        {
            e.ToTable("conversation_history");
            e.HasKey(x => new { x.AppId, x.UserId });
            e.Property(x => x.AppId).HasMaxLength(64);
            e.Property(x => x.UserId).HasMaxLength(64);
            e.Property(x => x.MessagesJson).HasColumnType("jsonb");
        });

        modelBuilder.Entity<UserProfileEntity>(e =>
        {
            e.ToTable("user_profiles");
            e.HasKey(x => new { x.AppId, x.UserId });
            e.Property(x => x.AppId).HasMaxLength(64);
            e.Property(x => x.UserId).HasMaxLength(64);
            e.Property(x => x.FactsJson).HasColumnType("jsonb");
        });

        modelBuilder.Entity<SemanticFactEntity>(e =>
        {
            e.ToTable("semantic_facts");
            e.HasKey(x => x.Id);
            e.Property(x => x.AppId).HasMaxLength(64);
            e.Property(x => x.UserId).HasMaxLength(64);
            e.HasIndex(x => new { x.AppId, x.UserId });
        });

        modelBuilder.Entity<FeedbackEntity>(e =>
        {
            e.ToTable("feedback");
            e.HasKey(x => x.Id);
            e.Property(x => x.AppId).HasMaxLength(64);
            e.HasIndex(x => new { x.AppId, x.Timestamp });
        });

        modelBuilder.Entity<AuditLogEntity>(e =>
        {
            e.ToTable("audit_log");
            e.HasKey(x => x.Id);
            e.Property(x => x.AppId).HasMaxLength(64);
            e.HasIndex(x => new { x.AppId, x.Timestamp });
        });

        modelBuilder.Entity<KnowledgeLoopEntryEntity>(e =>
        {
            e.ToTable("knowledge_loop_entries");
            e.HasKey(x => x.SessionId);
            e.Property(x => x.SessionId).HasMaxLength(64);
            e.Property(x => x.AppId).HasMaxLength(64);
            e.Property(x => x.UserId).HasMaxLength(64);
            e.Property(x => x.MessagesJson).HasColumnType("jsonb");
            e.Property(x => x.EvaluationJson).HasColumnType("jsonb");
            e.HasIndex(x => new { x.AppId, x.Status });
            e.HasIndex(x => x.CreatedAt);
        });

    }
}

public sealed class RegisteredAppEntity
{
    public string AppId { get; set; } = string.Empty;
    public string ApiKey { get; set; } = string.Empty;
    public string AppName { get; set; } = string.Empty;
    public string Domain { get; set; } = string.Empty;
    public string WikiPath { get; set; } = string.Empty;
    public DateTimeOffset RegisteredAt { get; set; }
}

public sealed class AppProfileEntity
{
    public string AppId { get; set; } = string.Empty;
    public string Persona { get; set; } = string.Empty;
    public string BusinessRules { get; set; } = string.Empty;
    public string FormatRules { get; set; } = string.Empty;
    public string ConfigJson { get; set; } = "{}";
    public string ContentRulesJson { get; set; } = "{}";
    public DateTimeOffset UpdatedAt { get; set; }
}

public sealed class ConversationHistoryEntity
{
    public string AppId { get; set; } = string.Empty;
    public string UserId { get; set; } = string.Empty;
    public string? SessionSummary { get; set; }
    public string MessagesJson { get; set; } = "[]";
}

public sealed class UserProfileEntity
{
    public string AppId { get; set; } = string.Empty;
    public string UserId { get; set; } = string.Empty;
    public string? SessionContext { get; set; }
    public string FactsJson { get; set; } = "[]";
}

public sealed class SemanticFactEntity
{
    public long Id { get; set; }
    public string AppId { get; set; } = string.Empty;
    public string UserId { get; set; } = string.Empty;
    public string Text { get; set; } = string.Empty;
    public float[] Vector { get; set; } = [];
    public DateTimeOffset LearnedAt { get; set; }
}

public sealed class FeedbackEntity
{
    public long Id { get; set; }
    public string MessageId { get; set; } = string.Empty;
    public string AppId { get; set; } = string.Empty;
    public string UserId { get; set; } = string.Empty;
    public int Score { get; set; }
    public string? Reason { get; set; }
    public DateTimeOffset Timestamp { get; set; }
    public bool IsImplicit { get; set; }
}

public sealed class AuditLogEntity
{
    public long Id { get; set; }
    public string AppId { get; set; } = string.Empty;
    public string UserId { get; set; } = string.Empty;
    public string Phase { get; set; } = string.Empty;
    public string Reason { get; set; } = string.Empty;
    public DateTimeOffset Timestamp { get; set; }
}
