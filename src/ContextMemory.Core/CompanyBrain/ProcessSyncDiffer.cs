using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using ContextMemory.Core.Models;

namespace ContextMemory.Core.CompanyBrain;

public static class ProcessSyncDiffer
{
    private static readonly JsonSerializerOptions HashOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public static ProcessSyncDiff Compute(
        IReadOnlyList<CompanyProcess> before,
        IReadOnlyList<CompanyProcess> after)
    {
        var beforeMap = before.ToDictionary(p => p.ProcessId, ComputeHash, StringComparer.Ordinal);
        var afterMap = after.ToDictionary(p => p.ProcessId, ComputeHash, StringComparer.Ordinal);

        var added = new List<string>();
        var updated = new List<string>();
        var removed = new List<string>();

        foreach (var (processId, hash) in afterMap)
        {
            if (!beforeMap.ContainsKey(processId))
                added.Add(processId);
            else if (!string.Equals(beforeMap[processId], hash, StringComparison.Ordinal))
                updated.Add(processId);
        }

        foreach (var processId in beforeMap.Keys)
        {
            if (!afterMap.ContainsKey(processId))
                removed.Add(processId);
        }

        return new ProcessSyncDiff
        {
            Added = added,
            Updated = updated,
            Removed = removed,
            TotalBefore = before.Count,
            TotalAfter = after.Count
        };
    }

    internal static string ComputeHash(CompanyProcess process)
    {
        var payload = new
        {
            process.Title,
            process.Summary,
            process.Category,
            process.Triggers,
            process.Steps,
            process.Guardrails,
            process.IsCritical
        };

        var json = JsonSerializer.Serialize(payload, HashOptions);
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(json));
        return Convert.ToHexString(bytes);
    }
}
