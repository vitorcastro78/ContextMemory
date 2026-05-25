using System.Net.Http.Headers;
using System.Text.Json;
using ContextMemory.Core.Contracts;
using ContextMemory.Core.Models;
using Microsoft.Extensions.Http;

namespace ContextMemory.Core.CompanyBrain.Connectors;

public sealed class SlackHistoryConnector : IKnowledgeSourceConnector
{
    private readonly HttpClient _http;

    public SlackHistoryConnector(IHttpClientFactory httpClientFactory)
    {
        _http = httpClientFactory.CreateClient(nameof(SlackHistoryConnector));
        _http.Timeout = TimeSpan.FromSeconds(60);
    }

    public KnowledgeSourceType SourceType => KnowledgeSourceType.Slack;

    public async Task<KnowledgeSyncResult> SyncAsync(KnowledgeSource source, CancellationToken cancellationToken = default)
    {
        if (!source.Settings.TryGetValue("botToken", out var botToken) || string.IsNullOrWhiteSpace(botToken))
        {
            return new KnowledgeSyncResult { Messages = ["Slack source requires settings.botToken."] };
        }

        if (!source.Settings.TryGetValue("channelIds", out var channelIdsRaw) || string.IsNullOrWhiteSpace(channelIdsRaw))
        {
            return new KnowledgeSyncResult { Messages = ["Slack source requires settings.channelIds (comma-separated)."] };
        }

        var channelIds = channelIdsRaw.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var processes = new List<CompanyProcess>();
        var messages = new List<string>();

        foreach (var channelId in channelIds)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var url = $"https://slack.com/api/conversations.history?channel={Uri.EscapeDataString(channelId)}&limit=100";
            using var request = new HttpRequestMessage(HttpMethod.Get, url);
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", botToken);

            HttpResponseMessage response;
            try
            {
                response = await _http.SendAsync(request, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                messages.Add($"Slack channel {channelId}: request failed ({ex.Message}).");
                continue;
            }

            var json = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            using var doc = JsonDocument.Parse(json);
            if (!doc.RootElement.TryGetProperty("ok", out var okEl) || !okEl.GetBoolean())
            {
                var err = doc.RootElement.TryGetProperty("error", out var errEl) ? errEl.GetString() : "unknown";
                messages.Add($"Slack channel {channelId}: API error '{err}'.");
                continue;
            }

            if (!doc.RootElement.TryGetProperty("messages", out var messagesEl))
                continue;

            var markdown = BuildMarkdownFromMessages(channelId, messagesEl);
            var extracted = MarkdownWikiConnector.ExtractProcesses(source.CompanyId, $"slack:{channelId}", markdown);
            processes.AddRange(extracted);

            if (extracted.Count == 0 && markdown.Length > 80)
            {
                processes.Add(new CompanyProcess
                {
                    ProcessId = $"slack-{MarkdownWikiConnector.Slugify(channelId)}",
                    CompanyId = source.CompanyId,
                    Title = $"Slack channel {channelId}",
                    Summary = "Conhecimento recente agregado do canal Slack.",
                    Category = ProcessCategory.Support,
                    Triggers = ["slack", channelId],
                    Steps = [new ProcessStep { Order = 1, Action = "Consultar histórico recente do canal e aplicar políticas internas." }],
                    SourceRef = $"slack:{channelId}",
                    UpdatedAt = DateTimeOffset.UtcNow
                });
            }

            messages.Add($"Slack channel {channelId}: {messagesEl.GetArrayLength()} message(s), {extracted.Count} process block(s).");
        }

        return new KnowledgeSyncResult { Processes = processes, Messages = messages };
    }

    private static string BuildMarkdownFromMessages(string channelId, JsonElement messagesEl)
    {
        var sb = new System.Text.StringBuilder();
        sb.AppendLine($"# Slack channel {channelId}");
        foreach (var msg in messagesEl.EnumerateArray())
        {
            if (!msg.TryGetProperty("text", out var textEl))
                continue;
            var text = textEl.GetString();
            if (string.IsNullOrWhiteSpace(text))
                continue;
            sb.AppendLine(text.Trim());
            sb.AppendLine();
        }
        return sb.ToString();
    }
}
