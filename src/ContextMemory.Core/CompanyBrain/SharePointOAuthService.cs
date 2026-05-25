using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using ContextMemory.Core.Configuration;
using ContextMemory.Core.Contracts;
using ContextMemory.Core.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace ContextMemory.Core.CompanyBrain;

public sealed record SharePointOAuthStartResult
{
    public required string AuthorizationUrl { get; init; }
    public required string State { get; init; }
}

public sealed record SharePointOAuthStatus
{
    public bool Configured { get; init; }
    public bool Connected { get; init; }
    public string? TenantId { get; init; }
}

public sealed class SharePointOAuthService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private readonly HttpClient _http;
    private readonly SharePointOAuthStateStore _stateStore;
    private readonly ICompanyBrainStore _store;
    private readonly SharePointOAuthOptions _options;
    private readonly ILogger<SharePointOAuthService> _logger;

    public SharePointOAuthService(
        IHttpClientFactory httpClientFactory,
        SharePointOAuthStateStore stateStore,
        ICompanyBrainStore store,
        IOptions<ContextMemoryOptions> options,
        ILogger<SharePointOAuthService> logger)
    {
        _http = httpClientFactory.CreateClient(nameof(SharePointOAuthService));
        _stateStore = stateStore;
        _store = store;
        _options = options.Value.SharePointOAuth;
        _logger = logger;
    }

    public bool IsConfigured =>
        _options.Enabled
        && !string.IsNullOrWhiteSpace(_options.ClientId)
        && !string.IsNullOrWhiteSpace(_options.ClientSecret)
        && !string.IsNullOrWhiteSpace(_options.RedirectUri);

    public SharePointOAuthStartResult StartAuthorization(string companyId, string sourceId)
    {
        EnsureSharePointSource(companyId, sourceId);

        if (!IsConfigured)
            throw new InvalidOperationException("SharePoint OAuth is not configured on the server.");

        var state = Guid.NewGuid().ToString("N");
        _stateStore.Save(new SharePointOAuthPendingState
        {
            State = state,
            CompanyId = companyId,
            SourceId = sourceId,
            CreatedAt = DateTimeOffset.UtcNow
        });

        var query = new StringBuilder();
        AppendQuery(query, "client_id", _options.ClientId);
        AppendQuery(query, "response_type", "code");
        AppendQuery(query, "redirect_uri", _options.RedirectUri);
        AppendQuery(query, "response_mode", "query");
        AppendQuery(query, "scope", _options.Scopes);
        AppendQuery(query, "state", state);

        var authorizeUrl =
            $"https://login.microsoftonline.com/{Uri.EscapeDataString(_options.TenantId)}/oauth2/v2.0/authorize?{query}";

        return new SharePointOAuthStartResult
        {
            AuthorizationUrl = authorizeUrl,
            State = state
        };
    }

    public async Task<string> CompleteAuthorizationAsync(
        string code,
        string state,
        CancellationToken cancellationToken = default)
    {
        if (!_stateStore.TryTake(state, out var pending) || pending is null)
            throw new InvalidOperationException("Invalid or expired OAuth state.");

        var token = await ExchangeCodeAsync(code, cancellationToken).ConfigureAwait(false);
        await SaveTokensToSourceAsync(pending.CompanyId, pending.SourceId, token, cancellationToken)
            .ConfigureAwait(false);

        return $"{_options.AdminRedirectBase.TrimEnd('/')}/companies/{pending.CompanyId}?sharepoint=connected&sourceId={pending.SourceId}";
    }

    public async Task<string> ResolveAccessTokenAsync(
        KnowledgeSource source,
        CancellationToken cancellationToken = default)
    {
        if (source.Settings.TryGetValue("accessToken", out var accessToken)
            && !string.IsNullOrWhiteSpace(accessToken))
            return accessToken.Trim();

        if (!source.Settings.TryGetValue("refreshToken", out var refreshToken)
            || string.IsNullOrWhiteSpace(refreshToken))
            throw new InvalidOperationException(
                "SharePoint source requires accessToken or refreshToken. Connect via OAuth first.");

        if (!IsConfigured)
            throw new InvalidOperationException("SharePoint OAuth is not configured on the server.");

        var refreshed = await RefreshAccessTokenAsync(refreshToken.Trim(), cancellationToken)
            .ConfigureAwait(false);

        await SaveTokensToSourceAsync(source.CompanyId, source.SourceId, refreshed, cancellationToken)
            .ConfigureAwait(false);

        return refreshed.AccessToken;
    }

    public SharePointOAuthStatus GetSourceStatus(string companyId, string sourceId)
    {
        var source = GetSharePointSource(companyId, sourceId);
        var connected = source.Settings.ContainsKey("refreshToken")
            || source.Settings.ContainsKey("accessToken");

        return new SharePointOAuthStatus
        {
            Configured = IsConfigured,
            Connected = connected,
            TenantId = _options.TenantId
        };
    }

    private async Task<SharePointTokenResponse> ExchangeCodeAsync(
        string code,
        CancellationToken cancellationToken)
    {
        using var request = BuildTokenRequest(new Dictionary<string, string>
        {
            ["grant_type"] = "authorization_code",
            ["code"] = code,
            ["redirect_uri"] = _options.RedirectUri
        });

        return await SendTokenRequestAsync(request, cancellationToken).ConfigureAwait(false);
    }

    private async Task<SharePointTokenResponse> RefreshAccessTokenAsync(
        string refreshToken,
        CancellationToken cancellationToken)
    {
        using var request = BuildTokenRequest(new Dictionary<string, string>
        {
            ["grant_type"] = "refresh_token",
            ["refresh_token"] = refreshToken
        });

        return await SendTokenRequestAsync(request, cancellationToken).ConfigureAwait(false);
    }

    private HttpRequestMessage BuildTokenRequest(IReadOnlyDictionary<string, string> fields)
    {
        var content = new FormUrlEncodedContent(fields);
        var url =
            $"https://login.microsoftonline.com/{Uri.EscapeDataString(_options.TenantId)}/oauth2/v2.0/token";

        var request = new HttpRequestMessage(HttpMethod.Post, url) { Content = content };
        var credentials = Convert.ToBase64String(
            System.Text.Encoding.UTF8.GetBytes($"{_options.ClientId}:{_options.ClientSecret}"));
        request.Headers.Authorization = new AuthenticationHeaderValue("Basic", credentials);
        return request;
    }

    private async Task<SharePointTokenResponse> SendTokenRequestAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        using var response = await _http.SendAsync(request, cancellationToken).ConfigureAwait(false);
        var body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);

        if (!response.IsSuccessStatusCode)
        {
            _logger.LogWarning("SharePoint token request failed: {Status} {Body}", (int)response.StatusCode, body);
            throw new InvalidOperationException($"SharePoint token request failed: {(int)response.StatusCode}");
        }

        var token = JsonSerializer.Deserialize<SharePointTokenResponse>(body, JsonOptions)
            ?? throw new InvalidOperationException("Invalid token response from Microsoft.");

        if (string.IsNullOrWhiteSpace(token.AccessToken))
            throw new InvalidOperationException("Token response did not include access_token.");

        return token;
    }

    private async Task SaveTokensToSourceAsync(
        string companyId,
        string sourceId,
        SharePointTokenResponse token,
        CancellationToken cancellationToken)
    {
        var source = GetSharePointSource(companyId, sourceId);
        var settings = new Dictionary<string, string>(source.Settings, StringComparer.OrdinalIgnoreCase)
        {
            ["accessToken"] = token.AccessToken
        };

        if (!string.IsNullOrWhiteSpace(token.RefreshToken))
            settings["refreshToken"] = token.RefreshToken;

        if (token.ExpiresIn > 0)
        {
            var expiresAt = DateTimeOffset.UtcNow.AddSeconds(token.ExpiresIn);
            settings["tokenExpiresAt"] = expiresAt.ToString("O");
        }

        _store.UpsertKnowledgeSource(source with { Settings = settings });
        await Task.CompletedTask.ConfigureAwait(false);
    }

    private KnowledgeSource GetSharePointSource(string companyId, string sourceId)
    {
        EnsureSharePointSource(companyId, sourceId);
        return _store.ListKnowledgeSources(companyId)
            .First(s => string.Equals(s.SourceId, sourceId, StringComparison.Ordinal));
    }

    private void EnsureSharePointSource(string companyId, string sourceId)
    {
        if (!_store.TryGetCompany(companyId, out _))
            throw new InvalidOperationException($"Company '{companyId}' not found.");

        var source = _store.ListKnowledgeSources(companyId)
            .FirstOrDefault(s => string.Equals(s.SourceId, sourceId, StringComparison.Ordinal));

        if (source is null)
            throw new InvalidOperationException($"Knowledge source '{sourceId}' not found.");

        if (source.Type != KnowledgeSourceType.SharePoint)
            throw new InvalidOperationException($"Source '{sourceId}' is not a SharePoint source.");
    }

    private static void AppendQuery(StringBuilder query, string key, string value)
    {
        if (query.Length > 0)
            query.Append('&');
        query.Append(Uri.EscapeDataString(key));
        query.Append('=');
        query.Append(Uri.EscapeDataString(value));
    }

    private sealed class SharePointTokenResponse
    {
        [JsonPropertyName("access_token")]
        public string AccessToken { get; set; } = string.Empty;

        [JsonPropertyName("refresh_token")]
        public string? RefreshToken { get; set; }

        [JsonPropertyName("expires_in")]
        public int ExpiresIn { get; set; }
    }
}
