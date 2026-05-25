using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using ContextMemory.Core.Models;
using Xunit;

namespace ContextMemory.Api.Tests;

public class CompanyBrainSyncDiffIntegrationTests : IClassFixture<ContextMemoryWebApplicationFactory>
{
    private readonly HttpClient _client;

    public CompanyBrainSyncDiffIntegrationTests(ContextMemoryWebApplicationFactory factory) =>
        _client = factory.CreateClient();

    [Fact]
    public async Task Sync_ReturnsDiffAndHistory()
    {
        var companyId = $"diff-{Guid.NewGuid():N}"[..14];

        using (var register = MasterKeyRequest(HttpMethod.Post, "/companies/register",
                   JsonContent.Create(new RegisterCompanyRequest { CompanyId = companyId, Name = "Diff Co" })))
        {
            await _client.SendAsync(register);
        }

        using (var upsert = MasterKeyRequest(HttpMethod.Post, $"/companies/{companyId}/processes",
                   JsonContent.Create(new UpsertProcessRequest
                   {
                       ProcessId = "flow-a",
                       Title = "Flow A",
                       Steps = [new ProcessStep { Order = 1, Action = "Step A." }]
                   })))
        {
            await _client.SendAsync(upsert);
        }

        using var sync = MasterKeyRequest(HttpMethod.Post, $"/companies/{companyId}/sync");
        var syncResponse = await _client.SendAsync(sync);
        Assert.Equal(HttpStatusCode.OK, syncResponse.StatusCode);

        var syncResult = await syncResponse.Content.ReadFromJsonAsync<CompanySyncResult>();
        Assert.NotNull(syncResult?.Diff);

        using var historyRequest = MasterKeyRequest(HttpMethod.Get, $"/companies/{companyId}/sync/history");
        var historyResponse = await _client.SendAsync(historyRequest);
        Assert.Equal(HttpStatusCode.OK, historyResponse.StatusCode);

        var history = await historyResponse.Content.ReadFromJsonAsync<List<CompanySyncHistoryEntry>>();
        Assert.NotNull(history);
        Assert.NotEmpty(history!);
    }

    [Fact]
    public async Task Metrics_IncludesCompanyBrainSeries()
    {
        var companyId = $"met-{Guid.NewGuid():N}"[..14];

        using (var register = MasterKeyRequest(HttpMethod.Post, "/companies/register",
                   JsonContent.Create(new RegisterCompanyRequest { CompanyId = companyId, Name = "Metrics Co" })))
        {
            await _client.SendAsync(register);
        }

        using (var sync = MasterKeyRequest(HttpMethod.Post, $"/companies/{companyId}/sync"))
        {
            await _client.SendAsync(sync);
        }

        var response = await _client.GetAsync("/metrics");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        Assert.Contains("cm_company_brain_sync_total", body);
        Assert.Contains($"companyId=\"{companyId}\"", body);
    }

    private HttpRequestMessage MasterKeyRequest(HttpMethod method, string path, HttpContent? content = null)
    {
        var request = new HttpRequestMessage(method, path) { Content = content };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", "test-master-key");
        return request;
    }
}
