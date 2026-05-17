using ContextMemory.Core.Contracts;

namespace ContextMemory.Core.Feedback;

public sealed class ImplicitFeedbackDetector : IImplicitFeedbackDetector
{
    private static readonly (string[] Keywords, int Score, string Reason)[] Patterns =
    [
        (["não era isso", "nao era isso", "não é isso", "nao e isso", "wrong", "not what"], -1, "implicit_negative_mismatch"),
        (["repete", "repetir", "try again", "de novo", "outra vez"], -1, "implicit_negative_repeat"),
        (["mais curto", "too long", "muito longo", "resume", "shorter"], -1, "implicit_negative_too_long"),
        (["em formato diferente", "different format", "outro formato"], -1, "implicit_negative_format"),
        (["perfeito", "obrigado", "obrigada", "exactamente", "excelente", "great", "thanks"], 1, "implicit_positive"),
    ];

    public bool TryDetect(string userMessage, out int score, out string? reason)
    {
        score = 0;
        reason = null;
        if (string.IsNullOrWhiteSpace(userMessage))
            return false;

        var text = userMessage.ToLowerInvariant();
        foreach (var (keywords, patternScore, patternReason) in Patterns)
        {
            if (keywords.Any(k => text.Contains(k, StringComparison.Ordinal)))
            {
                score = patternScore;
                reason = patternReason;
                return true;
            }
        }

        return false;
    }
}
