using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using ContextMemory.Core.Contracts;
using ContextMemory.Core.Models;
using Microsoft.Extensions.Http;

namespace ContextMemory.Core.CompanyBrain.Connectors;

public sealed partial class ConfluenceWikiConnector : IKnowledgeSourceConnector
{
    private readonly HttpClient _http;

    public ConfluenceWikiConnector(IHttpClientFactory httpClientFactory)
    {
        _http = httpClientFactory.CreateClient(nameof(ConfluenceWikiConnector));
        _http.Timeout = TimeSpan.FromSeconds(60);
    }

    public KnowledgeSourceType SourceType => KnowledgeSourceType.Confluence;

    public async Task<KnowledgeSyncResult> SyncAsync(KnowledgeSource source, CancellationToken cancellationToken = default)
    {
        if (!TryGetSetting(source, "baseUrl", out var baseUrl)
            || !TryGetSetting(source, "email", out var email)
            || !TryGetSetting(source, "apiToken", out var apiToken))
        {
            return new KnowledgeSyncResult
            {
                Messages = ["Confluence source requires settings: baseUrl, email, apiToken."]
            };
        }

        source.Settings.TryGetValue("spaceKey", out var spaceKey);
        var url = string.IsNullOrWhiteSpace(spaceKey)
            ? $"{baseUrl.TrimEnd('/')}/wiki/rest/api/content?type=page&limit=50&expand=body.storage"
            : $"{baseUrl.TrimEnd('/')}/wiki/rest/api/content?spaceKey={Uri.EscapeDataString(spaceKey)}&type=page&limit=50&expand=body.storage";

        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        var credentials = Convert.ToBase64String(Encoding.UTF8.GetBytes($"{email}:{apiToken}"));
        request.Headers.Authorization = new AuthenticationHeaderValue("Basic", credentials);

        HttpResponseMessage response;
        try
        {
            response = await _http.SendAsync(request, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            return new KnowledgeSyncResult { Messages = [$"Confluence request failed: {ex.Message}"] };
        }

        if (!response.IsSuccessStatusCode)
        {
            var body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            return new KnowledgeSyncResult
            {
                Messages = [$"Confluence API returned {(int)response.StatusCode}: {body[..Math.Min(body.Length, 200)]}"]
            };
        }

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken).ConfigureAwait(false);

        if (!doc.RootElement.TryGetProperty("results", out var results))
            return new KnowledgeSyncResult { Messages = ["Confluence response missing results array."] };

        var processes = new List<CompanyProcess>();
        var messages = new List<string>();

        foreach (var page in results.EnumerateArray())
        {
            var title = page.TryGetProperty("title", out var titleEl) ? titleEl.GetString() ?? "page" : "page";
            if (!page.TryGetProperty("body", out var bodyEl)
                || !bodyEl.TryGetProperty("storage", out var storageEl)
                || !storageEl.TryGetProperty("value", out var valueEl))
                continue;

            var html = valueEl.GetString() ?? string.Empty;
            var text = StripHtml(html);
            var virtualFile = $"confluence:{title}";
            var markdown = $"## Process: {title}\n{text}";
            var extracted = MarkdownWikiConnector.ExtractProcesses(source.CompanyId, virtualFile, markdown);
            if (extracted.Count > 0)
            {
                processes.AddRange(extracted);
                continue;
            }

            if (text.Length < 40)
                continue;

            processes.Add(new CompanyProcess
            {
                ProcessId = MarkdownWikiConnector.Slugify(title),
                CompanyId = source.CompanyId,
                Title = title,
                Summary = text.Length > 240 ? text[..240] + "…" : text,
                Category = ProcessCategory.General,
                Steps = [new ProcessStep { Order = 1, Action = "Follow Confluence page guidance." }],
                SourceRef = virtualFile,
                UpdatedAt = DateTimeOffset.UtcNow
            });
        }

        messages.Add($"Confluence sync parsed {processes.Count} process(es) from {results.GetArrayLength()} page(s).");
        return new KnowledgeSyncResult { Processes = processes, Messages = messages };
    }

    public static string StripHtml(string html) =>
        string.IsNullOrWhiteSpace(html)
            ? string.Empty
            : HtmlTag().Replace(html, " ").Replace("&nbsp;", " ", StringComparison.OrdinalIgnoreCase).Trim();

    private static bool TryGetSetting(KnowledgeSource source, string key, out string value)
    {
        value = string.Empty;
        return source.Settings.TryGetValue(key, out value) && !string.IsNullOrWhiteSpace(value);
    }

    [GeneratedRegex("<[^>]+>")]
    private static partial Regex HtmlTag();
}
