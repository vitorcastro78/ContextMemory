using System.Text.Json;
using System.Text.RegularExpressions;
using ContextMemory.Core.CompanyBrain.Connectors;
using ContextMemory.Core.Contracts;
using ContextMemory.Core.Models;
using Microsoft.Extensions.Http;

namespace ContextMemory.Core.CompanyBrain.Connectors;

public sealed partial class ZendeskHelpCenterConnector : IKnowledgeSourceConnector
{
    private readonly HttpClient _http;

    public ZendeskHelpCenterConnector(IHttpClientFactory httpClientFactory)
    {
        _http = httpClientFactory.CreateClient(nameof(ZendeskHelpCenterConnector));
        _http.Timeout = TimeSpan.FromSeconds(60);
    }

    public KnowledgeSourceType SourceType => KnowledgeSourceType.Zendesk;

    public async Task<KnowledgeSyncResult> SyncAsync(KnowledgeSource source, CancellationToken cancellationToken = default)
    {
        if (!TryGetSetting(source, "subdomain", out var subdomain)
            || !TryGetSetting(source, "email", out var email)
            || !TryGetSetting(source, "apiToken", out var apiToken))
        {
            return new KnowledgeSyncResult
            {
                Messages = ["Zendesk source requires settings: subdomain, email, apiToken."]
            };
        }

        var url = $"https://{subdomain.Trim()}.zendesk.com/api/v2/help_center/articles.json?per_page=100";
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        var credentials = Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes($"{email}/token:{apiToken}"));
        request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Basic", credentials);

        HttpResponseMessage response;
        try
        {
            response = await _http.SendAsync(request, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            return new KnowledgeSyncResult { Messages = [$"Zendesk request failed: {ex.Message}"] };
        }

        if (!response.IsSuccessStatusCode)
        {
            var body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            return new KnowledgeSyncResult
            {
                Messages = [$"Zendesk API returned {(int)response.StatusCode}: {body[..Math.Min(body.Length, 200)]}"]
            };
        }

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken).ConfigureAwait(false);

        if (!doc.RootElement.TryGetProperty("articles", out var articles))
            return new KnowledgeSyncResult { Messages = ["Zendesk response missing articles array."] };

        var processes = new List<CompanyProcess>();
        foreach (var article in articles.EnumerateArray())
        {
            var title = article.TryGetProperty("title", out var titleEl) ? titleEl.GetString() ?? "article" : "article";
            var bodyHtml = article.TryGetProperty("body", out var bodyEl) ? bodyEl.GetString() ?? string.Empty : string.Empty;
            var text = ConfluenceWikiConnector.StripHtml(bodyHtml);
            var virtualFile = $"zendesk:{title}";
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
                Category = ProcessCategory.Support,
                Triggers = ["zendesk", "help center", title.ToLowerInvariant()],
                Steps = [new ProcessStep { Order = 1, Action = "Follow Zendesk article guidance." }],
                SourceRef = virtualFile,
                UpdatedAt = DateTimeOffset.UtcNow
            });
        }

        return new KnowledgeSyncResult
        {
            Processes = processes,
            Messages = [$"Zendesk sync parsed {processes.Count} process(es) from {articles.GetArrayLength()} article(s)."]
        };
    }

    private static bool TryGetSetting(KnowledgeSource source, string key, out string value)
    {
        value = string.Empty;
        return source.Settings.TryGetValue(key, out value) && !string.IsNullOrWhiteSpace(value);
    }
}
