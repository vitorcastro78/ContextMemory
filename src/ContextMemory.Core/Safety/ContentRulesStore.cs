using System.Collections.Concurrent;
using System.Text.Json;
using ContextMemory.Core.Configuration;
using ContextMemory.Core.Contracts;
using ContextMemory.Core.Models;
using Microsoft.Extensions.Options;

namespace ContextMemory.Core.Safety;

public sealed class ContentRulesStore : IContentRulesStore
{
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };
    private readonly ConcurrentDictionary<string, ContentRules> _cache = new(StringComparer.Ordinal);
    private readonly string _profilesRoot;

    public ContentRulesStore(IOptions<ContextMemoryOptions> options)
    {
        _profilesRoot = Path.Combine(
            Path.GetFullPath(options.Value.DataPath, options.Value.ContentRootPath),
            "app-profiles");
    }

    public ContentRules GetRules(string appId)
    {
        return _cache.GetOrAdd(appId, LoadRules);
    }

    public void Reload(string appId) => _cache[appId] = LoadRules(appId);

    public void EnsureDefaultRules(string appId)
    {
        var path = Path.Combine(_profilesRoot, appId, "content-rules.json");
        if (File.Exists(path))
            return;

        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var rules = ContentRulesDefaults.ForApp(appId);
        File.WriteAllText(path, JsonSerializer.Serialize(rules, JsonOptions));
        _cache[appId] = rules;
    }

    private ContentRules LoadRules(string appId)
    {
        var path = Path.Combine(_profilesRoot, appId, "content-rules.json");
        if (!File.Exists(path))
            return new ContentRules();

        var json = File.ReadAllText(path);
        return JsonSerializer.Deserialize<ContentRules>(json, JsonOptions) ?? new ContentRules();
    }
}
