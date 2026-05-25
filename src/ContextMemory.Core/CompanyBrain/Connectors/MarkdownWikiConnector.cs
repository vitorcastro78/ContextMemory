using System.Text;
using System.Text.RegularExpressions;
using ContextMemory.Core.Contracts;
using ContextMemory.Core.Models;

namespace ContextMemory.Core.CompanyBrain.Connectors;

public sealed partial class MarkdownWikiConnector : IKnowledgeSourceConnector
{
    public KnowledgeSourceType SourceType => KnowledgeSourceType.MarkdownWiki;

    public Task<KnowledgeSyncResult> SyncAsync(KnowledgeSource source, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (!source.Settings.TryGetValue("wikiPath", out var wikiPath) || string.IsNullOrWhiteSpace(wikiPath))
        {
            return Task.FromResult(new KnowledgeSyncResult
            {
                Messages = ["MarkdownWiki source requires settings.wikiPath."]
            });
        }

        if (!Directory.Exists(wikiPath))
        {
            return Task.FromResult(new KnowledgeSyncResult
            {
                Messages = [$"Wiki path not found: {wikiPath}"]
            });
        }

        var processes = new List<CompanyProcess>();
        var messages = new List<string>();

        foreach (var file in Directory.EnumerateFiles(wikiPath, "*.md", SearchOption.AllDirectories))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var text = File.ReadAllText(file);
            var extracted = ExtractProcesses(source.CompanyId, file, text);
            if (extracted.Count > 0)
            {
                processes.AddRange(extracted);
                messages.Add($"Extracted {extracted.Count} process(es) from {Path.GetFileName(file)}.");
            }
        }

        if (processes.Count == 0)
            messages.Add("No process sections found. Use headings like '## Process: Title' in wiki markdown.");

        return Task.FromResult(new KnowledgeSyncResult
        {
            Processes = processes,
            Messages = messages
        });
    }

    public static IReadOnlyList<CompanyProcess> ExtractProcesses(string companyId, string sourceFile, string markdown)
    {
        var results = new List<CompanyProcess>();
        var matches = ProcessHeading().Matches(markdown);
        if (matches.Count == 0)
            return results;

        for (var i = 0; i < matches.Count; i++)
        {
            var match = matches[i];
            var title = match.Groups["title"].Value.Trim();
            if (title.Length == 0)
                continue;

            var start = match.Index + match.Length;
            var end = i + 1 < matches.Count ? matches[i + 1].Index : markdown.Length;
            var body = markdown[start..end].Trim();

            var processId = Slugify(title);
            var steps = ParseSteps(body);
            var guardrails = ParseGuardrails(body);
            var triggers = ParseTriggers(body);
            var category = ParseCategory(body);
            var summary = ParseSummary(body);
            var isCritical = ParseCritical(body, category);

            results.Add(new CompanyProcess
            {
                ProcessId = processId,
                CompanyId = companyId,
                Title = title,
                Summary = summary,
                Category = category,
                Triggers = triggers,
                Steps = steps,
                Guardrails = guardrails,
                IsCritical = isCritical,
                SourceRef = sourceFile,
                UpdatedAt = DateTimeOffset.UtcNow
            });
        }

        return results;
    }

    private static IReadOnlyList<ProcessStep> ParseSteps(string body)
    {
        var steps = new List<ProcessStep>();
        var order = 1;
        foreach (var line in body.Split('\n'))
        {
            var trimmed = line.Trim();
            var match = OrderedStep().Match(trimmed);
            if (!match.Success)
                continue;

            var action = match.Groups["step"].Value.Trim();
            if (action.Length == 0)
                continue;

            steps.Add(new ProcessStep { Order = order++, Action = action });
        }

        return steps;
    }

    private static IReadOnlyList<string> ParseGuardrails(string body)
    {
        var guardrails = new List<string>();
        var inSection = false;

        foreach (var line in body.Split('\n'))
        {
            var trimmed = line.Trim();
            if (GuardrailHeading().IsMatch(trimmed))
            {
                inSection = true;
                continue;
            }

            if (inSection && Heading().IsMatch(trimmed))
                break;

            if (inSection && trimmed.StartsWith("- ", StringComparison.Ordinal))
                guardrails.Add(trimmed[2..].Trim());
        }

        return guardrails;
    }

    private static IReadOnlyList<string> ParseTriggers(string body)
    {
        var triggers = new List<string>();
        var inSection = false;

        foreach (var line in body.Split('\n'))
        {
            var trimmed = line.Trim();
            if (TriggerHeading().IsMatch(trimmed))
            {
                inSection = true;
                continue;
            }

            if (inSection && Heading().IsMatch(trimmed))
                break;

            if (inSection && trimmed.StartsWith("- ", StringComparison.Ordinal))
                triggers.Add(trimmed[2..].Trim());
        }

        return triggers;
    }

    private static ProcessCategory ParseCategory(string body)
    {
        var match = CategoryLine().Match(body);
        if (!match.Success)
            return ProcessCategory.General;

        return Enum.TryParse<ProcessCategory>(match.Groups["category"].Value, ignoreCase: true, out var category)
            ? category
            : ProcessCategory.General;
    }

    private static bool ParseCritical(string body, ProcessCategory category)
    {
        var match = CriticalLine().Match(body);
        if (match.Success)
        {
            var value = match.Groups["value"].Value.Trim();
            return value.Equals("yes", StringComparison.OrdinalIgnoreCase)
                || value.Equals("true", StringComparison.OrdinalIgnoreCase)
                || value.Equals("1", StringComparison.Ordinal);
        }

        return category == ProcessCategory.Compliance;
    }

    private static string ParseSummary(string body)
    {
        foreach (var line in body.Split('\n'))
        {
            var trimmed = line.Trim();
            if (trimmed.Length == 0 || Heading().IsMatch(trimmed) || OrderedStep().IsMatch(trimmed))
                continue;

            return trimmed;
        }

        return string.Empty;
    }

    public static string Slugify(string value)
    {
        var slug = value.ToLowerInvariant();
        slug = NonSlug().Replace(slug, "-");
        slug = MultiDash().Replace(slug, "-").Trim('-');
        return slug.Length > 0 ? slug : "process";
    }

    [GeneratedRegex(@"^#{2,3}\s+Process:\s*(?<title>.+)$", RegexOptions.Multiline | RegexOptions.IgnoreCase)]
    private static partial Regex ProcessHeading();

    [GeneratedRegex(@"^\d+\.\s+(?<step>.+)$")]
    private static partial Regex OrderedStep();

    [GeneratedRegex(@"^#{2,4}\s+Guardrails\s*$", RegexOptions.IgnoreCase)]
    private static partial Regex GuardrailHeading();

    [GeneratedRegex(@"^#{2,4}\s+Triggers\s*$", RegexOptions.IgnoreCase)]
    private static partial Regex TriggerHeading();

    [GeneratedRegex(@"^#{1,6}\s+")]
    private static partial Regex Heading();

    [GeneratedRegex(@"^Category:\s*(?<category>[A-Za-z]+)\s*$", RegexOptions.IgnoreCase | RegexOptions.Multiline)]
    private static partial Regex CategoryLine();

    [GeneratedRegex(@"^Critical:\s*(?<value>.+)\s*$", RegexOptions.IgnoreCase | RegexOptions.Multiline)]
    private static partial Regex CriticalLine();

    [GeneratedRegex(@"[^a-z0-9]+")]
    private static partial Regex NonSlug();

    [GeneratedRegex(@"-{2,}")]
    private static partial Regex MultiDash();
}
