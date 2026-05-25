using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using ContextMemory.Core.Contracts;
using ContextMemory.Core.Models;
using Microsoft.Extensions.Http;

namespace ContextMemory.Core.CompanyBrain.Connectors;

public sealed class GitHubMarkdownConnector : IKnowledgeSourceConnector
{
    private readonly HttpClient _http;

    public GitHubMarkdownConnector(IHttpClientFactory httpClientFactory)
    {
        _http = httpClientFactory.CreateClient(nameof(GitHubMarkdownConnector));
        _http.DefaultRequestHeaders.UserAgent.ParseAdd("ContextMemory-CompanyBrain/1.0");
        _http.Timeout = TimeSpan.FromSeconds(90);
    }

    public KnowledgeSourceType SourceType => KnowledgeSourceType.GitHub;

    public async Task<KnowledgeSyncResult> SyncAsync(KnowledgeSource source, CancellationToken cancellationToken = default)
    {
        if (!TryGetSetting(source, "owner", out var owner)
            || !TryGetSetting(source, "repo", out var repo))
        {
            return new KnowledgeSyncResult
            {
                Messages = ["GitHub source requires settings: owner, repo. Optional: path, branch, token."]
            };
        }

        source.Settings.TryGetValue("path", out var path);
        path ??= string.Empty;
        source.Settings.TryGetValue("branch", out var branch);
        branch = string.IsNullOrWhiteSpace(branch) ? "main" : branch.Trim();

        var processes = new List<CompanyProcess>();
        var messages = new List<string>();

        try
        {
            await CollectMarkdownAsync(
                owner.Trim(),
                repo.Trim(),
                path.Trim(),
                branch,
                source,
                processes,
                messages,
                cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            return new KnowledgeSyncResult { Messages = [$"GitHub sync failed: {ex.Message}"] };
        }

        return new KnowledgeSyncResult
        {
            Processes = processes,
            Messages = messages
        };
    }

    private async Task CollectMarkdownAsync(
        string owner,
        string repo,
        string path,
        string branch,
        KnowledgeSource source,
        List<CompanyProcess> processes,
        List<string> messages,
        CancellationToken cancellationToken)
    {
        var url = string.IsNullOrWhiteSpace(path)
            ? $"https://api.github.com/repos/{owner}/{repo}/contents?ref={Uri.EscapeDataString(branch)}"
            : $"https://api.github.com/repos/{owner}/{repo}/contents/{path.Trim('/')}?ref={Uri.EscapeDataString(branch)}";

        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        ApplyAuth(request, source);

        using var response = await _http.SendAsync(request, cancellationToken).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
        {
            var body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            messages.Add($"GitHub API {(int)response.StatusCode}: {body[..Math.Min(body.Length, 200)]}");
            return;
        }

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken).ConfigureAwait(false);

        if (doc.RootElement.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in doc.RootElement.EnumerateArray())
                await ProcessGitHubItemAsync(item, owner, repo, branch, source, processes, messages, cancellationToken)
                    .ConfigureAwait(false);
        }
        else
        {
            await ProcessGitHubItemAsync(doc.RootElement, owner, repo, branch, source, processes, messages, cancellationToken)
                .ConfigureAwait(false);
        }
    }

    private async Task ProcessGitHubItemAsync(
        JsonElement item,
        string owner,
        string repo,
        string branch,
        KnowledgeSource source,
        List<CompanyProcess> processes,
        List<string> messages,
        CancellationToken cancellationToken)
    {
        var type = item.TryGetProperty("type", out var typeEl) ? typeEl.GetString() : null;
        var itemPath = item.TryGetProperty("path", out var pathEl) ? pathEl.GetString() ?? string.Empty : string.Empty;

        if (string.Equals(type, "dir", StringComparison.OrdinalIgnoreCase))
        {
            await CollectMarkdownAsync(owner, repo, itemPath, branch, source, processes, messages, cancellationToken)
                .ConfigureAwait(false);
            return;
        }

        if (!string.Equals(type, "file", StringComparison.OrdinalIgnoreCase)
            || !itemPath.EndsWith(".md", StringComparison.OrdinalIgnoreCase))
            return;

        if (!item.TryGetProperty("download_url", out var downloadEl))
            return;

        var downloadUrl = downloadEl.GetString();
        if (string.IsNullOrWhiteSpace(downloadUrl))
            return;

        using var request = new HttpRequestMessage(HttpMethod.Get, downloadUrl);
        ApplyAuth(request, source);

        using var response = await _http.SendAsync(request, cancellationToken).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
        {
            messages.Add($"Failed to download {itemPath}: {(int)response.StatusCode}");
            return;
        }

        var markdown = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        var extracted = MarkdownWikiConnector.ExtractProcesses(source.CompanyId, itemPath, markdown);
        processes.AddRange(extracted);
        if (extracted.Count > 0)
            messages.Add($"GitHub: extracted {extracted.Count} process(es) from {itemPath}.");
    }

    private static void ApplyAuth(HttpRequestMessage request, KnowledgeSource source)
    {
        if (source.Settings.TryGetValue("token", out var token) && !string.IsNullOrWhiteSpace(token))
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token.Trim());
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
