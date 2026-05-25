using System.Collections.Concurrent;
using System.Text.Json;
using ContextMemory.Core.Configuration;
using ContextMemory.Core.Contracts;
using ContextMemory.Core.Models;
using Microsoft.Extensions.Options;

namespace ContextMemory.Core.CompanyBrain;

public sealed class CompanyBrainStore : ICompanyBrainStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true
    };

    private readonly string _companiesRoot;
    private readonly ConcurrentDictionary<string, CompanyProfile> _companies = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, ConcurrentDictionary<string, CompanyProcess>> _processes = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, ConcurrentDictionary<string, KnowledgeSource>> _sources = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, string> _appToCompany = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, HashSet<string>> _companyApps = new(StringComparer.Ordinal);

    public CompanyBrainStore(IOptions<ContextMemoryOptions> options)
    {
        var config = options.Value;
        _companiesRoot = Path.Combine(
            Path.GetFullPath(config.DataPath, config.ContentRootPath),
            "companies");
        Directory.CreateDirectory(_companiesRoot);
        LoadAll();
    }

    public bool TryGetCompany(string companyId, out CompanyProfile? company) =>
        _companies.TryGetValue(companyId, out company);

    public IReadOnlyList<CompanyProfile> ListCompanies() =>
        _companies.Values.OrderBy(c => c.Name).ToList();

    public bool RegisterCompany(CompanyProfile company)
    {
        if (!_companies.TryAdd(company.CompanyId, company))
            return false;

        EnsureCompanyDirs(company.CompanyId);
        WriteJson(CompanyFile(company.CompanyId), company);
        WriteJson(AppLinksFile(company.CompanyId), Array.Empty<string>());
        SetWebhookSecret(company.CompanyId);
        return true;
    }

    public bool UpsertProcess(CompanyProcess process)
    {
        if (!_companies.ContainsKey(process.CompanyId))
            return false;

        var bucket = _processes.GetOrAdd(process.CompanyId, _ => new ConcurrentDictionary<string, CompanyProcess>(StringComparer.Ordinal));
        bucket[process.ProcessId] = process;
        WriteJson(ProcessFile(process.CompanyId, process.ProcessId), process);
        return true;
    }

    public IReadOnlyList<CompanyProcess> ListProcesses(string companyId) =>
        _processes.TryGetValue(companyId, out var bucket)
            ? bucket.Values.OrderBy(p => p.Title).ToList()
            : [];

    public bool TryGetProcess(string companyId, string processId, out CompanyProcess? process)
    {
        process = null;
        return _processes.TryGetValue(companyId, out var bucket)
            && bucket.TryGetValue(processId, out process);
    }

    public bool UpsertKnowledgeSource(KnowledgeSource source)
    {
        if (!_companies.ContainsKey(source.CompanyId))
            return false;

        var bucket = _sources.GetOrAdd(source.CompanyId, _ => new ConcurrentDictionary<string, KnowledgeSource>(StringComparer.Ordinal));
        bucket[source.SourceId] = source;
        WriteJson(SourceFile(source.CompanyId, source.SourceId), source);
        return true;
    }

    public IReadOnlyList<KnowledgeSource> ListKnowledgeSources(string companyId) =>
        _sources.TryGetValue(companyId, out var bucket)
            ? bucket.Values.OrderBy(s => s.DisplayName).ToList()
            : [];

    public bool LinkApp(string companyId, string appId)
    {
        if (!_companies.ContainsKey(companyId))
            return false;

        if (_appToCompany.TryGetValue(appId, out var existing) && !string.Equals(existing, companyId, StringComparison.Ordinal))
            return false;

        _appToCompany[appId] = companyId;
        var apps = _companyApps.GetOrAdd(companyId, _ => new HashSet<string>(StringComparer.Ordinal));
        apps.Add(appId);
        PersistAppLinks(companyId);
        return true;
    }

    public bool UnlinkApp(string companyId, string appId)
    {
        if (!_appToCompany.TryGetValue(appId, out var linked) || !string.Equals(linked, companyId, StringComparison.Ordinal))
            return false;

        _appToCompany.TryRemove(appId, out _);
        if (_companyApps.TryGetValue(companyId, out var apps))
            apps.Remove(appId);
        PersistAppLinks(companyId);
        return true;
    }

    public bool TryGetCompanyForApp(string appId, out string? companyId) =>
        _appToCompany.TryGetValue(appId, out companyId);

    public IReadOnlyList<string> ListLinkedApps(string companyId) =>
        _companyApps.TryGetValue(companyId, out var apps)
            ? apps.OrderBy(a => a).ToList()
            : [];

    public void SaveSkillsCache(string companyId, CompanySkillsFile skillsFile) =>
        WriteJson(SkillsCacheFile(companyId), skillsFile);

    public bool TryGetSkillsCache(string companyId, out CompanySkillsFile? skillsFile)
    {
        skillsFile = null;
        var path = SkillsCacheFile(companyId);
        if (!File.Exists(path))
            return false;

        skillsFile = ReadJson<CompanySkillsFile>(path);
        return skillsFile is not null;
    }

    public bool TryGetWebhookSecret(string companyId, out string? secret)
    {
        secret = null;
        var path = WebhookSecretFile(companyId);
        if (!File.Exists(path))
            return false;
        secret = File.ReadAllText(path).Trim();
        return secret.Length > 0;
    }

    public string SetWebhookSecret(string companyId, string? secret = null)
    {
        if (!_companies.ContainsKey(companyId))
            throw new InvalidOperationException($"Company '{companyId}' not found.");

        secret ??= Security.CompanyWebhookAuth.CreateWebhookSecret();
        Directory.CreateDirectory(CompanyDir(companyId));
        File.WriteAllText(WebhookSecretFile(companyId), secret);
        return secret;
    }

    private string WebhookSecretFile(string companyId) => Path.Combine(CompanyDir(companyId), "webhook-secret.txt");

    private void LoadAll()
    {
        if (!Directory.Exists(_companiesRoot))
            return;

        foreach (var companyDir in Directory.EnumerateDirectories(_companiesRoot))
        {
            var companyId = Path.GetFileName(companyDir);
            var companyPath = Path.Combine(companyDir, "company.json");
            if (!File.Exists(companyPath))
                continue;

            var company = ReadJson<CompanyProfile>(companyPath);
            if (company is null)
                continue;

            _companies[companyId] = company;

            var processDir = Path.Combine(companyDir, "processes");
            if (Directory.Exists(processDir))
            {
                var bucket = _processes.GetOrAdd(companyId, _ => new ConcurrentDictionary<string, CompanyProcess>(StringComparer.Ordinal));
                foreach (var file in Directory.EnumerateFiles(processDir, "*.json"))
                {
                    var process = ReadJson<CompanyProcess>(file);
                    if (process is not null)
                        bucket[process.ProcessId] = process;
                }
            }

            var sourceDir = Path.Combine(companyDir, "sources");
            if (Directory.Exists(sourceDir))
            {
                var bucket = _sources.GetOrAdd(companyId, _ => new ConcurrentDictionary<string, KnowledgeSource>(StringComparer.Ordinal));
                foreach (var file in Directory.EnumerateFiles(sourceDir, "*.json"))
                {
                    var source = ReadJson<KnowledgeSource>(file);
                    if (source is not null)
                        bucket[source.SourceId] = source;
                }
            }

            var linksPath = AppLinksFile(companyId);
            if (File.Exists(linksPath))
            {
                var apps = ReadJson<string[]>(linksPath) ?? [];
                var set = _companyApps.GetItem(companyId) ?? new HashSet<string>(StringComparer.Ordinal);
                foreach (var appId in apps)
                {
                    set.Add(appId);
                    _appToCompany[appId] = companyId;
                }
                _companyApps[companyId] = set;
            }
        }
    }

    private void EnsureCompanyDirs(string companyId)
    {
        var root = CompanyDir(companyId);
        Directory.CreateDirectory(root);
        Directory.CreateDirectory(Path.Combine(root, "processes"));
        Directory.CreateDirectory(Path.Combine(root, "sources"));
    }

    private void PersistAppLinks(string companyId)
    {
        var apps = ListLinkedApps(companyId);
        WriteJson(AppLinksFile(companyId), apps);
    }

    private string CompanyDir(string companyId) => Path.Combine(_companiesRoot, companyId);
    private string CompanyFile(string companyId) => Path.Combine(CompanyDir(companyId), "company.json");
    private string ProcessFile(string companyId, string processId) =>
        Path.Combine(CompanyDir(companyId), "processes", $"{processId}.json");
    private string SourceFile(string companyId, string sourceId) =>
        Path.Combine(CompanyDir(companyId), "sources", $"{sourceId}.json");
    private string AppLinksFile(string companyId) => Path.Combine(CompanyDir(companyId), "app-links.json");
    private string SkillsCacheFile(string companyId) => Path.Combine(CompanyDir(companyId), "skills-cache.json");

    private static void WriteJson<T>(string path, T value)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, JsonSerializer.Serialize(value, JsonOptions));
    }

    private static T? ReadJson<T>(string path)
    {
        try
        {
            return JsonSerializer.Deserialize<T>(File.ReadAllText(path), JsonOptions);
        }
        catch
        {
            return default;
        }
    }
}

internal static class ConcurrentDictionaryExtensions
{
    public static TValue? GetItem<TKey, TValue>(this ConcurrentDictionary<TKey, TValue> dict, TKey key)
        where TKey : notnull
    {
        dict.TryGetValue(key, out var value);
        return value;
    }
}
