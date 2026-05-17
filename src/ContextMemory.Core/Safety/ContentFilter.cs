using ContextMemory.Core.Contracts;
using ContextMemory.Core.Models;

namespace ContextMemory.Core.Safety;

public sealed class ContentFilter : IContentFilter
{
    public ContentFilterResult FilterPre(string appId, string userId, string content, ContentRules rules)
    {
        if (content.Length > rules.MaxInputLength)
            return ContentFilterResult.Block($"input_exceeds_max_length_{rules.MaxInputLength}");

        foreach (var topic in rules.BlockedTopics)
        {
            if (string.IsNullOrWhiteSpace(topic))
                continue;

            if (content.Contains(topic, StringComparison.OrdinalIgnoreCase))
                return ContentFilterResult.Block($"blocked_topic:{topic}");
        }

        string? prefix = null;
        foreach (var sensitive in rules.SensitiveTopics)
        {
            if (!string.IsNullOrWhiteSpace(sensitive)
                && content.Contains(sensitive, StringComparison.OrdinalIgnoreCase))
            {
                prefix = "[AVISO] Este tópico pode requerer validação humana adicional.\n";
                break;
            }
        }

        return ContentFilterResult.Pass(prefix is null ? null : prefix + content);
    }

    public ContentFilterResult FilterPost(
        string appId,
        string userId,
        string content,
        ContentRules rules,
        string defaultLanguage)
    {
        if (content.Length > rules.MaxResponseLength)
        {
            content = content[..rules.MaxResponseLength] + "\n[... truncado]";
        }

        if (!string.IsNullOrWhiteSpace(rules.RequiredDisclaimer)
            && !content.Contains(rules.RequiredDisclaimer, StringComparison.OrdinalIgnoreCase))
        {
            content = content.TrimEnd() + "\n\n" + rules.RequiredDisclaimer;
        }

        if (!string.IsNullOrWhiteSpace(rules.EnforceLanguage)
            && rules.EnforceLanguage.StartsWith("pt", StringComparison.OrdinalIgnoreCase))
        {
            var englishRatio = EstimateEnglishRatio(content);
            if (englishRatio > 0.6 && content.Length > 80)
                content = "[Nota: responde em português conforme configurado.]\n" + content;
        }

        return ContentFilterResult.Pass(content);
    }

    private static double EstimateEnglishRatio(string text)
    {
        var words = text.Split([' ', '\n', '\r', '\t'], StringSplitOptions.RemoveEmptyEntries);
        if (words.Length == 0)
            return 0;

        var englishMarkers = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "the", "and", "is", "are", "was", "for", "with", "this", "that", "you", "your"
        };

        var hits = words.Count(w => englishMarkers.Contains(w.Trim('.', ',', ';', ':', '!')));
        return (double)hits / words.Length;
    }
}
