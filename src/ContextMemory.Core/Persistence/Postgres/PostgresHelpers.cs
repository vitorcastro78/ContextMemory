using System.Text.Json;
using ContextMemory.Core.Configuration;
using ContextMemory.Core.Models;

namespace ContextMemory.Core.Persistence.Postgres;

internal static class PostgresJson
{
    public static readonly JsonSerializerOptions CamelCase = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false
    };
}

internal static class AppRegistryHelper
{
    public static AppProfile CreateProfile(string appId, string apiKey, AppOptionsEntry entry, ContextMemoryOptions config) =>
        new()
        {
            AppId = appId,
            ApiKey = apiKey,
            SystemPrompt = entry.SystemPrompt,
            DefaultLanguage = entry.DefaultLanguage,
            WikiPath = ResolveWikiPath(entry.WikiPath, appId, config),
            MaxHistoryMessages = entry.MaxHistoryMessages > 0
                ? entry.MaxHistoryMessages
                : config.MaxHistoryMessages,
            WikiChunksTopK = entry.WikiChunksTopK > 0
                ? entry.WikiChunksTopK
                : config.WikiChunksTopK,
            SimilarityThreshold = entry.SimilarityThreshold > 0
                ? entry.SimilarityThreshold
                : config.SimilarityThreshold
        };

    public static string ResolveWikiPath(string configuredPath, string appId, ContextMemoryOptions config)
    {
        var root = config.ContentRootPath;

        if (!string.IsNullOrWhiteSpace(configuredPath))
            return Path.GetFullPath(configuredPath, root);

        var domainCandidate = Path.GetFullPath(Path.Combine(config.WikiPath, appId), root);
        if (Directory.Exists(domainCandidate))
            return domainCandidate;

        return Path.GetFullPath(Path.Combine(config.WikiPath, appId), root);
    }
}
