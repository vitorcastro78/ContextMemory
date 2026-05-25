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
    public DbSet<CompanyEntity> Companies => Set<CompanyEntity>();
    public DbSet<CompanyProcessEntity> CompanyProcesses => Set<CompanyProcessEntity>();
    public DbSet<CompanyKnowledgeSourceEntity> CompanyKnowledgeSources => Set<CompanyKnowledgeSourceEntity>();
    public DbSet<CompanyAppLinkEntity> CompanyAppLinks => Set<CompanyAppLinkEntity>();

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

        modelBuilder.Entity<CompanyEntity>(e =>
        {
            e.ToTable("companies");
            e.HasKey(x => x.CompanyId);
            e.Property(x => x.CompanyId).HasMaxLength(64);
            e.Property(x => x.SkillsCacheJson).HasColumnType("jsonb");
        });

        modelBuilder.Entity<CompanyProcessEntity>(e =>
        {
            e.ToTable("company_processes");
            e.HasKey(x => new { x.CompanyId, x.ProcessId });
            e.Property(x => x.CompanyId).HasMaxLength(64);
            e.Property(x => x.ProcessId).HasMaxLength(64);
            e.Property(x => x.TriggersJson).HasColumnType("jsonb");
            e.Property(x => x.StepsJson).HasColumnType("jsonb");
            e.Property(x => x.GuardrailsJson).HasColumnType("jsonb");
        });

        modelBuilder.Entity<CompanyKnowledgeSourceEntity>(e =>
        {
            e.ToTable("company_knowledge_sources");
            e.HasKey(x => new { x.CompanyId, x.SourceId });
            e.Property(x => x.CompanyId).HasMaxLength(64);
            e.Property(x => x.SourceId).HasMaxLength(64);
            e.Property(x => x.SettingsJson).HasColumnType("jsonb");
        });

        modelBuilder.Entity<CompanyAppLinkEntity>(e =>
        {
            e.ToTable("company_app_links");
            e.HasKey(x => new { x.CompanyId, x.AppId });
            e.Property(x => x.CompanyId).HasMaxLength(64);
            e.Property(x => x.AppId).HasMaxLength(64);
            e.HasIndex(x => x.AppId).IsUnique();
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

public sealed class CompanyEntity
{
    public string CompanyId { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string? WebhookSecret { get; set; }
    public string? SkillsCacheJson { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}

public sealed class CompanyProcessEntity
{
    public string CompanyId { get; set; } = string.Empty;
    public string ProcessId { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string Summary { get; set; } = string.Empty;
    public string Category { get; set; } = nameof(ProcessCategory.General);
    public string TriggersJson { get; set; } = "[]";
    public string StepsJson { get; set; } = "[]";
    public string GuardrailsJson { get; set; } = "[]";
    public string? SourceRef { get; set; }
    public bool IsCritical { get; set; }
    public string PublishStatus { get; set; } = nameof(ProcessPublishStatus.Published);
    public DateTimeOffset UpdatedAt { get; set; }
}

public sealed class CompanyKnowledgeSourceEntity
{
    public string CompanyId { get; set; } = string.Empty;
    public string SourceId { get; set; } = string.Empty;
    public string Type { get; set; } = nameof(KnowledgeSourceType.MarkdownWiki);
    public string DisplayName { get; set; } = string.Empty;
    public string SettingsJson { get; set; } = "{}";
    public bool Enabled { get; set; } = true;
    public DateTimeOffset? LastSyncedAt { get; set; }
}

public sealed class CompanyAppLinkEntity
{
    public string CompanyId { get; set; } = string.Empty;
    public string AppId { get; set; } = string.Empty;
}
