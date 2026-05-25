using System.Text.Json;
using ContextMemory.Core.Configuration;
using ContextMemory.Core.Models;
using Microsoft.Extensions.Options;

namespace ContextMemory.Core.CompanyBrain;

public sealed class SyncHistoryStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false
    };

    private readonly string _companiesRoot;

    public SyncHistoryStore(IOptions<ContextMemoryOptions> options)
    {
        var config = options.Value;
        _companiesRoot = Path.Combine(
            Path.GetFullPath(config.DataPath, config.ContentRootPath),
            "companies");
    }

    public void Append(CompanySyncHistoryEntry entry)
    {
        var companyDir = Path.Combine(_companiesRoot, entry.CompanyId);
        Directory.CreateDirectory(companyDir);
        var line = JsonSerializer.Serialize(entry, JsonOptions);
        File.AppendAllText(Path.Combine(companyDir, "sync-history.jsonl"), line + Environment.NewLine);
    }

    public IReadOnlyList<CompanySyncHistoryEntry> ListRecent(string companyId, int limit = 20)
    {
        var path = Path.Combine(_companiesRoot, companyId, "sync-history.jsonl");
        if (!File.Exists(path))
            return [];

        var lines = File.ReadAllLines(path);
        var entries = new List<CompanySyncHistoryEntry>(lines.Length);
        foreach (var line in lines)
        {
            if (string.IsNullOrWhiteSpace(line))
                continue;

            try
            {
                var entry = JsonSerializer.Deserialize<CompanySyncHistoryEntry>(line, JsonOptions);
                if (entry is not null)
                    entries.Add(entry);
            }
            catch
            {
                // skip corrupt lines
            }
        }

        return entries
            .OrderByDescending(e => e.SyncedAt)
            .Take(Math.Clamp(limit, 1, 100))
            .ToList();
    }

    public CompanySyncHistoryEntry? TryGetEntry(string companyId, string entryId)
    {
        if (string.IsNullOrWhiteSpace(entryId))
            return null;

        return ListRecent(companyId, 100)
            .FirstOrDefault(e => string.Equals(e.EntryId, entryId, StringComparison.Ordinal));
    }
}
