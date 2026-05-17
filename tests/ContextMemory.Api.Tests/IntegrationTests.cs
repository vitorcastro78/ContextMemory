using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using ContextMemory.Core.Contracts;
using ContextMemory.Core.Models;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace ContextMemory.Api.Tests;

public class IntegrationTests : IClassFixture<ContextMemoryWebApplicationFactory>
{
    private readonly HttpClient _client;
    private readonly ContextMemoryWebApplicationFactory _factory;

    public IntegrationTests(ContextMemoryWebApplicationFactory factory)
    {
        _factory = factory;
        _client = factory.CreateClient();
    }

    [Fact]
    public async Task Health_ReturnsJsonWithStatus()
    {
        var response = await _client.GetAsync("/health");
        var json = await response.Content.ReadAsStringAsync();

        Assert.Contains("status", json, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("checks", json, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Metrics_ReturnsPrometheusFormat()
    {
        var response = await _client.GetAsync("/metrics");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("text/plain", response.Content.Headers.ContentType?.MediaType ?? "");
    }

    [Fact]
    public async Task Chat_WithoutHeaders_Returns401()
    {
        var response = await _client.PostAsync("/api/chat", JsonContent.Create(new { model = "x", messages = Array.Empty<object>() }));
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Admin_WithoutMasterKey_Returns401()
    {
        var response = await _client.GetAsync("/admin/apps");
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Admin_WithMasterKey_ReturnsApps()
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "/admin/apps");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", "test-master-key");

        var response = await _client.SendAsync(request);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Feedback_RequiresAuth()
    {
        var response = await _client.PostAsJsonAsync("/api/chat/feedback", new { messageId = Guid.NewGuid(), score = 1 });
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task ConversationMemory_IsolatesUsers()
    {
        using var scope = _factory.Services.CreateScope();
        var memory = scope.ServiceProvider.GetRequiredService<IConversationMemory>();

        await memory.AppendAsync(
            "kyc-dev",
            "user-a",
            [new OllamaMessage { Role = "user", Content = "segredo-a" }],
            maxMessages: 20);

        await memory.AppendAsync(
            "kyc-dev",
            "user-b",
            [new OllamaMessage { Role = "user", Content = "segredo-b" }],
            maxMessages: 20);

        var historyA = await memory.GetHistoryAsync("kyc-dev", "user-a", 20);
        var historyB = await memory.GetHistoryAsync("kyc-dev", "user-b", 20);

        Assert.Contains(historyA, m => m.Content.Contains("segredo-a", StringComparison.Ordinal));
        Assert.DoesNotContain(historyA, m => m.Content.Contains("segredo-b", StringComparison.Ordinal));
        Assert.Contains(historyB, m => m.Content.Contains("segredo-b", StringComparison.Ordinal));
        Assert.DoesNotContain(historyB, m => m.Content.Contains("segredo-a", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Chat_RequestBody_IsOllamaCompatibleShape()
    {
        var payload = """
            {
              "model": "llama3.2",
              "messages": [{ "role": "user", "content": "olá" }],
              "stream": false
            }
            """;

        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/chat")
        {
            Content = new StringContent(payload, Encoding.UTF8, "application/json")
        };
        request.Headers.Add("X-App-Id", "kyc-dev");
        request.Headers.Add("X-User-Id", "contract-user");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", "test-api-key");

        var response = await _client.SendAsync(request);

        Assert.NotEqual(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.NotEqual(HttpStatusCode.BadRequest, response.StatusCode);
    }
}
