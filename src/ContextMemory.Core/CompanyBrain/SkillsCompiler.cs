using System.Text;
using ContextMemory.Core.Models;

namespace ContextMemory.Core.CompanyBrain;

public static class SkillsCompiler
{
    public static CompanySkillsFile Compile(string companyId, IReadOnlyList<CompanyProcess> processes)
    {
        var grouped = processes
            .GroupBy(p => p.Category)
            .OrderBy(g => g.Key);

        var skills = new List<CompanySkill>();
        foreach (var group in grouped)
        {
            var skillId = group.Key.ToString().ToLowerInvariant();
            var instructions = BuildSkillInstructions(group.Key, group.ToList());
            skills.Add(new CompanySkill
            {
                SkillId = skillId,
                Name = FormatCategoryName(group.Key),
                Description = $"Procedimentos de {FormatCategoryName(group.Key).ToLowerInvariant()} da empresa.",
                ProcessIds = group.Select(p => p.ProcessId).ToList(),
                Instructions = instructions
            });
        }

        return new CompanySkillsFile
        {
            CompanyId = companyId,
            Version = "1.0",
            GeneratedAt = DateTimeOffset.UtcNow,
            Skills = skills,
            Processes = processes.OrderBy(p => p.Title).ToList()
        };
    }

    public static string FormatProcessBlock(CompanyProcess process)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"### {process.Title}");
        if (!string.IsNullOrWhiteSpace(process.Summary))
            sb.AppendLine(process.Summary.Trim());

        if (process.Steps.Count > 0)
        {
            sb.AppendLine("Passos:");
            foreach (var step in process.Steps.OrderBy(s => s.Order))
                sb.AppendLine($"{step.Order}. {step.Action}");
        }

        if (process.Guardrails.Count > 0)
        {
            sb.AppendLine("Guardrails:");
            foreach (var rule in process.Guardrails)
                sb.AppendLine($"- {rule}");
        }

        return sb.ToString().Trim();
    }

    private static string BuildSkillInstructions(ProcessCategory category, IReadOnlyList<CompanyProcess> processes)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"Skill: {FormatCategoryName(category)}");
        sb.AppendLine("Quando aplicável, segue estes procedimentos da empresa:");

        foreach (var process in processes.OrderBy(p => p.Title))
        {
            sb.AppendLine();
            sb.AppendLine(FormatProcessBlock(process));
        }

        return sb.ToString().Trim();
    }

    private static string FormatCategoryName(ProcessCategory category) => category switch
    {
        ProcessCategory.Operations => "Operações",
        ProcessCategory.Support => "Suporte",
        ProcessCategory.Engineering => "Engenharia",
        ProcessCategory.Finance => "Finanças",
        ProcessCategory.Compliance => "Compliance",
        _ => "Geral"
    };
}
