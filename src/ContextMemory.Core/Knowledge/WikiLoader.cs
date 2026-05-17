using System.Text;
using ContextMemory.Core.Configuration;
using ContextMemory.Core.Models;
using Markdig;
using Markdig.Syntax;
using Markdig.Syntax.Inlines;
using Microsoft.Extensions.Options;

namespace ContextMemory.Core.Knowledge;

public sealed class WikiLoader
{
    private readonly WikiLoaderOptions _options;

    public WikiLoader(IOptions<ContextMemoryOptions> options)
    {
        var config = options.Value;
        _options = new WikiLoaderOptions
        {
            MaxChunkTokens = config.MaxChunkTokens > 0 ? config.MaxChunkTokens : 512,
            ChunkOverlapTokens = config.ChunkOverlapTokens > 0 ? config.ChunkOverlapTokens : 50
        };
    }

    public IReadOnlyList<WikiChunk> LoadFile(string wikiRoot, string filePath)
    {
        var fullPath = Path.GetFullPath(filePath);
        var root = Path.GetFullPath(wikiRoot);
        if (!fullPath.StartsWith(root, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("File path is outside wiki root.");

        var relativePath = Path.GetRelativePath(root, fullPath).Replace('\\', '/');
        var markdown = File.ReadAllText(fullPath);
        return LoadMarkdown(relativePath, markdown);
    }

    public IReadOnlyList<WikiChunk> LoadMarkdown(string sourcePath, string markdown)
    {
        var document = Markdown.Parse(markdown);
        var sections = ExtractSections(document);
        var chunks = new List<WikiChunk>();

        foreach (var section in sections)
        {
            var headerPath = string.Join(" > ", section.Headers);
            var prefix = string.IsNullOrEmpty(headerPath)
                ? $"[{sourcePath}]"
                : $"[{sourcePath} > {headerPath}]";

            foreach (var piece in SplitByTokenLimit(section.Content, _options.MaxChunkTokens, _options.ChunkOverlapTokens))
            {
                if (string.IsNullOrWhiteSpace(piece))
                    continue;

                chunks.Add(new WikiChunk($"{prefix}\n{piece.Trim()}", sourcePath, headerPath));
            }
        }

        if (chunks.Count == 0 && !string.IsNullOrWhiteSpace(markdown))
        {
            foreach (var piece in SplitByTokenLimit(markdown.Trim(), _options.MaxChunkTokens, _options.ChunkOverlapTokens))
            {
                chunks.Add(new WikiChunk($"[{sourcePath}]\n{piece.Trim()}", sourcePath, string.Empty));
            }
        }

        return chunks;
    }

    private static List<WikiSection> ExtractSections(MarkdownDocument document)
    {
        var sections = new List<WikiSection>();
        var headerStack = new List<string>();
        var content = new StringBuilder();
        WikiSection? current = null;

        void Flush()
        {
            if (current is null && content.Length == 0)
                return;

            var section = current ?? new WikiSection([], content.ToString().Trim());
            if (!string.IsNullOrWhiteSpace(section.Content))
                sections.Add(section with { Content = section.Content.Trim() });

            content.Clear();
            current = null;
        }

        foreach (var block in document)
        {
            if (block is HeadingBlock heading && heading.Level is 2 or 3)
            {
                Flush();

                while (headerStack.Count >= heading.Level - 1)
                    headerStack.RemoveAt(headerStack.Count - 1);

                var title = ExtractInlineText(heading.Inline).Trim();
                if (string.IsNullOrEmpty(title))
                    continue;

                if (heading.Level == 2)
                    headerStack.Clear();

                headerStack.Add(title);
                current = new WikiSection(headerStack.ToArray(), string.Empty);
                continue;
            }

            var text = ExtractBlockText(block);
            if (string.IsNullOrWhiteSpace(text))
                continue;

            current ??= new WikiSection(headerStack.ToArray(), string.Empty);
            if (content.Length > 0)
                content.AppendLine();
            content.Append(text);
        }

        Flush();
        return sections;
    }

    private static string ExtractBlockText(Block block) => block switch
    {
        ParagraphBlock paragraph => ExtractInlineText(paragraph.Inline),
        HeadingBlock heading => ExtractInlineText(heading.Inline),
        ListBlock list => string.Join('\n', list.OfType<ListItemBlock>().Select(ExtractListItemText)),
        QuoteBlock quote => string.Join('\n', quote.OfType<Block>().Select(ExtractBlockText)),
        CodeBlock code => code.Lines.ToString(),
        LeafBlock leaf => ExtractInlineText(leaf.Inline),
        ContainerBlock container => string.Join('\n',
            container.OfType<Block>().Select(ExtractBlockText).Where(t => !string.IsNullOrWhiteSpace(t))),
        _ => string.Empty
    };

    private static string ExtractListItemText(ListItemBlock item) =>
        string.Join(' ', item.OfType<Block>().Select(ExtractBlockText).Where(t => !string.IsNullOrWhiteSpace(t)));

    private static string ExtractInlineText(ContainerInline? inline)
    {
        if (inline is null)
            return string.Empty;

        var sb = new StringBuilder();
        foreach (var node in inline)
        {
            switch (node)
            {
                case LiteralInline literal:
                    sb.Append(literal.Content.ToString());
                    break;
                case LineBreakInline:
                    sb.Append(' ');
                    break;
                case LinkInline link:
                    sb.Append(ExtractInlineText(link));
                    break;
            }
        }

        return sb.ToString();
    }

    internal static IEnumerable<string> SplitByTokenLimit(string text, int maxTokens, int overlapTokens)
    {
        if (string.IsNullOrWhiteSpace(text))
            yield break;

        var maxChars = maxTokens * 4;
        var overlapChars = overlapTokens * 4;

        if (text.Length <= maxChars)
        {
            yield return text;
            yield break;
        }

        var start = 0;
        while (start < text.Length)
        {
            var length = Math.Min(maxChars, text.Length - start);
            var slice = text.Substring(start, length).Trim();
            if (slice.Length > 0)
                yield return slice;

            if (start + length >= text.Length)
                break;

            start += Math.Max(1, maxChars - overlapChars);
        }
    }

    private sealed record WikiSection(string[] Headers, string Content);

    private sealed class WikiLoaderOptions
    {
        public int MaxChunkTokens { get; init; }
        public int ChunkOverlapTokens { get; init; }
    }
}
