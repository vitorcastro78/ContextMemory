using System.Text;
using ContextMemory.Core.Models;

namespace ContextMemory.Core.CompanyBrain;

public static class ProcessSyncDiffSideBySideBuilder
{
    public static IReadOnlyList<ProcessSideBySideEntry> Build(
        IReadOnlyList<CompanyProcess> before,
        IReadOnlyList<CompanyProcess> after,
        ProcessSyncDiff diff)
    {
        var beforeMap = before.ToDictionary(p => p.ProcessId, StringComparer.Ordinal);
        var afterMap = after.ToDictionary(p => p.ProcessId, StringComparer.Ordinal);
        var entries = new List<ProcessSideBySideEntry>();

        foreach (var processId in diff.Added)
        {
            if (!afterMap.TryGetValue(processId, out var process))
                continue;

            entries.Add(new ProcessSideBySideEntry
            {
                ProcessId = processId,
                Title = process.Title,
                ChangeType = "added",
                IsCritical = process.IsCritical,
                BeforeText = string.Empty,
                AfterText = FormatProcess(process),
                Changes = []
            });
        }

        foreach (var processId in diff.Removed)
        {
            if (!beforeMap.TryGetValue(processId, out var process))
                continue;

            entries.Add(new ProcessSideBySideEntry
            {
                ProcessId = processId,
                Title = process.Title,
                ChangeType = "removed",
                IsCritical = process.IsCritical,
                BeforeText = FormatProcess(process),
                AfterText = string.Empty,
                Changes = []
            });
        }

        foreach (var processId in diff.Updated)
        {
            if (!beforeMap.TryGetValue(processId, out var beforeProcess)
                || !afterMap.TryGetValue(processId, out var afterProcess))
                continue;

            entries.Add(new ProcessSideBySideEntry
            {
                ProcessId = processId,
                Title = afterProcess.Title,
                ChangeType = "updated",
                IsCritical = afterProcess.IsCritical,
                BeforeText = FormatProcess(beforeProcess),
                AfterText = FormatProcess(afterProcess),
                Changes = BuildFieldChanges(beforeProcess, afterProcess)
            });
        }

        return entries
            .OrderBy(e => e.ChangeType switch
            {
                "removed" => 0,
                "updated" => 1,
                _ => 2
            })
            .ThenBy(e => e.Title, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static IReadOnlyList<ProcessFieldChange> BuildFieldChanges(
        CompanyProcess before,
        CompanyProcess after)
    {
        var changes = new List<ProcessFieldChange>();

        AddChange(changes, "Título", before.Title, after.Title);
        AddChange(changes, "Resumo", before.Summary, after.Summary);
        AddChange(changes, "Categoria", before.Category.ToString(), after.Category.ToString());
        AddChange(changes, "Crítico", before.IsCritical ? "sim" : "não", after.IsCritical ? "sim" : "não");
        AddChange(changes, "Triggers", JoinList(before.Triggers), JoinList(after.Triggers));
        AddChange(changes, "Guardrails", JoinList(before.Guardrails), JoinList(after.Guardrails));
        AddChange(changes, "Passos", FormatSteps(before.Steps), FormatSteps(after.Steps));

        return changes;
    }

    private static void AddChange(
        List<ProcessFieldChange> changes,
        string field,
        string before,
        string after)
    {
        if (string.Equals(before, after, StringComparison.Ordinal))
            return;

        changes.Add(new ProcessFieldChange
        {
            Field = field,
            Before = before,
            After = after
        });
    }

    private static string FormatProcess(CompanyProcess process)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"Título: {process.Title}");
        if (!string.IsNullOrWhiteSpace(process.Summary))
            sb.AppendLine($"Resumo: {process.Summary}");
        sb.AppendLine($"Categoria: {process.Category}");
        sb.AppendLine($"Crítico: {(process.IsCritical ? "sim" : "não")}");
        if (process.Triggers.Count > 0)
            sb.AppendLine($"Triggers: {JoinList(process.Triggers)}");
        if (process.Guardrails.Count > 0)
            sb.AppendLine($"Guardrails: {JoinList(process.Guardrails)}");
        if (process.Steps.Count > 0)
        {
            sb.AppendLine("Passos:");
            foreach (var step in process.Steps.OrderBy(s => s.Order))
                sb.AppendLine($"  {step.Order}. {step.Action}");
        }

        return sb.ToString().TrimEnd();
    }

    private static string FormatSteps(IReadOnlyList<ProcessStep> steps) =>
        steps.Count == 0
            ? "(vazio)"
            : string.Join("\n", steps.OrderBy(s => s.Order).Select(s => $"{s.Order}. {s.Action}"));

    private static string JoinList(IReadOnlyList<string> items) =>
        items.Count == 0 ? "(vazio)" : string.Join(", ", items);
}
