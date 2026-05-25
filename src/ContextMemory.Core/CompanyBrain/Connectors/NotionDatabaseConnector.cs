using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using ContextMemory.Core.Contracts;
using ContextMemory.Core.Models;
using Microsoft.Extensions.Http;

namespace ContextMemory.Core.CompanyBrain.Connectors;

public sealed class NotionDatabaseConnector : IKnowledgeSourceConnector
{
    private const string NotionVersion = "2022-06-28";
    private readonly HttpClient _http;

    public NotionDatabaseConnector(IHttpClientFactory httpClientFactory)
    {
        _http = httpClientFactory.CreateClient(nameof(NotionDatabaseConnector));
        _http.Timeout = TimeSpan.FromSeconds(60);
    }

    public KnowledgeSourceType SourceType => KnowledgeSourceType.Notion;

    public async Task<KnowledgeSyncResult> SyncAsync(KnowledgeSource source, CancellationToken cancellationToken = default)
    {
        if (!source.Settings.TryGetValue("apiToken", out var apiToken) || string.IsNullOrWhiteSpace(apiToken))
            return new KnowledgeSyncResult { Messages = ["Notion source requires settings.apiToken."] };

        if (!source.Settings.TryGetValue("databaseId", out var databaseId) || string.IsNullOrWhiteSpace(databaseId))
            return new KnowledgeSyncResult { Messages = ["Notion source requires settings.databaseId."] };

        var url = $"https://api.notion.com/v1/databases/{databaseId.Trim()}/query";
        using var request = new HttpRequestMessage(HttpMethod.Post, url)
        {
            Content = new StringContent("{}", Encoding.UTF8, "application/json")
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiToken.Trim());
        request.Headers.Add("Notion-Version", NotionVersion);

        HttpResponseMessage response;
        try
        {
            response = await _http.SendAsync(request, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            return new KnowledgeSyncResult { Messages = [$"Notion request failed: {ex.Message}"] };
        }

        if (!response.IsSuccessStatusCode)
        {
            var body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            return new KnowledgeSyncResult
            {
                Messages = [$"Notion API returned {(int)response.StatusCode}: {body[..Math.Min(body.Length, 200)]}"]
            };
        }

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken).ConfigureAwait(false);

        if (!doc.RootElement.TryGetProperty("results", out var results))
            return new KnowledgeSyncResult { Messages = ["Notion response missing results array."] };

        var processes = new List<CompanyProcess>();
        var messages = new List<string>();

        foreach (var page in results.EnumerateArray())
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!page.TryGetProperty("id", out var idEl))
                continue;

            var pageId = idEl.GetString() ?? string.Empty;
            var title = ExtractNotionTitle(page);
            var blockText = await FetchPageTextAsync(pageId, apiToken, cancellationToken).ConfigureAwait(false);
            var combined = $"{title}\n{blockText}";
            var virtualFile = $"notion:{pageId}";
            var markdown = $"## Process: {title}\n{combined}";
            var extracted = MarkdownWikiConnector.ExtractProcesses(source.CompanyId, virtualFile, markdown);
            if (extracted.Count > 0)
            {
                processes.AddRange(extracted);
                continue;
            }

            if (combined.Length < 40)
                continue;

            processes.Add(new CompanyProcess
            {
                ProcessId = MarkdownWikiConnector.Slugify(title),
                CompanyId = source.CompanyId,
                Title = title,
                Summary = combined.Length > 240 ? combined[..240] + "…" : combined,
                Category = ProcessCategory.General,
                Triggers = ["notion", title.ToLowerInvariant()],
                Steps = [new ProcessStep { Order = 1, Action = "Apply Notion page guidance." }],
                SourceRef = virtualFile,
                UpdatedAt = DateTimeOffset.UtcNow
            });
        }

        messages.Add($"Notion sync parsed {processes.Count} process(es) from {results.GetArrayLength()} page(s).");
        return new KnowledgeSyncResult { Processes = processes, Messages = messages };
    }

    private async Task<string> FetchPageTextAsync(string pageId, string apiToken, CancellationToken cancellationToken)
    {
        var url = $"https://api.notion.com/v1/blocks/{pageId}/children?page_size=100";
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiToken);
        request.Headers.Add("Notion-Version", NotionVersion);

        using var response = await _http.SendAsync(request, cancellationToken).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
            return string.Empty;

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken).ConfigureAwait(false);
        if (!doc.RootElement.TryGetProperty("results", out var blocks))
            return string.Empty;

        var sb = new StringBuilder();
        foreach (var block in blocks.EnumerateArray())
            AppendBlockText(block, sb);

        return sb.ToString().Trim();
    }

    private static void AppendBlockText(JsonElement block, StringBuilder sb)
    {
        if (!block.TryGetProperty("type", out var typeEl))
            return;

        var type = typeEl.GetString();
        if (string.IsNullOrWhiteSpace(type))
            return;

        if (!block.TryGetProperty(type, out var payload))
            return;

        if (payload.TryGetProperty("rich_text", out var richText))
        {
            foreach (var part in richText.EnumerateArray())
            {
                if (part.TryGetProperty("plain_text", out var textEl))
                    sb.AppendLine(textEl.GetString());
            }
        }
    }

    internal static string ExtractNotionTitle(JsonElement page)
    {
        if (!page.TryGetProperty("properties", out var props))
            return "Notion page";

        foreach (var prop in props.EnumerateObject())
        {
            if (!prop.Value.TryGetProperty("title", out var titleArr))
                continue;

            var sb = new StringBuilder();
            foreach (var part in titleArr.EnumerateArray())
            {
                if (part.TryGetProperty("plain_text", out var textEl))
                    sb.Append(textEl.GetString());
            }

            var title = sb.ToString().Trim();
            if (title.Length > 0)
                return title;
        }

        return "Notion page";
    }
}
