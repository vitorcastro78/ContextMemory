using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using ContextMemory.Core.CompanyBrain;
using ContextMemory.Core.Models;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace ContextMemory.Api.Tests;

public class CompanyBrainMcpSseIntegrationTests : IClassFixture<ContextMemoryWebApplicationFactory>
{
    private readonly HttpClient _client;
    private readonly ContextMemoryWebApplicationFactory _factory;

    public CompanyBrainMcpSseIntegrationTests(ContextMemoryWebApplicationFactory factory)
    {
        _factory = factory;
        _client = factory.CreateClient();
    }

    [Fact]
    public async Task McpSse_SessionReceivesResponseViaMessageEndpoint()
    {
        var companyId = $"sse-{Guid.NewGuid():N}"[..14];
        var hub = _factory.Services.GetRequiredService<McpSseHub>();
        var sessionId = hub.CreateSession(companyId);

        using (var register = MasterKeyRequest(HttpMethod.Post, "/companies/register",
                   JsonContent.Create(new RegisterCompanyRequest { CompanyId = companyId, Name = "SSE Co" })))
        {
            await _client.SendAsync(register);
        }

        WebhookSecretInfo info;
        using (var rotate = MasterKeyRequest(HttpMethod.Post, $"/companies/{companyId}/webhook/rotate"))
        {
            info = (await (await _client.SendAsync(rotate)).Content.ReadFromJsonAsync<WebhookSecretInfo>())!;
        }

        using var mcp = new HttpRequestMessage(HttpMethod.Post, $"/companies/{companyId}/mcp/messages?sessionId={sessionId}")
        {
            Content = JsonContent.Create(new JsonRpcRequest { Id = 1, Method = "tools/list" })
        };
        mcp.Headers.Authorization = new AuthenticationHeaderValue("Bearer", info.WebhookSecret);

        var response = await _client.SendAsync(mcp);
        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);

        Assert.True(hub.TryGetSession(sessionId, out var session));
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(2));
        var message = await session!.Channel.Reader.ReadAsync(cts.Token);
        Assert.Null(message.Error);
        Assert.NotNull(message.Result);
    }

    [Fact]
    public async Task ImportFromDisk_ReturnsResult()
    {
        using var request = MasterKeyRequest(HttpMethod.Post, "/companies/import-from-disk");
        var response = await _client.SendAsync(request);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var result = await response.Content.ReadFromJsonAsync<CompanyImportResult>();
        Assert.NotNull(result);
    }

    private HttpRequestMessage MasterKeyRequest(HttpMethod method, string path, HttpContent? content = null)
    {
        var request = new HttpRequestMessage(method, path) { Content = content };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", "test-master-key");
        return request;
    }
}
