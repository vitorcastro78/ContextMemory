using System.Net.Http.Headers;
using System.Text.Json;
using ContextMemory.Core.Contracts;
using ContextMemory.Core.Models;
using Microsoft.Extensions.Http;

namespace ContextMemory.Core.CompanyBrain.Connectors;

public sealed class GoogleDriveFolderConnector : IKnowledgeSourceConnector
{
    private readonly HttpClient _http;

    public GoogleDriveFolderConnector(IHttpClientFactory httpClientFactory)
    {
        _http = httpClientFactory.CreateClient(nameof(GoogleDriveFolderConnector));
        _http.Timeout = TimeSpan.FromSeconds(90);
    }

    public KnowledgeSourceType SourceType => KnowledgeSourceType.GoogleDrive;

    public async Task<KnowledgeSyncResult> SyncAsync(KnowledgeSource source, CancellationToken cancellationToken = default)
    {
        if (!TryGetSetting(source, "folderId", out var folderId)
            || !TryGetSetting(source, "token", out var token))
        {
            return new KnowledgeSyncResult
            {
                Messages = ["Google Drive source requires settings: folderId, token (OAuth access token)."]
            };
        }

        var processes = new List<CompanyProcess>();
        var messages = new List<string>();

        try
        {
            await CollectFilesAsync(
                folderId.Trim(),
                token.Trim(),
                source,
                processes,
                messages,
                cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            return new KnowledgeSyncResult { Messages = [$"Google Drive sync failed: {ex.Message}"] };
        }

        return new KnowledgeSyncResult
        {
            Processes = processes,
            Messages = messages
        };
    }

    private async Task CollectFilesAsync(
        string folderId,
        string token,
        KnowledgeSource source,
        List<CompanyProcess> processes,
        List<string> messages,
        CancellationToken cancellationToken)
    {
        var query = Uri.EscapeDataString($"'{folderId}' in parents and trashed = false");
        var url = $"https://www.googleapis.com/drive/v3/files?q={query}&fields=files(id,name,mimeType)&pageSize=100";

        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

        using var response = await _http.SendAsync(request, cancellationToken).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
        {
            var body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            messages.Add($"Google Drive API {(int)response.StatusCode}: {body[..Math.Min(body.Length, 200)]}");
            return;
        }

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken).ConfigureAwait(false);

        if (!doc.RootElement.TryGetProperty("files", out var files))
            return;

        foreach (var file in files.EnumerateArray())
        {
            var name = file.TryGetProperty("name", out var nameEl) ? nameEl.GetString() ?? string.Empty : string.Empty;
            var mimeType = file.TryGetProperty("mimeType", out var mimeEl) ? mimeEl.GetString() ?? string.Empty : string.Empty;
            if (!file.TryGetProperty("id", out var idEl))
                continue;

            var fileId = idEl.GetString();
            if (string.IsNullOrWhiteSpace(fileId))
                continue;

            if (string.Equals(mimeType, "application/vnd.google-apps.folder", StringComparison.Ordinal))
            {
                await CollectFilesAsync(fileId, token, source, processes, messages, cancellationToken)
                    .ConfigureAwait(false);
                continue;
            }

            var isMarkdown = name.EndsWith(".md", StringComparison.OrdinalIgnoreCase)
                || string.Equals(mimeType, "text/markdown", StringComparison.Ordinal);

            if (!isMarkdown && !string.Equals(mimeType, "application/vnd.google-apps.document", StringComparison.Ordinal))
                continue;

            var downloadUrl = string.Equals(mimeType, "application/vnd.google-apps.document", StringComparison.Ordinal)
                ? $"https://www.googleapis.com/drive/v3/files/{fileId}/export?mimeType=text/markdown"
                : $"https://www.googleapis.com/drive/v3/files/{fileId}?alt=media";

            using var downloadRequest = new HttpRequestMessage(HttpMethod.Get, downloadUrl);
            downloadRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

            using var downloadResponse = await _http.SendAsync(downloadRequest, cancellationToken).ConfigureAwait(false);
            if (!downloadResponse.IsSuccessStatusCode)
            {
                messages.Add($"Failed to download {name}: {(int)downloadResponse.StatusCode}");
                continue;
            }

            var markdown = await downloadResponse.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            var extracted = MarkdownWikiConnector.ExtractProcesses(source.CompanyId, name, markdown);
            processes.AddRange(extracted);
            if (extracted.Count > 0)
                messages.Add($"Google Drive: extracted {extracted.Count} process(es) from {name}.");
        }
    }

    private static bool TryGetSetting(KnowledgeSource source, string key, out string value)
    {
        if (source.Settings.TryGetValue(key, out value) && !string.IsNullOrWhiteSpace(value))
        {
            value = value.Trim();
            return true;
        }

        value = string.Empty;
        return false;
    }
}
