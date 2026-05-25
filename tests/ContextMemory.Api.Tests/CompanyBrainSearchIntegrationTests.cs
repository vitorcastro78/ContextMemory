using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using ContextMemory.Core.Models;
using Xunit;

namespace ContextMemory.Api.Tests;

public class CompanyBrainSearchIntegrationTests : IClassFixture<ContextMemoryWebApplicationFactory>
{
    private readonly HttpClient _client;

    public CompanyBrainSearchIntegrationTests(ContextMemoryWebApplicationFactory factory) =>
        _client = factory.CreateClient();

    [Fact]
    public async Task SearchProcesses_ReturnsMatches()
    {
        var companyId = $"search-{Guid.NewGuid():N}"[..16];

        using (var register = MasterKeyRequest(HttpMethod.Post, "/companies/register",
                   JsonContent.Create(new RegisterCompanyRequest { CompanyId = companyId, Name = "Search Co" })))
        {
            await _client.SendAsync(register);
        }

        using (var upsert = MasterKeyRequest(HttpMethod.Post, $"/companies/{companyId}/processes",
                   JsonContent.Create(new UpsertProcessRequest
                   {
                       ProcessId = "refund-flow",
                       Title = "Refund Exception",
                       Triggers = ["refund", "reembolso"],
                       Steps = [new ProcessStep { Order = 1, Action = "Validate ticket." }]
                   })))
        {
            await _client.SendAsync(upsert);
        }

        using var search = MasterKeyRequest(
            HttpMethod.Post,
            $"/companies/{companyId}/processes/search",
            JsonContent.Create(new ProcessSearchRequest { Query = "refund", TopK = 3 }));

        var response = await _client.SendAsync(search);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var result = await response.Content.ReadFromJsonAsync<ProcessSearchResponse>();
        Assert.NotNull(result);
        Assert.Single(result!.Processes);
        Assert.Equal("refund-flow", result.Processes[0].ProcessId);
    }

    [Fact]
    public async Task Mcp_ResourcesList_ReturnsSkillsResource()
    {
        var companyId = $"res-{Guid.NewGuid():N}"[..14];

        using (var register = MasterKeyRequest(HttpMethod.Post, "/companies/register",
                   JsonContent.Create(new RegisterCompanyRequest { CompanyId = companyId, Name = "Res Co" })))
        {
            await _client.SendAsync(register);
        }

        using (var upsert = MasterKeyRequest(HttpMethod.Post, $"/companies/{companyId}/processes",
                   JsonContent.Create(new UpsertProcessRequest
                   {
                       ProcessId = "support",
                       Title = "Support Flow",
                       Steps = [new ProcessStep { Order = 1, Action = "Acknowledge." }]
                   })))
        {
            await _client.SendAsync(upsert);
        }

        WebhookSecretInfo info;
        using (var rotate = MasterKeyRequest(HttpMethod.Post, $"/companies/{companyId}/webhook/rotate"))
        {
            info = (await (await _client.SendAsync(rotate)).Content.ReadFromJsonAsync<WebhookSecretInfo>())!;
        }

        using var mcp = new HttpRequestMessage(HttpMethod.Post, $"/companies/{companyId}/mcp")
        {
            Content = JsonContent.Create(new JsonRpcRequest { Id = 1, Method = "resources/list" })
        };
        mcp.Headers.Authorization = new AuthenticationHeaderValue("Bearer", info.WebhookSecret);

        var response = await _client.SendAsync(mcp);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        Assert.Contains("skills.yaml", body);
    }

    private HttpRequestMessage MasterKeyRequest(HttpMethod method, string path, HttpContent? content = null)
    {
        var request = new HttpRequestMessage(method, path) { Content = content };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", "test-master-key");
        return request;
    }
}
