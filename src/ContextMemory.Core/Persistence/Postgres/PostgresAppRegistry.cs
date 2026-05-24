using System.Collections.Concurrent;
using System.Text.Json;
using ContextMemory.Core.Configuration;
using ContextMemory.Core.Contracts;
using ContextMemory.Core.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace ContextMemory.Core.Persistence.Postgres;

public sealed class PostgresAppRegistry : IAppRegistry
{
    private readonly ConcurrentDictionary<string, AppProfile> _apps = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, RegisteredAppRecord> _registrations = new(StringComparer.Ordinal);
    private readonly HashSet<string> _seededAppIds = new(StringComparer.Ordinal);
    private readonly ContextMemoryOptions _config;
    private readonly IDbContextFactory<ContextMemoryDbContext> _dbFactory;
    private readonly object _loadLock = new();
    private volatile bool _registrationsLoaded;

    public PostgresAppRegistry(
        IOptions<ContextMemoryOptions> options,
        IDbContextFactory<ContextMemoryDbContext> dbFactory)
    {
        _config = options.Value;
        _dbFactory = dbFactory;

        foreach (var (appId, entry) in _config.Apps)
        {
            _apps[appId] = AppRegistryHelper.CreateProfile(appId, entry.ApiKey, entry, _config);
            _seededAppIds.Add(appId);
        }
    }

    private void EnsureRegisteredAppsLoaded()
    {
        if (_registrationsLoaded)
            return;

        lock (_loadLock)
        {
            if (_registrationsLoaded)
                return;

            LoadRegisteredApps();
            _registrationsLoaded = true;
        }
    }

    public bool TryGetApp(string appId, out AppProfile? profile)
    {
        EnsureRegisteredAppsLoaded();
        return _apps.TryGetValue(appId, out profile);
    }

    public bool ValidateApiKey(string appId, string apiKey)
    {
        EnsureRegisteredAppsLoaded();
        return _apps.TryGetValue(appId, out var profile)
            && string.Equals(profile.ApiKey, apiKey, StringComparison.Ordinal);
    }

    public IReadOnlyCollection<AppProfile> GetAllApps()
    {
        EnsureRegisteredAppsLoaded();
        return _apps.Values.ToList();
    }

    public bool TryGetRegistration(string appId, out RegisteredAppRecord? record)
    {
        EnsureRegisteredAppsLoaded();
        return _registrations.TryGetValue(appId, out record);
    }

    public string GetAppSource(string appId)
    {
        EnsureRegisteredAppsLoaded();
        return _registrations.ContainsKey(appId) ? "registered" : _seededAppIds.Contains(appId) ? "seed" : "unknown";
    }

    public bool Register(AppProfile profile, RegisteredAppRecord record)
    {
        if (!_apps.TryAdd(profile.AppId, profile))
            return false;

        _registrations[profile.AppId] = record;

        using var db = _dbFactory.CreateDbContext();
        db.RegisteredApps.Add(new RegisteredAppEntity
        {
            AppId = record.AppId,
            ApiKey = record.ApiKey,
            AppName = record.AppName,
            Domain = record.Domain,
            WikiPath = record.WikiPath,
            RegisteredAt = record.RegisteredAt
        });
        db.SaveChanges();
        return true;
    }

    private void LoadRegisteredApps()
    {
        using var db = _dbFactory.CreateDbContext();
        foreach (var row in db.RegisteredApps.AsNoTracking())
        {
            var record = new RegisteredAppRecord
            {
                AppId = row.AppId,
                ApiKey = row.ApiKey,
                AppName = row.AppName,
                Domain = row.Domain,
                WikiPath = row.WikiPath,
                RegisteredAt = row.RegisteredAt
            };

            var profile = new AppProfile
            {
                AppId = record.AppId,
                ApiKey = record.ApiKey,
                WikiPath = AppRegistryHelper.ResolveWikiPath(record.WikiPath, record.AppId, _config),
                DefaultLanguage = "pt-PT"
            };

            _apps[record.AppId] = profile;
            _registrations[record.AppId] = record;
        }
    }
}
