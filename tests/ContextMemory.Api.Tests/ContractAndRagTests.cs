using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using ContextMemory.Core.Contracts;
using ContextMemory.Core.Models;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace ContextMemory.Api.Tests;

public class ContractAndRagTests : IClassFixture<StubOllamaWebApplicationFactory>
{
    private const string AppId = "demo-app";
    private const string ApiKey = "test-api-key";
    private readonly HttpClient _client;
    private readonly StubOllamaWebApplicationFactory _factory;

    public ContractAndRagTests(StubOllamaWebApplicationFactory factory)
    {
        _factory = factory;
        _client = factory.CreateClient();
    }

    private HttpRequestMessage CreateAuthedRequest(HttpMethod method, string path, HttpContent? content = null)
    {
        var request = new HttpRequestMessage(method, path) { Content = content };
        request.Headers.Add("X-App-Id", AppId);
        request.Headers.Add("X-User-Id", "contract-user");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", ApiKey);
        return request;
    }

    [Fact]
    public async Task Chat_Passthrough_PreservesOllamaResponseFields()
    {
        var payload = """
            {
              "model": "llama3.2",
              "messages": [{ "role": "user", "content": "hello" }],
              "stream": false
            }
            """;

        using var request = CreateAuthedRequest(
            HttpMethod.Post,
            "/api/chat",
            new StringContent(payload, Encoding.UTF8, "application/json"));

        var response = await _client.SendAsync(request);
        response.EnsureSuccessStatusCode();

        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var root = doc.RootElement;

        Assert.Equal("llama3.2", root.GetProperty("model").GetString());
        Assert.True(root.GetProperty("done").GetBoolean());
        Assert.Equal("stop", root.GetProperty("done_reason").GetString());
        Assert.Equal("Hello from stub", root.GetProperty("message").GetProperty("content").GetString());
        Assert.Equal(4321000000, root.GetProperty("total_duration").GetInt64());
        Assert.Equal(312, root.GetProperty("prompt_eval_count").GetInt32());
        Assert.Equal(187, root.GetProperty("eval_count").GetInt32());
        Assert.Equal(JsonValueKind.Array, root.GetProperty("context").ValueKind);
    }

    [Fact]
    public async Task Generate_Passthrough_PreservesOllamaGenerateFields()
    {
        var payload = """
            {
              "model": "llama3.2",
              "prompt": "Summarize this",
              "stream": false
            }
            """;

        using var request = CreateAuthedRequest(
            HttpMethod.Post,
            "/api/generate",
            new StringContent(payload, Encoding.UTF8, "application/json"));

        var response = await _client.SendAsync(request);
        response.EnsureSuccessStatusCode();

        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var root = doc.RootElement;

        Assert.Equal("llama3.2", root.GetProperty("model").GetString());
        Assert.Equal("Generated text", root.GetProperty("response").GetString());
        Assert.True(root.GetProperty("done").GetBoolean());
        Assert.Equal(4321000000, root.GetProperty("total_duration").GetInt64());
        Assert.Equal(12, root.GetProperty("prompt_eval_count").GetInt32());
        Assert.Equal(8, root.GetProperty("eval_count").GetInt32());
    }

    [Fact]
    public async Task Generate_Streaming_ReturnsNdjson()
    {
        var payload = """{"model":"llama3.2","prompt":"x","stream":true}""";
        using var request = CreateAuthedRequest(
            HttpMethod.Post,
            "/api/generate",
            new StringContent(payload, Encoding.UTF8, "application/json"));

        var response = await _client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead);
        response.EnsureSuccessStatusCode();
        Assert.Contains("ndjson", response.Content.Headers.ContentType?.MediaType ?? "");

        var body = await response.Content.ReadAsStringAsync();
        Assert.Contains("\"response\":\"Generated\"", body);
        Assert.Contains("\"done\":true", body);
    }

    [Fact]
    public async Task GetApp_ReturnsMetadata()
    {
        using var request = CreateAuthedRequest(HttpMethod.Get, $"/apps/{AppId}");
        var response = await _client.SendAsync(request);
        response.EnsureSuccessStatusCode();

        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var root = doc.RootElement;

        Assert.Equal(AppId, root.GetProperty("AppId").GetString());
        Assert.Equal("ollama", root.GetProperty("LlmBackend").GetString());
        Assert.Equal($"/apps/{AppId}/wiki", root.GetProperty("WikiUploadEndpoint").GetString());
    }

    [Fact]
    public async Task RagPipeline_FindsIndexedWikiChunk()
    {
        const string marker = "UNIQUE_RAG_MARKER_XYZ_12345";

        using var scope = _factory.Services.CreateScope();
        var wikiIndex = scope.ServiceProvider.GetRequiredService<IWikiIndexService>();
        var appRegistry = scope.ServiceProvider.GetRequiredService<IAppRegistry>();

        appRegistry.TryGetApp(AppId, out var app);
        Assert.NotNull(app);

        var wikiFile = Path.Combine(app!.WikiPath, "rag-test.md");
        await File.WriteAllTextAsync(wikiFile, $"## Test\n\nContent with {marker} for retrieval.\n");

        await wikiIndex.ReindexFileAsync(AppId, app.WikiPath, "rag-test.md", CancellationToken.None);
        var chunks = await wikiIndex.SearchAsync(AppId, marker, topK: 3, threshold: 0.5f, CancellationToken.None);

        Assert.NotEmpty(chunks);
        Assert.Contains(chunks, c => c.Content.Contains(marker, StringComparison.Ordinal));
    }
}
