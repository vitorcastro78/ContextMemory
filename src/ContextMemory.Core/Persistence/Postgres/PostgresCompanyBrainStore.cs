using System.Collections.Concurrent;
using System.Text.Json;
using ContextMemory.Core.Contracts;
using ContextMemory.Core.Models;
using ContextMemory.Core.Security;
using Microsoft.EntityFrameworkCore;

namespace ContextMemory.Core.Persistence.Postgres;

public sealed class PostgresCompanyBrainStore : ICompanyBrainStore
{
    private readonly IDbContextFactory<ContextMemoryDbContext> _dbFactory;
    private readonly object _loadLock = new();
    private volatile bool _loaded;

    private readonly ConcurrentDictionary<string, CompanyProfile> _companies = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, ConcurrentDictionary<string, CompanyProcess>> _processes = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, ConcurrentDictionary<string, KnowledgeSource>> _sources = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, string> _appToCompany = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, HashSet<string>> _companyApps = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, string> _webhookSecrets = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, CompanySkillsFile?> _skillsCache = new(StringComparer.Ordinal);

    public PostgresCompanyBrainStore(IDbContextFactory<ContextMemoryDbContext> dbFactory) =>
        _dbFactory = dbFactory;

    public bool TryGetCompany(string companyId, out CompanyProfile? company)
    {
        EnsureLoaded();
        return _companies.TryGetValue(companyId, out company);
    }

    public IReadOnlyList<CompanyProfile> ListCompanies()
    {
        EnsureLoaded();
        return _companies.Values.OrderBy(c => c.Name).ToList();
    }

    public bool RegisterCompany(CompanyProfile company)
    {
        EnsureLoaded();
        if (_companies.ContainsKey(company.CompanyId))
            return false;

        using var db = _dbFactory.CreateDbContext();
        db.Companies.Add(new CompanyEntity
        {
            CompanyId = company.CompanyId,
            Name = company.Name,
            Description = company.Description,
            WebhookSecret = CompanyWebhookAuth.CreateWebhookSecret(),
            CreatedAt = company.CreatedAt,
            UpdatedAt = company.UpdatedAt
        });
        db.SaveChanges();

        var secret = db.Companies.AsNoTracking().First(c => c.CompanyId == company.CompanyId).WebhookSecret
            ?? CompanyWebhookAuth.CreateWebhookSecret();

        _companies[company.CompanyId] = company;
        _webhookSecrets[company.CompanyId] = secret;
        _processes[company.CompanyId] = new ConcurrentDictionary<string, CompanyProcess>(StringComparer.Ordinal);
        _sources[company.CompanyId] = new ConcurrentDictionary<string, KnowledgeSource>(StringComparer.Ordinal);
        _companyApps[company.CompanyId] = new HashSet<string>(StringComparer.Ordinal);
        return true;
    }

    public bool UpsertProcess(CompanyProcess process)
    {
        EnsureLoaded();
        if (!_companies.ContainsKey(process.CompanyId))
            return false;

        using var db = _dbFactory.CreateDbContext();
        var row = db.CompanyProcesses.Find(process.CompanyId, process.ProcessId);
        if (row is null)
        {
            db.CompanyProcesses.Add(MapProcessEntity(process));
        }
        else
        {
            UpdateProcessEntity(row, process);
        }
        db.SaveChanges();

        var bucket = _processes.GetOrAdd(process.CompanyId, _ => new ConcurrentDictionary<string, CompanyProcess>(StringComparer.Ordinal));
        bucket[process.ProcessId] = process;
        return true;
    }

    public IReadOnlyList<CompanyProcess> ListProcesses(string companyId)
    {
        EnsureLoaded();
        return _processes.TryGetValue(companyId, out var bucket)
            ? bucket.Values.OrderBy(p => p.Title).ToList()
            : [];
    }

    public bool TryGetProcess(string companyId, string processId, out CompanyProcess? process)
    {
        EnsureLoaded();
        process = null;
        return _processes.TryGetValue(companyId, out var bucket)
            && bucket.TryGetValue(processId, out process);
    }

    public bool UpsertKnowledgeSource(KnowledgeSource source)
    {
        EnsureLoaded();
        if (!_companies.ContainsKey(source.CompanyId))
            return false;

        using var db = _dbFactory.CreateDbContext();
        var row = db.CompanyKnowledgeSources.Find(source.CompanyId, source.SourceId);
        if (row is null)
            db.CompanyKnowledgeSources.Add(MapSourceEntity(source));
        else
            UpdateSourceEntity(row, source);
        db.SaveChanges();

        var bucket = _sources.GetOrAdd(source.CompanyId, _ => new ConcurrentDictionary<string, KnowledgeSource>(StringComparer.Ordinal));
        bucket[source.SourceId] = source;
        return true;
    }

    public IReadOnlyList<KnowledgeSource> ListKnowledgeSources(string companyId)
    {
        EnsureLoaded();
        return _sources.TryGetValue(companyId, out var bucket)
            ? bucket.Values.OrderBy(s => s.DisplayName).ToList()
            : [];
    }

    public bool LinkApp(string companyId, string appId)
    {
        EnsureLoaded();
        if (!_companies.ContainsKey(companyId))
            return false;

        if (_appToCompany.TryGetValue(appId, out var existing) && !string.Equals(existing, companyId, StringComparison.Ordinal))
            return false;

        using var db = _dbFactory.CreateDbContext();
        if (!db.CompanyAppLinks.Any(x => x.CompanyId == companyId && x.AppId == appId))
            db.CompanyAppLinks.Add(new CompanyAppLinkEntity { CompanyId = companyId, AppId = appId });
        db.SaveChanges();

        _appToCompany[appId] = companyId;
        _companyApps.GetOrAdd(companyId, _ => new HashSet<string>(StringComparer.Ordinal)).Add(appId);
        return true;
    }

    public bool UnlinkApp(string companyId, string appId)
    {
        EnsureLoaded();
        if (!_appToCompany.TryGetValue(appId, out var linked) || !string.Equals(linked, companyId, StringComparison.Ordinal))
            return false;

        using var db = _dbFactory.CreateDbContext();
        var row = db.CompanyAppLinks.Find(companyId, appId);
        if (row is not null)
            db.CompanyAppLinks.Remove(row);
        db.SaveChanges();

        _appToCompany.TryRemove(appId, out _);
        if (_companyApps.TryGetValue(companyId, out var apps))
            apps.Remove(appId);
        return true;
    }

    public bool TryGetCompanyForApp(string appId, out string? companyId)
    {
        EnsureLoaded();
        return _appToCompany.TryGetValue(appId, out companyId);
    }

    public IReadOnlyList<string> ListLinkedApps(string companyId)
    {
        EnsureLoaded();
        return _companyApps.TryGetValue(companyId, out var apps)
            ? apps.OrderBy(a => a).ToList()
            : [];
    }

    public void SaveSkillsCache(string companyId, CompanySkillsFile skillsFile)
    {
        EnsureLoaded();
        _skillsCache[companyId] = skillsFile;

        using var db = _dbFactory.CreateDbContext();
        var row = db.Companies.Find(companyId);
        if (row is null)
            return;
        row.SkillsCacheJson = JsonSerializer.Serialize(skillsFile, PostgresJson.CamelCase);
        row.UpdatedAt = DateTimeOffset.UtcNow;
        db.SaveChanges();
    }

    public bool TryGetSkillsCache(string companyId, out CompanySkillsFile? skillsFile)
    {
        EnsureLoaded();
        if (_skillsCache.TryGetValue(companyId, out skillsFile) && skillsFile is not null)
            return true;

        skillsFile = null;
        return false;
    }

    public bool TryGetWebhookSecret(string companyId, out string? secret)
    {
        EnsureLoaded();
        if (_webhookSecrets.TryGetValue(companyId, out var cached) && !string.IsNullOrWhiteSpace(cached))
        {
            secret = cached;
            return true;
        }

        secret = null;
        return false;
    }

    public string SetWebhookSecret(string companyId, string? secret = null)
    {
        EnsureLoaded();
        if (!_companies.ContainsKey(companyId))
            throw new InvalidOperationException($"Company '{companyId}' not found.");

        secret ??= CompanyWebhookAuth.CreateWebhookSecret();
        using var db = _dbFactory.CreateDbContext();
        var row = db.Companies.Find(companyId) ?? throw new InvalidOperationException($"Company '{companyId}' not found.");
        row.WebhookSecret = secret;
        row.UpdatedAt = DateTimeOffset.UtcNow;
        db.SaveChanges();
        _webhookSecrets[companyId] = secret;
        return secret;
    }

    private void EnsureLoaded()
    {
        if (_loaded)
            return;

        lock (_loadLock)
        {
            if (_loaded)
                return;
            LoadAll();
            _loaded = true;
        }
    }

    private void LoadAll()
    {
        using var db = _dbFactory.CreateDbContext();
        foreach (var row in db.Companies.AsNoTracking())
        {
            _companies[row.CompanyId] = new CompanyProfile
            {
                CompanyId = row.CompanyId,
                Name = row.Name,
                Description = row.Description,
                CreatedAt = row.CreatedAt,
                UpdatedAt = row.UpdatedAt
            };
            if (!string.IsNullOrWhiteSpace(row.WebhookSecret))
                _webhookSecrets[row.CompanyId] = row.WebhookSecret;
            if (!string.IsNullOrWhiteSpace(row.SkillsCacheJson))
            {
                var cache = JsonSerializer.Deserialize<CompanySkillsFile>(row.SkillsCacheJson, PostgresJson.CamelCase);
                if (cache is not null)
                    _skillsCache[row.CompanyId] = cache;
            }
        }

        foreach (var row in db.CompanyProcesses.AsNoTracking())
        {
            var bucket = _processes.GetOrAdd(row.CompanyId, _ => new ConcurrentDictionary<string, CompanyProcess>(StringComparer.Ordinal));
            bucket[row.ProcessId] = MapProcessModel(row);
        }

        foreach (var row in db.CompanyKnowledgeSources.AsNoTracking())
        {
            var bucket = _sources.GetOrAdd(row.CompanyId, _ => new ConcurrentDictionary<string, KnowledgeSource>(StringComparer.Ordinal));
            bucket[row.SourceId] = MapSourceModel(row);
        }

        foreach (var row in db.CompanyAppLinks.AsNoTracking())
        {
            _appToCompany[row.AppId] = row.CompanyId;
            _companyApps.GetOrAdd(row.CompanyId, _ => new HashSet<string>(StringComparer.Ordinal)).Add(row.AppId);
        }
    }

    private static CompanyProcessEntity MapProcessEntity(CompanyProcess process) => new()
    {
        CompanyId = process.CompanyId,
        ProcessId = process.ProcessId,
        Title = process.Title,
        Summary = process.Summary,
        Category = process.Category.ToString(),
        TriggersJson = JsonSerializer.Serialize(process.Triggers, PostgresJson.CamelCase),
        StepsJson = JsonSerializer.Serialize(process.Steps, PostgresJson.CamelCase),
        GuardrailsJson = JsonSerializer.Serialize(process.Guardrails, PostgresJson.CamelCase),
        SourceRef = process.SourceRef,
        IsCritical = process.IsCritical,
        PublishStatus = process.PublishStatus.ToString(),
        UpdatedAt = process.UpdatedAt
    };

    private static void UpdateProcessEntity(CompanyProcessEntity row, CompanyProcess process)
    {
        row.Title = process.Title;
        row.Summary = process.Summary;
        row.Category = process.Category.ToString();
        row.TriggersJson = JsonSerializer.Serialize(process.Triggers, PostgresJson.CamelCase);
        row.StepsJson = JsonSerializer.Serialize(process.Steps, PostgresJson.CamelCase);
        row.GuardrailsJson = JsonSerializer.Serialize(process.Guardrails, PostgresJson.CamelCase);
        row.SourceRef = process.SourceRef;
        row.IsCritical = process.IsCritical;
        row.PublishStatus = process.PublishStatus.ToString();
        row.UpdatedAt = process.UpdatedAt;
    }

    private static CompanyProcess MapProcessModel(CompanyProcessEntity row) => new()
    {
        CompanyId = row.CompanyId,
        ProcessId = row.ProcessId,
        Title = row.Title,
        Summary = row.Summary,
        Category = Enum.TryParse<ProcessCategory>(row.Category, out var category) ? category : ProcessCategory.General,
        Triggers = JsonSerializer.Deserialize<List<string>>(row.TriggersJson, PostgresJson.CamelCase) ?? [],
        Steps = JsonSerializer.Deserialize<List<ProcessStep>>(row.StepsJson, PostgresJson.CamelCase) ?? [],
        Guardrails = JsonSerializer.Deserialize<List<string>>(row.GuardrailsJson, PostgresJson.CamelCase) ?? [],
        SourceRef = row.SourceRef,
        IsCritical = row.IsCritical,
        PublishStatus = Enum.TryParse<ProcessPublishStatus>(row.PublishStatus, out var status)
            ? status
            : ProcessPublishStatus.Published,
        UpdatedAt = row.UpdatedAt
    };

    private static CompanyKnowledgeSourceEntity MapSourceEntity(KnowledgeSource source) => new()
    {
        CompanyId = source.CompanyId,
        SourceId = source.SourceId,
        Type = source.Type.ToString(),
        DisplayName = source.DisplayName,
        SettingsJson = JsonSerializer.Serialize(source.Settings, PostgresJson.CamelCase),
        Enabled = source.Enabled,
        LastSyncedAt = source.LastSyncedAt
    };

    private static void UpdateSourceEntity(CompanyKnowledgeSourceEntity row, KnowledgeSource source)
    {
        row.Type = source.Type.ToString();
        row.DisplayName = source.DisplayName;
        row.SettingsJson = JsonSerializer.Serialize(source.Settings, PostgresJson.CamelCase);
        row.Enabled = source.Enabled;
        row.LastSyncedAt = source.LastSyncedAt;
    }

    private static KnowledgeSource MapSourceModel(CompanyKnowledgeSourceEntity row) => new()
    {
        CompanyId = row.CompanyId,
        SourceId = row.SourceId,
        Type = Enum.TryParse<KnowledgeSourceType>(row.Type, out var type) ? type : KnowledgeSourceType.MarkdownWiki,
        DisplayName = row.DisplayName,
        Settings = JsonSerializer.Deserialize<Dictionary<string, string>>(row.SettingsJson, PostgresJson.CamelCase)
            ?? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase),
        Enabled = row.Enabled,
        LastSyncedAt = row.LastSyncedAt
    };
}
