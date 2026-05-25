using System.Text;
using ContextMemory.Core.Models;

namespace ContextMemory.Core.CompanyBrain;

public static class SkillsExporter
{
    public static string ToYaml(CompanySkillsFile skills)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"company_id: {YamlScalar(skills.CompanyId)}");
        sb.AppendLine($"version: {YamlScalar(skills.Version)}");
        sb.AppendLine($"generated_at: {skills.GeneratedAt:O}");
        sb.AppendLine("skills:");

        foreach (var skill in skills.Skills)
        {
            sb.AppendLine($"  - id: {YamlScalar(skill.SkillId)}");
            sb.AppendLine($"    name: {YamlScalar(skill.Name)}");
            sb.AppendLine($"    description: {YamlScalar(skill.Description)}");
            sb.AppendLine("    process_ids:");
            foreach (var processId in skill.ProcessIds)
                sb.AppendLine($"      - {YamlScalar(processId)}");
            sb.AppendLine("    instructions: |");
            AppendYamlBlock(sb, skill.Instructions, indent: 6);
        }

        sb.AppendLine("processes:");
        foreach (var process in skills.Processes)
        {
            sb.AppendLine($"  - id: {YamlScalar(process.ProcessId)}");
            sb.AppendLine($"    title: {YamlScalar(process.Title)}");
            sb.AppendLine($"    category: {process.Category}");
            if (!string.IsNullOrWhiteSpace(process.Summary))
                sb.AppendLine($"    summary: {YamlScalar(process.Summary)}");
            if (process.Triggers.Count > 0)
            {
                sb.AppendLine("    triggers:");
                foreach (var trigger in process.Triggers)
                    sb.AppendLine($"      - {YamlScalar(trigger)}");
            }
            if (process.Steps.Count > 0)
            {
                sb.AppendLine("    steps:");
                foreach (var step in process.Steps.OrderBy(s => s.Order))
                    sb.AppendLine($"      - {YamlScalar(step.Action)}");
            }
            if (process.Guardrails.Count > 0)
            {
                sb.AppendLine("    guardrails:");
                foreach (var rule in process.Guardrails)
                    sb.AppendLine($"      - {YamlScalar(rule)}");
            }
        }

        return sb.ToString();
    }

    public static McpSkillsExport ToMcp(CompanySkillsFile skills)
    {
        var tools = skills.Processes.Select(process => new McpToolDefinition
        {
            Name = ToMcpToolName(process.ProcessId),
            Description = BuildMcpDescription(process),
            InputSchema = new
            {
                type = "object",
                properties = new
                {
                    context = new
                    {
                        type = "string",
                        description = "Situação, pergunta ou ticket do utilizador."
                    }
                },
                required = new[] { "context" }
            }
        }).ToList();

        return new McpSkillsExport
        {
            SchemaVersion = "1.0",
            CompanyId = skills.CompanyId,
            GeneratedAt = skills.GeneratedAt,
            Tools = tools,
            Skills = skills.Skills
        };
    }

    private static string BuildMcpDescription(CompanyProcess process)
    {
        var sb = new StringBuilder();
        sb.Append(process.Title);
        if (!string.IsNullOrWhiteSpace(process.Summary))
            sb.AppendLine().Append(process.Summary.Trim());

        if (process.Triggers.Count > 0)
            sb.AppendLine().Append("Triggers: ").Append(string.Join(", ", process.Triggers));

        sb.AppendLine().AppendLine();
        sb.Append(SkillsCompiler.FormatProcessBlock(process));
        return sb.ToString().Trim();
    }

    public static string ToMcpToolName(string processId)
    {
        var normalized = processId.Replace('-', '_');
        return normalized.StartsWith("process_", StringComparison.Ordinal) ? normalized : $"process_{normalized}";
    }

    private static string YamlScalar(string value)
    {
        if (value.Contains('\n') || value.Contains(':') || value.Contains('"') || value.StartsWith(' '))
            return $"\"{value.Replace("\\", "\\\\").Replace("\"", "\\\"")}\"";
        return value;
    }

    private static void AppendYamlBlock(StringBuilder sb, string text, int indent)
    {
        var pad = new string(' ', indent);
        foreach (var line in text.Replace("\r\n", "\n").Split('\n'))
            sb.AppendLine($"{pad}{line}");
    }
}
