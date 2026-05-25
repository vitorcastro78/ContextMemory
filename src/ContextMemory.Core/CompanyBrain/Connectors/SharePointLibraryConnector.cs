using System.Net.Http.Headers;
using System.Text.Json;
using ContextMemory.Core.Contracts;
using ContextMemory.Core.Models;
using Microsoft.Extensions.Http;

namespace ContextMemory.Core.CompanyBrain.Connectors;

public sealed class SharePointLibraryConnector : IKnowledgeSourceConnector
{
    private readonly HttpClient _http;
    private readonly SharePointOAuthService _oauth;

    public SharePointLibraryConnector(
        IHttpClientFactory httpClientFactory,
        SharePointOAuthService oauth)
    {
        _http = httpClientFactory.CreateClient(nameof(SharePointLibraryConnector));
        _http.Timeout = TimeSpan.FromSeconds(90);
        _oauth = oauth;
    }

    public KnowledgeSourceType SourceType => KnowledgeSourceType.SharePoint;

    public async Task<KnowledgeSyncResult> SyncAsync(KnowledgeSource source, CancellationToken cancellationToken = default)
    {
        if (!TryGetSetting(source, "siteUrl", out var siteUrl)
            || !TryGetSetting(source, "folderPath", out var folderPath))
        {
            return new KnowledgeSyncResult
            {
                Messages = ["SharePoint source requires settings: siteUrl, folderPath, and OAuth or accessToken."]
            };
        }

        string accessToken;
        try
        {
            accessToken = await _oauth.ResolveAccessTokenAsync(source, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            return new KnowledgeSyncResult
            {
                Messages = [$"SharePoint authentication failed: {ex.Message}"]
            };
        }

        var processes = new List<CompanyProcess>();
        var messages = new List<string>();

        try
        {
            await CollectFolderAsync(
                siteUrl.Trim().TrimEnd('/'),
                accessToken,
                folderPath.Trim(),
                source,
                processes,
                messages,
                cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            return new KnowledgeSyncResult { Messages = [$"SharePoint sync failed: {ex.Message}"] };
        }

        return new KnowledgeSyncResult
        {
            Processes = processes,
            Messages = messages
        };
    }

    private async Task CollectFolderAsync(
        string siteUrl,
        string accessToken,
        string folderPath,
        KnowledgeSource source,
        List<CompanyProcess> processes,
        List<string> messages,
        CancellationToken cancellationToken)
    {
        var encodedPath = Uri.EscapeDataString(folderPath);
        var url = $"{siteUrl}/_api/web/GetFolderByServerRelativeUrl('{encodedPath}')/Files";

        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

        using var response = await _http.SendAsync(request, cancellationToken).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
        {
            var body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            messages.Add($"SharePoint API {(int)response.StatusCode}: {body[..Math.Min(body.Length, 200)]}");
            return;
        }

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken).ConfigureAwait(false);

        if (!doc.RootElement.TryGetProperty("value", out var files))
            return;

        foreach (var file in files.EnumerateArray())
        {
            var name = file.TryGetProperty("Name", out var nameEl) ? nameEl.GetString() ?? string.Empty : string.Empty;
            if (!name.EndsWith(".md", StringComparison.OrdinalIgnoreCase))
                continue;

            if (!file.TryGetProperty("ServerRelativeUrl", out var pathEl))
                continue;

            var serverRelativeUrl = pathEl.GetString();
            if (string.IsNullOrWhiteSpace(serverRelativeUrl))
                continue;

            var contentUrl =
                $"{siteUrl}/_api/web/GetFileByServerRelativeUrl('{Uri.EscapeDataString(serverRelativeUrl)}')/$value";

            using var contentRequest = new HttpRequestMessage(HttpMethod.Get, contentUrl);
            contentRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);

            using var contentResponse = await _http.SendAsync(contentRequest, cancellationToken).ConfigureAwait(false);
            if (!contentResponse.IsSuccessStatusCode)
            {
                messages.Add($"Failed to download {name}: {(int)contentResponse.StatusCode}");
                continue;
            }

            var markdown = await contentResponse.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            var extracted = MarkdownWikiConnector.ExtractProcesses(source.CompanyId, name, markdown);
            processes.AddRange(extracted);
            if (extracted.Count > 0)
                messages.Add($"SharePoint: extracted {extracted.Count} process(es) from {name}.");
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
