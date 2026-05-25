using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using ContextMemory.Core.Models;
using Xunit;

namespace ContextMemory.Api.Tests;

public class CompanyBrainApprovalIntegrationTests : IClassFixture<ContextMemoryWebApplicationFactory>
{
    private readonly HttpClient _client;

    public CompanyBrainApprovalIntegrationTests(ContextMemoryWebApplicationFactory factory) =>
        _client = factory.CreateClient();

    [Fact]
    public async Task Sync_CreatesDraft_ProcessApprove_Publishes()
    {
        var companyId = $"appr-{Guid.NewGuid():N}"[..14];

        using (var register = MasterKeyRequest(HttpMethod.Post, "/companies/register",
                   JsonContent.Create(new RegisterCompanyRequest { CompanyId = companyId, Name = "Approval Co" })))
        {
            await _client.SendAsync(register);
        }

        using (var upsert = MasterKeyRequest(HttpMethod.Post, $"/companies/{companyId}/processes",
                   JsonContent.Create(new UpsertProcessRequest
                   {
                       ProcessId = "manual-flow",
                       Title = "Manual Flow",
                       PublishImmediately = true,
                       Steps = [new ProcessStep { Order = 1, Action = "Published step." }]
                   })))
        {
            await _client.SendAsync(upsert);
        }

        using (var upsertDraft = MasterKeyRequest(HttpMethod.Post, $"/companies/{companyId}/processes",
                   JsonContent.Create(new UpsertProcessRequest
                   {
                       ProcessId = "draft-flow",
                       Title = "Draft Flow",
                       PublishImmediately = false,
                       Steps = [new ProcessStep { Order = 1, Action = "Draft step." }]
                   })))
        {
            await _client.SendAsync(upsertDraft);
        }

        using var pendingRequest = MasterKeyRequest(HttpMethod.Get, $"/companies/{companyId}/processes/pending");
        var pendingResponse = await _client.SendAsync(pendingRequest);
        Assert.Equal(HttpStatusCode.OK, pendingResponse.StatusCode);
        var pending = await pendingResponse.Content.ReadFromJsonAsync<List<CompanyProcess>>();
        Assert.NotNull(pending);
        Assert.Contains(pending!, p => p.ProcessId == "draft-flow");

        using var approveRequest = MasterKeyRequest(
            HttpMethod.Post,
            $"/companies/{companyId}/processes/draft-flow/approve");
        var approveResponse = await _client.SendAsync(approveRequest);
        Assert.Equal(HttpStatusCode.OK, approveResponse.StatusCode);

        using var metricsRequest = MasterKeyRequest(HttpMethod.Get, $"/companies/{companyId}/metrics");
        var metricsResponse = await _client.SendAsync(metricsRequest);
        Assert.Equal(HttpStatusCode.OK, metricsResponse.StatusCode);
        var metrics = await metricsResponse.Content.ReadFromJsonAsync<CompanyBrainMetricsSnapshot>();
        Assert.NotNull(metrics);
        Assert.True(metrics!.ApprovalsTotal >= 1);
    }

    private HttpRequestMessage MasterKeyRequest(HttpMethod method, string path, HttpContent? content = null)
    {
        var request = new HttpRequestMessage(method, path) { Content = content };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", "test-master-key");
        return request;
    }
}
