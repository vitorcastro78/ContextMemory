using System.Text.Json;
using ContextMemory.Core.Configuration;
using ContextMemory.Core.Contracts;
using ContextMemory.Core.Models;
using Microsoft.Extensions.Options;

namespace ContextMemory.Core.CompanyBrain;

public sealed class CompanyBrainDiskImporter
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    private readonly ICompanyBrainStore _store;
    private readonly string _companiesRoot;

    public CompanyBrainDiskImporter(ICompanyBrainStore store, IOptions<ContextMemoryOptions> options)
    {
        _store = store;
        var config = options.Value;
        _companiesRoot = Path.Combine(
            Path.GetFullPath(config.DataPath, config.ContentRootPath),
            "companies");
    }

    public CompanyImportResult ImportAll()
    {
        if (!Directory.Exists(_companiesRoot))
        {
            return new CompanyImportResult
            {
                Messages = [$"Directory not found: {_companiesRoot}"]
            };
        }

        var messages = new List<string>();
        var companies = 0;
        var processes = 0;
        var sources = 0;
        var links = 0;

        foreach (var companyDir in Directory.EnumerateDirectories(_companiesRoot))
        {
            var companyId = Path.GetFileName(companyDir);
            var companyPath = Path.Combine(companyDir, "company.json");
            if (!File.Exists(companyPath))
                continue;

            var profile = ReadJson<CompanyProfile>(companyPath);
            if (profile is null)
                continue;

            if (!_store.TryGetCompany(companyId, out _))
            {
                if (!_store.RegisterCompany(profile))
                {
                    messages.Add($"Skipped company '{companyId}' (already exists).");
                    continue;
                }
                companies++;
            }

            var webhookPath = Path.Combine(companyDir, "webhook-secret.txt");
            if (File.Exists(webhookPath))
            {
                var secret = File.ReadAllText(webhookPath).Trim();
                if (secret.Length > 0)
                    _store.SetWebhookSecret(companyId, secret);
            }

            var processDir = Path.Combine(companyDir, "processes");
            if (Directory.Exists(processDir))
            {
                foreach (var file in Directory.EnumerateFiles(processDir, "*.json"))
                {
                    var process = ReadJson<CompanyProcess>(file);
                    if (process is null)
                        continue;
                    if (_store.UpsertProcess(process))
                        processes++;
                }
            }

            var sourceDir = Path.Combine(companyDir, "sources");
            if (Directory.Exists(sourceDir))
            {
                foreach (var file in Directory.EnumerateFiles(sourceDir, "*.json"))
                {
                    var source = ReadJson<KnowledgeSource>(file);
                    if (source is null)
                        continue;
                    if (_store.UpsertKnowledgeSource(source))
                        sources++;
                }
            }

            var linksPath = Path.Combine(companyDir, "app-links.json");
            if (File.Exists(linksPath))
            {
                var appIds = ReadJson<string[]>(linksPath) ?? [];
                foreach (var appId in appIds)
                {
                    if (_store.LinkApp(companyId, appId))
                        links++;
                }
            }

            var skillsPath = Path.Combine(companyDir, "skills-cache.json");
            if (File.Exists(skillsPath))
            {
                var skills = ReadJson<CompanySkillsFile>(skillsPath);
                if (skills is not null)
                    _store.SaveSkillsCache(companyId, skills);
            }

            messages.Add($"Imported company '{companyId}' from disk.");
        }

        return new CompanyImportResult
        {
            CompaniesImported = companies,
            ProcessesImported = processes,
            SourcesImported = sources,
            AppLinksImported = links,
            Messages = messages
        };
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
