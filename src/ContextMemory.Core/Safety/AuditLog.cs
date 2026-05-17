using System.Collections.Concurrent;
using System.Text.Json;
using ContextMemory.Core.Configuration;
using ContextMemory.Core.Contracts;
using ContextMemory.Core.Models;
using Microsoft.Extensions.Options;

namespace ContextMemory.Core.Safety;

public sealed class AuditLog : IAuditLog
{
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };
    private readonly string _auditRoot;
    private readonly ConcurrentDictionary<string, SemaphoreSlim> _locks = new();

    public AuditLog(IOptions<ContextMemoryOptions> options)
    {
        _auditRoot = Path.GetFullPath(
            options.Value.AuditLogPath.StartsWith('.')
                ? Path.Combine(options.Value.DataPath, "audit")
                : options.Value.AuditLogPath,
            options.Value.ContentRootPath);
        Directory.CreateDirectory(_auditRoot);
    }

    public async Task AppendAsync(AuditLogEntry entry, CancellationToken cancellationToken = default)
    {
        var gate = GetLock(entry.AppId);
        await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var path = GetPath(entry.AppId);
            var line = JsonSerializer.Serialize(entry, JsonOptions) + Environment.NewLine;
            await File.AppendAllTextAsync(path, line, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            gate.Release();
        }
    }

    public async Task<IReadOnlyList<AuditLogEntry>> GetByAppAsync(
        string appId,
        int limit = 100,
        CancellationToken cancellationToken = default)
    {
        var path = GetPath(appId);
        if (!File.Exists(path))
            return [];

        var lines = await File.ReadAllLinesAsync(path, cancellationToken).ConfigureAwait(false);
        var entries = new List<AuditLogEntry>();

        foreach (var line in lines.Reverse().Take(limit))
        {
            if (string.IsNullOrWhiteSpace(line))
                continue;

            var entry = JsonSerializer.Deserialize<AuditLogEntry>(line, JsonOptions);
            if (entry is not null)
                entries.Add(entry);
        }

        return entries;
    }

    private string GetPath(string appId) => Path.Combine(_auditRoot, $"{appId}.jsonl");
    private SemaphoreSlim GetLock(string appId) => _locks.GetOrAdd(appId, _ => new SemaphoreSlim(1, 1));
}
