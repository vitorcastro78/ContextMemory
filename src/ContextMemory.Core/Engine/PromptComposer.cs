using System.Text;
using ContextMemory.Core.CompanyBrain;
using ContextMemory.Core.Models;

namespace ContextMemory.Core.Engine;

public sealed class PromptComposer
{
    public string Compose(PromptContext ctx)
    {
        var sb = new StringBuilder();

        if (!string.IsNullOrWhiteSpace(ctx.AppConfig.BasePersona))
            sb.AppendLine(ctx.AppConfig.BasePersona.Trim());

        if (!string.IsNullOrWhiteSpace(ctx.AppConfig.BusinessRules))
        {
            sb.AppendLine("[REGRAS DE NEGÓCIO]");
            sb.AppendLine(ctx.AppConfig.BusinessRules.Trim());
        }

        var activeFacts = ctx.UserProfile.Facts
            .OrderByDescending(f => f.LastConfirmedAt)
            .Take(10)
            .ToList();

        if (activeFacts.Count > 0)
        {
            sb.AppendLine("[PERFIL DO UTILIZADOR]");
            foreach (var fact in activeFacts)
                sb.AppendLine($"- {fact.Text}");
        }

        if (ctx.WikiChunks.Count > 0)
        {
            sb.AppendLine("[CONHECIMENTO DE DOMÍNIO]");
            foreach (var chunk in ctx.WikiChunks)
                sb.AppendLine(chunk.Content);
        }

        if (ctx.ExecutableProcesses.Count > 0)
        {
            sb.AppendLine("[PROCESSOS / SKILLS DA EMPRESA]");
            sb.AppendLine("Segue estes procedimentos quando forem relevantes para a pergunta:");
            foreach (var process in ctx.ExecutableProcesses)
                sb.AppendLine(SkillsCompiler.FormatProcessBlock(process));
        }

        var sessionContext = ctx.SessionContext ?? ctx.UserProfile.SessionContext;
        if (!string.IsNullOrWhiteSpace(sessionContext))
        {
            sb.AppendLine("[CONTEXTO ACTIVO]");
            sb.AppendLine(sessionContext.Trim());
        }

        sb.AppendLine($"[SITUACIONAL] {DateTime.UtcNow:dd MMM yyyy HH:mm} UTC");
        sb.AppendLine($"Responde sempre em {ctx.AppConfig.DefaultLanguage}.");

        var intentInstruction = ctx.Intent switch
        {
            MessageIntent.GenerateReport => "Responde com um relatório estruturado em secções.",
            MessageIntent.QuickQuestion => "Responde de forma concisa, máximo 3 parágrafos.",
            MessageIntent.ListProcedure => "Responde com uma lista numerada de passos.",
            _ => string.Empty
        };

        if (!string.IsNullOrWhiteSpace(intentInstruction))
            sb.AppendLine(intentInstruction);

        if (!string.IsNullOrWhiteSpace(ctx.AppConfig.FormatRules))
            sb.AppendLine(ctx.AppConfig.FormatRules.Trim());

        return sb.ToString().Trim();
    }
}
