using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using ContextMemory.Core.Models;
using ContextMemory.Core.Security;
using Xunit;

namespace ContextMemory.Api.Tests;

public class CompanyBrainWebhookMcpIntegrationTests : IClassFixture<ContextMemoryWebApplicationFactory>
{
    private readonly HttpClient _client;

    public CompanyBrainWebhookMcpIntegrationTests(ContextMemoryWebApplicationFactory factory) =>
        _client = factory.CreateClient();

    [Fact]
    public async Task Webhook_WithValidSignature_IngestsProcess()
    {
        var companyId = $"wh-{Guid.NewGuid():N}"[..14];

        using (var register = MasterKeyRequest(HttpMethod.Post, "/companies/register",
                   JsonContent.Create(new RegisterCompanyRequest { CompanyId = companyId, Name = "Webhook Co" })))
        {
            Assert.Equal(HttpStatusCode.Created, (await _client.SendAsync(register)).StatusCode);
        }

        string secret;
        using (var rotate = MasterKeyRequest(HttpMethod.Post, $"/companies/{companyId}/webhook/rotate"))
        {
            var rotateResponse = await _client.SendAsync(rotate);
            var info = await rotateResponse.Content.ReadFromJsonAsync<WebhookSecretInfo>();
            secret = info!.WebhookSecret;
        }

        const string body = """
            {"format":"markdown","content":"## Process: Webhook Flow\n1. Receive event.\n2. Apply policy."}
            """;
        var signature = CompanyWebhookAuth.ComputeSignature(secret, body);

        using var webhook = new HttpRequestMessage(HttpMethod.Post, $"/companies/{companyId}/webhook")
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json")
        };
        webhook.Headers.Add(CompanyWebhookAuth.SignatureHeader, signature);

        var webhookResponse = await _client.SendAsync(webhook);
        Assert.Equal(HttpStatusCode.Created, webhookResponse.StatusCode);
    }

    [Fact]
    public async Task Mcp_WithWebhookSecret_ListsTools()
    {
        var companyId = $"mcp-{Guid.NewGuid():N}"[..14];

        using (var register = MasterKeyRequest(HttpMethod.Post, "/companies/register",
                   JsonContent.Create(new RegisterCompanyRequest { CompanyId = companyId, Name = "MCP Co" })))
        {
            await _client.SendAsync(register);
        }

        using (var upsert = MasterKeyRequest(HttpMethod.Post, $"/companies/{companyId}/processes",
                   JsonContent.Create(new UpsertProcessRequest
                   {
                       ProcessId = "support-flow",
                       Title = "Support Flow",
                       Steps = [new ProcessStep { Order = 1, Action = "Acknowledge ticket." }]
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
            Content = JsonContent.Create(new JsonRpcRequest { Id = 1, Method = "tools/list" })
        };
        mcp.Headers.Authorization = new AuthenticationHeaderValue("Bearer", info.WebhookSecret);

        var mcpResponse = await _client.SendAsync(mcp);
        Assert.Equal(HttpStatusCode.OK, mcpResponse.StatusCode);
        var json = await mcpResponse.Content.ReadAsStringAsync();
        Assert.Contains("process_support_flow", json);
    }

    private HttpRequestMessage MasterKeyRequest(HttpMethod method, string path, HttpContent? content = null)
    {
        var request = new HttpRequestMessage(method, path) { Content = content };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", "test-master-key");
        return request;
    }
}
