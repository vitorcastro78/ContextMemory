using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using ContextMemory.Core.CompanyBrain;
using ContextMemory.Core.Configuration;
using ContextMemory.Core.Models;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace ContextMemory.Api.Tests;

public class CompanyBrainSharePointOAuthTests
{
    [Fact]
    public void StartAuthorization_WhenConfigured_ReturnsAuthorizeUrl()
    {
        var store = new OAuthTestStore();
        store.RegisterCompany(new CompanyProfile
        {
            CompanyId = "acme",
            Name = "Acme",
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow
        });
        store.UpsertKnowledgeSource(new KnowledgeSource
        {
            SourceId = "sp-docs",
            CompanyId = "acme",
            Type = KnowledgeSourceType.SharePoint,
            DisplayName = "SharePoint",
            Settings = new Dictionary<string, string>
            {
                ["siteUrl"] = "https://contoso.sharepoint.com/sites/Team",
                ["folderPath"] = "/sites/Team/Shared Documents"
            }
        });

        var options = Options.Create(new ContextMemoryOptions
        {
            DataPath = "data",
            ContentRootPath = Path.GetTempPath(),
            SharePointOAuth = new SharePointOAuthOptions
            {
                Enabled = true,
                ClientId = "client-id",
                ClientSecret = "client-secret",
                TenantId = "common",
                RedirectUri = "http://localhost:5100/companies/sharepoint/oauth/callback"
            }
        });

        var service = new SharePointOAuthService(
            new StubHttpClientFactory(),
            new SharePointOAuthStateStore(options),
            store,
            options,
            NullLogger<SharePointOAuthService>.Instance);

        var start = service.StartAuthorization("acme", "sp-docs");

        Assert.Contains("login.microsoftonline.com", start.AuthorizationUrl);
        Assert.Contains("client-id", start.AuthorizationUrl);
        Assert.False(string.IsNullOrWhiteSpace(start.State));
    }

    [Fact]
    public async Task GlobalDashboard_And_OAuthStatus_EndpointsWork()
    {
        using var factory = new ContextMemoryWebApplicationFactory();
        using var client = factory.CreateClient();
        var companyId = $"sp-{Guid.NewGuid():N}"[..14];

        using (var register = MasterKeyRequest(HttpMethod.Post, "/companies/register",
                   JsonContent.Create(new RegisterCompanyRequest { CompanyId = companyId, Name = "SP Co" })))
        {
            await client.SendAsync(register);
        }

        using (var addSource = MasterKeyRequest(HttpMethod.Post, $"/companies/{companyId}/sources",
                   JsonContent.Create(new AddKnowledgeSourceRequest
                   {
                       SourceId = "sp1",
                       DisplayName = "SP",
                       Type = KnowledgeSourceType.SharePoint,
                       Settings = new Dictionary<string, string>
                       {
                           ["siteUrl"] = "https://contoso.sharepoint.com/sites/Team",
                           ["folderPath"] = "/sites/Team/Shared Documents"
                       }
                   })))
        {
            await client.SendAsync(addSource);
        }

        using var statusRequest = MasterKeyRequest(
            HttpMethod.Get,
            $"/companies/{companyId}/sources/sp1/sharepoint/oauth/status");
        var statusResponse = await client.SendAsync(statusRequest);
        Assert.Equal(HttpStatusCode.OK, statusResponse.StatusCode);
        var status = await statusResponse.Content.ReadFromJsonAsync<SharePointOAuthStatus>();
        Assert.NotNull(status);
        Assert.False(status!.Connected);
    }

    private static HttpRequestMessage MasterKeyRequest(HttpMethod method, string path, HttpContent? content = null)
    {
        var request = new HttpRequestMessage(method, path) { Content = content };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", "test-master-key");
        return request;
    }

    private sealed class StubHttpClientFactory : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new();
    }

    private sealed class OAuthTestStore : ContextMemory.Core.Contracts.ICompanyBrainStore
    {
        private readonly Dictionary<string, CompanyProfile> _companies = new(StringComparer.Ordinal);
        private readonly List<KnowledgeSource> _sources = [];

        public bool TryGetCompany(string companyId, out CompanyProfile? company) =>
            _companies.TryGetValue(companyId, out company);

        public IReadOnlyList<CompanyProfile> ListCompanies() => _companies.Values.ToList();

        public bool RegisterCompany(CompanyProfile company)
        {
            _companies[company.CompanyId] = company;
            return true;
        }
        public bool UpsertProcess(CompanyProcess process) => true;
        public IReadOnlyList<CompanyProcess> ListProcesses(string companyId) => [];
        public bool TryGetProcess(string companyId, string processId, out CompanyProcess? process) { process = null; return false; }
        public bool UpsertKnowledgeSource(KnowledgeSource source)
        {
            _sources.RemoveAll(s => s.SourceId == source.SourceId && s.CompanyId == source.CompanyId);
            _sources.Add(source);
            return true;
        }
        public IReadOnlyList<KnowledgeSource> ListKnowledgeSources(string companyId) =>
            _sources.Where(s => s.CompanyId == companyId).ToList();
        public bool LinkApp(string companyId, string appId) => true;
        public bool UnlinkApp(string companyId, string appId) => true;
        public bool TryGetCompanyForApp(string appId, out string? companyId) { companyId = null; return false; }
        public IReadOnlyList<string> ListLinkedApps(string companyId) => [];
        public void SaveSkillsCache(string companyId, CompanySkillsFile skillsFile) { }
        public bool TryGetSkillsCache(string companyId, out CompanySkillsFile? skillsFile) { skillsFile = null; return false; }
        public bool TryGetWebhookSecret(string companyId, out string? secret) { secret = null; return false; }
        public string SetWebhookSecret(string companyId, string? secret = null) => secret ?? "s";
    }
}
