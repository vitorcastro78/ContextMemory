using System.Collections.Concurrent;
using System.Text.Json;
using ContextMemory.Core.Configuration;
using ContextMemory.Core.Contracts;
using ContextMemory.Core.Models;
using Microsoft.Extensions.Options;

namespace ContextMemory.Core.Profile;

public sealed class AppRegistry : IAppRegistry
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true
    };

    private readonly ConcurrentDictionary<string, AppProfile> _apps = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, RegisteredAppRecord> _registrations = new(StringComparer.Ordinal);
    private readonly HashSet<string> _seededAppIds = new(StringComparer.Ordinal);
    private readonly ContextMemoryOptions _config;
    private readonly string _registeredAppsPath;

    public AppRegistry(IOptions<ContextMemoryOptions> options)
    {
        _config = options.Value;
        _registeredAppsPath = Path.Combine(
            Path.GetFullPath(_config.DataPath, _config.ContentRootPath),
            "registered-apps");
        Directory.CreateDirectory(_registeredAppsPath);

        foreach (var (appId, entry) in _config.Apps)
        {
            _apps[appId] = CreateProfile(appId, entry.ApiKey, entry);
            _seededAppIds.Add(appId);
        }

        LoadRegisteredApps();
    }

    public bool TryGetApp(string appId, out AppProfile? profile) =>
        _apps.TryGetValue(appId, out profile);

    public bool ValidateApiKey(string appId, string apiKey) =>
        _apps.TryGetValue(appId, out var profile)
        && string.Equals(profile.ApiKey, apiKey, StringComparison.Ordinal);

    public IReadOnlyCollection<AppProfile> GetAllApps() => _apps.Values.ToList();

    public bool TryGetRegistration(string appId, out RegisteredAppRecord? record) =>
        _registrations.TryGetValue(appId, out record);

    public string GetAppSource(string appId) =>
        _registrations.ContainsKey(appId) ? "registered" : _seededAppIds.Contains(appId) ? "seed" : "unknown";

    public bool Register(AppProfile profile, RegisteredAppRecord record)
    {
        if (!_apps.TryAdd(profile.AppId, profile))
            return false;

        _registrations[profile.AppId] = record;
        var path = Path.Combine(_registeredAppsPath, $"{profile.AppId}.json");
        File.WriteAllText(path, JsonSerializer.Serialize(record, JsonOptions));
        return true;
    }

    internal string RegisteredAppsDirectory => _registeredAppsPath;

    private void LoadRegisteredApps()
    {
        if (!Directory.Exists(_registeredAppsPath))
            return;

        foreach (var file in Directory.EnumerateFiles(_registeredAppsPath, "*.json"))
        {
            try
            {
                var record = JsonSerializer.Deserialize<RegisteredAppRecord>(File.ReadAllText(file), JsonOptions);
                if (record is null)
                    continue;

                var profile = new AppProfile
                {
                    AppId = record.AppId,
                    ApiKey = record.ApiKey,
                    WikiPath = ResolveWikiPath(record.WikiPath, record.AppId),
                    DefaultLanguage = "pt-PT"
                };

                _apps[record.AppId] = profile;
                _registrations[record.AppId] = record;
            }
            catch
            {
                // Skip corrupt registration files
            }
        }
    }

    private AppProfile CreateProfile(string appId, string apiKey, AppOptionsEntry entry) =>
        new()
        {
            AppId = appId,
            ApiKey = apiKey,
            SystemPrompt = entry.SystemPrompt,
            DefaultLanguage = entry.DefaultLanguage,
            WikiPath = ResolveWikiPath(entry.WikiPath, appId),
            MaxHistoryMessages = entry.MaxHistoryMessages > 0
                ? entry.MaxHistoryMessages
                : _config.MaxHistoryMessages,
            WikiChunksTopK = entry.WikiChunksTopK > 0
                ? entry.WikiChunksTopK
                : _config.WikiChunksTopK,
            SimilarityThreshold = entry.SimilarityThreshold > 0
                ? entry.SimilarityThreshold
                : _config.SimilarityThreshold
        };

    private string ResolveWikiPath(string configuredPath, string appId)
    {
        var root = _config.ContentRootPath;

        if (!string.IsNullOrWhiteSpace(configuredPath))
            return Path.GetFullPath(configuredPath, root);

        var domainCandidate = Path.GetFullPath(Path.Combine(_config.WikiPath, appId), root);
        if (Directory.Exists(domainCandidate))
            return domainCandidate;

        return Path.GetFullPath(Path.Combine(_config.WikiPath, appId), root);
    }
}
