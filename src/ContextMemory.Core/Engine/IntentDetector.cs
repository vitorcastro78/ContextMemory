using ContextMemory.Core.Contracts;
using ContextMemory.Core.Models;

namespace ContextMemory.Core.Engine;

public sealed class IntentDetector : IIntentDetector
{
    private static readonly string[] ReportKeywords =
    [
        "relatório", "relatorio", "report", "análise completa", "analise completa",
        "documento formal", "parecer", "memorando"
    ];

    private static readonly string[] QuickKeywords =
    [
        "rápido", "rapido", "resumo", "brevemente", "em poucas palavras",
        "quick", "short", "briefly", "o que é", "o que e", "define"
    ];

    private static readonly string[] ProcedureKeywords =
    [
        "procedimento", "passos", "como fazer", "como faço", "como faco",
        "lista numerada", "checklist", "workflow", "passo a passo", "steps"
    ];

    public MessageIntent Detect(string? message)
    {
        if (string.IsNullOrWhiteSpace(message))
            return MessageIntent.General;

        var text = message.ToLowerInvariant();

        if (ReportKeywords.Any(k => text.Contains(k, StringComparison.Ordinal)))
            return MessageIntent.GenerateReport;

        if (ProcedureKeywords.Any(k => text.Contains(k, StringComparison.Ordinal)))
            return MessageIntent.ListProcedure;

        if (QuickKeywords.Any(k => text.Contains(k, StringComparison.Ordinal)))
            return MessageIntent.QuickQuestion;

        return MessageIntent.General;
    }
}
