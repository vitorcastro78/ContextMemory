using System.Text.Json;
using System.Text.RegularExpressions;

namespace ContextMemory.Core.Tools;

public sealed record ToolCall(string Name, Dictionary<string, string> Arguments);

public sealed class ToolCallParser
{
    private static readonly Regex ToolCallRegex = new(
        @"<tool_call>\s*(\{[\s\S]*?\})\s*</tool_call>",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    public bool TryParse(string response, out ToolCall? toolCall)
    {
        toolCall = null;
        var match = ToolCallRegex.Match(response);
        if (!match.Success)
            return false;

        try
        {
            using var doc = JsonDocument.Parse(match.Groups[1].Value);
            var root = doc.RootElement;
            var name = root.TryGetProperty("name", out var n) ? n.GetString() : null;
            if (string.IsNullOrWhiteSpace(name))
                return false;

            var args = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            if (root.TryGetProperty("arguments", out var arguments))
            {
                foreach (var prop in arguments.EnumerateObject())
                    args[prop.Name] = prop.Value.ToString();
            }

            toolCall = new ToolCall(name, args);
            return true;
        }
        catch
        {
            return false;
        }
    }

    public string StripToolCallMarkup(string response) =>
        ToolCallRegex.Replace(response, string.Empty).Trim();
}
