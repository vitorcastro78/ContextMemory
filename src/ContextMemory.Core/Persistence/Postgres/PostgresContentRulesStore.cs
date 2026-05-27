using System.Collections.Concurrent;
using System.Text.Json;
using ContextMemory.Core.Contracts;
using ContextMemory.Core.Models;
using ContextMemory.Core.Safety;
using Microsoft.EntityFrameworkCore;

namespace ContextMemory.Core.Persistence.Postgres;

public sealed class PostgresContentRulesStore : IContentRulesStore
{
    private readonly IDbContextFactory<ContextMemoryDbContext> _dbFactory;
    private readonly ConcurrentDictionary<string, ContentRules> _cache = new(StringComparer.Ordinal);

    public PostgresContentRulesStore(IDbContextFactory<ContextMemoryDbContext> dbFactory) =>
        _dbFactory = dbFactory;

    public ContentRules GetRules(string appId) =>
        _cache.GetOrAdd(appId, LoadRules);

    public void Reload(string appId) => _cache[appId] = LoadRules(appId);

    public void EnsureDefaultRules(string appId)
    {
        using var db = _dbFactory.CreateDbContext();
        var row = db.AppProfiles.FirstOrDefault(x => x.AppId == appId);
        if (row is not null && row.ContentRulesJson is not null && row.ContentRulesJson != "{}" && row.ContentRulesJson != "[]")
            return;

        var rules = ContentRulesDefaults.ForApp(appId);
        var json = JsonSerializer.Serialize(rules, PostgresJson.CamelCase);

        if (row is null)
        {
            db.AppProfiles.Add(new AppProfileEntity
            {
                AppId = appId,
                ConfigJson = "{}",
                ContentRulesJson = json,
                UpdatedAt = DateTimeOffset.UtcNow
            });
        }
        else
        {
            row.ContentRulesJson = json;
            row.UpdatedAt = DateTimeOffset.UtcNow;
        }

        db.SaveChanges();
        _cache[appId] = rules;
    }

    private ContentRules LoadRules(string appId)
    {
        using var db = _dbFactory.CreateDbContext();
        var row = db.AppProfiles.AsNoTracking().FirstOrDefault(x => x.AppId == appId);
        if (row is null || string.IsNullOrWhiteSpace(row.ContentRulesJson))
            return new ContentRules();

        return JsonSerializer.Deserialize<ContentRules>(row.ContentRulesJson, PostgresJson.CamelCase)
               ?? new ContentRules();
    }
}
