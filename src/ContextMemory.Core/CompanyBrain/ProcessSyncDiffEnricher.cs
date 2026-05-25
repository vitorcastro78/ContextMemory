using ContextMemory.Core.Models;

namespace ContextMemory.Core.CompanyBrain;

public static class ProcessSyncDiffEnricher
{
    public static ProcessSyncDiffDetail Enrich(
        ProcessSyncDiff diff,
        IReadOnlyList<CompanyProcess> before,
        IReadOnlyList<CompanyProcess> after)
    {
        var beforeMap = before.ToDictionary(p => p.ProcessId, StringComparer.Ordinal);
        var afterMap = after.ToDictionary(p => p.ProcessId, StringComparer.Ordinal);

        return new ProcessSyncDiffDetail
        {
            Added = diff.Added.Select(id => ToItem(id, afterMap, "added")).ToList(),
            Updated = diff.Updated.Select(id => ToItem(id, afterMap, "updated")).ToList(),
            Removed = diff.Removed.Select(id => ToItem(id, beforeMap, "removed")).ToList(),
            TotalBefore = diff.TotalBefore,
            TotalAfter = diff.TotalAfter,
            SideBySide = ProcessSyncDiffSideBySideBuilder.Build(before, after, diff)
        };
    }

    public static IReadOnlyList<SyncCriticalAlert> BuildCriticalAlerts(ProcessSyncDiffDetail detail) =>
        detail.Added.Concat(detail.Updated).Concat(detail.Removed)
            .Where(item => item.IsCritical && item.ChangeType is "updated" or "removed")
            .Select(item => new SyncCriticalAlert
            {
                ProcessId = item.ProcessId,
                Title = item.Title,
                ChangeType = item.ChangeType
            })
            .ToList();

    private static ProcessDiffItem ToItem(
        string processId,
        IReadOnlyDictionary<string, CompanyProcess> lookup,
        string changeType)
    {
        if (!lookup.TryGetValue(processId, out var process))
        {
            return new ProcessDiffItem
            {
                ProcessId = processId,
                Title = processId,
                ChangeType = changeType,
                IsCritical = false
            };
        }

        return new ProcessDiffItem
        {
            ProcessId = processId,
            Title = process.Title,
            ChangeType = changeType,
            IsCritical = process.IsCritical
        };
    }
}
