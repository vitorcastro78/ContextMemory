using System.Net.Http.Headers;
using System.Net.Http.Json;
using ContextMemory.Admin.UI.Models;
using ContextMemory.Core.Models;

namespace ContextMemory.Admin.UI.Services;

public sealed class AdminApiClient
{
    private readonly HttpClient _http;
    private readonly AdminSession _session;

    public AdminApiClient(HttpClient http, AdminSession session)
    {
        _http = http;
        _session = session;
    }

    public async Task<IReadOnlyList<AdminAppListItem>> GetAppsAsync(CancellationToken cancellationToken = default)
    {
        var items = await GetAsync<List<AdminAppListItem>>("/admin/apps", cancellationToken).ConfigureAwait(false);
        return items ?? [];
    }

    public Task<AppStatsResponse?> GetAppStatsAsync(string appId, CancellationToken cancellationToken = default) =>
        GetAsync<AppStatsResponse>($"/admin/apps/{Uri.EscapeDataString(appId)}/stats", cancellationToken);

    public Task<AppCredentialsDto?> GetAppCredentialsAsync(string appId, CancellationToken cancellationToken = default) =>
        GetAsync<AppCredentialsDto>($"/admin/apps/{Uri.EscapeDataString(appId)}/credentials", cancellationToken);

    public async Task<AppCredentialsDto> RotateAppApiKeyAsync(string appId, CancellationToken cancellationToken = default)
    {
        using var request = CreateRequest(HttpMethod.Post, $"/admin/apps/{Uri.EscapeDataString(appId)}/rotate-api-key");
        using var response = await _http.SendAsync(request, cancellationToken).ConfigureAwait(false);
        await EnsureSuccessAsync(response).ConfigureAwait(false);
        var result = await response.Content.ReadFromJsonAsync<AppCredentialsDto>(cancellationToken)
            .ConfigureAwait(false);
        return result ?? throw new InvalidOperationException("Empty response from rotate-api-key.");
    }

    public async Task<IReadOnlyList<UserAdminSummaryDto>> GetUsersAsync(
        string appId,
        CancellationToken cancellationToken = default)
    {
        var items = await GetAsync<List<UserAdminSummaryDto>>(
            $"/admin/apps/{Uri.EscapeDataString(appId)}/users",
            cancellationToken).ConfigureAwait(false);
        return items ?? [];
    }

    public Task<UserAdminDetailDto?> GetUserAsync(
        string appId,
        string userId,
        CancellationToken cancellationToken = default) =>
        GetAsync<UserAdminDetailDto>(
            $"/admin/apps/{Uri.EscapeDataString(appId)}/users/{Uri.EscapeDataString(userId)}",
            cancellationToken);

    public async Task DeleteUserMemoryAsync(
        string appId,
        string userId,
        CancellationToken cancellationToken = default)
    {
        using var request = CreateRequest(HttpMethod.Delete,
            $"/admin/apps/{Uri.EscapeDataString(appId)}/users/{Uri.EscapeDataString(userId)}/memory");
        using var response = await _http.SendAsync(request, cancellationToken).ConfigureAwait(false);
        await EnsureSuccessAsync(response).ConfigureAwait(false);
    }

    public async Task<AppConfigFile?> PatchConfigAsync(
        string appId,
        AppConfigPatchRequest patch,
        CancellationToken cancellationToken = default)
    {
        using var request = CreateRequest(HttpMethod.Patch,
            $"/admin/apps/{Uri.EscapeDataString(appId)}/config");
        request.Content = JsonContent.Create(patch);
        using var response = await _http.SendAsync(request, cancellationToken).ConfigureAwait(false);
        await EnsureSuccessAsync(response).ConfigureAwait(false);
        return await response.Content.ReadFromJsonAsync<AppConfigFile>(cancellationToken).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<AuditLogEntry>> GetAuditAsync(
        string appId,
        CancellationToken cancellationToken = default)
    {
        var items = await GetAsync<List<AuditLogEntry>>(
            $"/admin/apps/{Uri.EscapeDataString(appId)}/audit",
            cancellationToken).ConfigureAwait(false);
        return items ?? [];
    }

    public async Task<RegisterAppResponse> RegisterAppAsync(
        RegisterAppRequest request,
        CancellationToken cancellationToken = default)
    {
        using var httpRequest = CreateRequest(HttpMethod.Post, "/apps/register");
        httpRequest.Content = JsonContent.Create(request);
        using var response = await _http.SendAsync(httpRequest, cancellationToken).ConfigureAwait(false);
        await EnsureSuccessAsync(response).ConfigureAwait(false);
        var result = await response.Content.ReadFromJsonAsync<RegisterAppResponse>(cancellationToken)
            .ConfigureAwait(false);
        return result ?? throw new InvalidOperationException("Empty response from register endpoint.");
    }

    private async Task<T?> GetAsync<T>(string path, CancellationToken cancellationToken)
    {
        using var request = CreateRequest(HttpMethod.Get, path);
        using var response = await _http.SendAsync(request, cancellationToken).ConfigureAwait(false);
        await EnsureSuccessAsync(response).ConfigureAwait(false);
        return await response.Content.ReadFromJsonAsync<T>(cancellationToken).ConfigureAwait(false);
    }

    private HttpRequestMessage CreateRequest(HttpMethod method, string path)
    {
        if (!_session.IsConfigured)
            throw new InvalidOperationException("Configure API URL and master key in Settings.");

        var baseUrl = _session.Settings.ApiBaseUrl.TrimEnd('/');
        var request = new HttpRequestMessage(method, $"{baseUrl}{path}");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _session.Settings.MasterKey);
        return request;
    }

    private static async Task EnsureSuccessAsync(HttpResponseMessage response)
    {
        if (response.IsSuccessStatusCode)
            return;

        var body = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
        var message = string.IsNullOrWhiteSpace(body)
            ? response.ReasonPhrase ?? "Request failed."
            : body;
        throw new AdminApiException((int)response.StatusCode, message);
    }
}
