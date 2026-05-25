using System.Text.Json;
using ContextMemory.Core.Contracts;
using ContextMemory.Core.Models;

namespace ContextMemory.Core.CompanyBrain.Connectors;

public sealed class ProcessJsonFolderConnector : IKnowledgeSourceConnector
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter() }
    };

    public KnowledgeSourceType SourceType => KnowledgeSourceType.ProcessJsonFolder;

    public Task<KnowledgeSyncResult> SyncAsync(KnowledgeSource source, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (!source.Settings.TryGetValue("folderPath", out var folderPath) || string.IsNullOrWhiteSpace(folderPath))
        {
            return Task.FromResult(new KnowledgeSyncResult
            {
                Messages = ["ProcessJsonFolder source requires settings.folderPath."]
            });
        }

        if (!Directory.Exists(folderPath))
        {
            return Task.FromResult(new KnowledgeSyncResult
            {
                Messages = [$"Folder not found: {folderPath}"]
            });
        }

        var processes = new List<CompanyProcess>();
        var messages = new List<string>();

        foreach (var file in Directory.EnumerateFiles(folderPath, "*.json", SearchOption.AllDirectories))
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                var json = File.ReadAllText(file);
                var parsed = ParseJsonFile(source.CompanyId, file, json);
                if (parsed.Count == 0)
                {
                    messages.Add($"No processes in {Path.GetFileName(file)}.");
                    continue;
                }

                processes.AddRange(parsed);
                messages.Add($"Loaded {parsed.Count} process(es) from {Path.GetFileName(file)}.");
            }
            catch (Exception ex)
            {
                messages.Add($"Failed to read {Path.GetFileName(file)}: {ex.Message}");
            }
        }

        return Task.FromResult(new KnowledgeSyncResult
        {
            Processes = processes,
            Messages = messages
        });
    }

    public static IReadOnlyList<CompanyProcess> ParseJsonFile(string companyId, string sourceFile, string json)
    {
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        if (root.ValueKind == JsonValueKind.Array)
        {
            return root.EnumerateArray()
                .Select(item => MapProcess(companyId, sourceFile, item))
                .Where(p => p is not null)
                .Cast<CompanyProcess>()
                .ToList();
        }

        if (root.TryGetProperty("processes", out var processesElement) && processesElement.ValueKind == JsonValueKind.Array)
        {
            return processesElement.EnumerateArray()
                .Select(item => MapProcess(companyId, sourceFile, item))
                .Where(p => p is not null)
                .Cast<CompanyProcess>()
                .ToList();
        }

        var single = MapProcess(companyId, sourceFile, root);
        return single is null ? [] : [single];
    }

    private static CompanyProcess? MapProcess(string companyId, string sourceFile, JsonElement element)
    {
        var request = element.Deserialize<UpsertProcessRequest>(JsonOptions);
        if (request is null || string.IsNullOrWhiteSpace(request.ProcessId) || string.IsNullOrWhiteSpace(request.Title))
            return null;

        return new CompanyProcess
        {
            ProcessId = request.ProcessId.Trim(),
            CompanyId = companyId,
            Title = request.Title.Trim(),
            Summary = request.Summary.Trim(),
            Category = request.Category,
            Triggers = request.Triggers,
            Steps = request.Steps,
            Guardrails = request.Guardrails,
            SourceRef = sourceFile,
            UpdatedAt = DateTimeOffset.UtcNow
        };
    }
}
