using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using ContextMemory.Core.Models;
using Xunit;

namespace ContextMemory.Api.Tests;

public class CompanyBrainDashboardIntegrationTests : IClassFixture<ContextMemoryWebApplicationFactory>
{
    private readonly HttpClient _client;

    public CompanyBrainDashboardIntegrationTests(ContextMemoryWebApplicationFactory factory) =>
        _client = factory.CreateClient();

    [Fact]
    public async Task GlobalDashboard_ReturnsAggregatedMetrics()
    {
        var companyId = $"dash-{Guid.NewGuid():N}"[..14];

        using (var register = MasterKeyRequest(HttpMethod.Post, "/companies/register",
                   JsonContent.Create(new RegisterCompanyRequest { CompanyId = companyId, Name = "Dash Co" })))
        {
            await _client.SendAsync(register);
        }

        using (var upsert = MasterKeyRequest(HttpMethod.Post, $"/companies/{companyId}/processes",
                   JsonContent.Create(new UpsertProcessRequest
                   {
                       ProcessId = "dash-flow",
                       Title = "Dash Flow",
                       PublishImmediately = false,
                       Steps = [new ProcessStep { Order = 1, Action = "Step." }]
                   })))
        {
            await _client.SendAsync(upsert);
        }

        using var dashboardRequest = MasterKeyRequest(HttpMethod.Get, "/companies/dashboard");
        var response = await _client.SendAsync(dashboardRequest);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var dashboard = await response.Content.ReadFromJsonAsync<CompanyBrainGlobalDashboard>();
        Assert.NotNull(dashboard);
        Assert.True(dashboard!.CompanyCount >= 1);
        Assert.Contains(dashboard.Companies, c => c.CompanyId == companyId);
        var row = dashboard.Companies.First(c => c.CompanyId == companyId);
        Assert.Equal(1, row.PendingApprovals);
    }

    private HttpRequestMessage MasterKeyRequest(HttpMethod method, string path, HttpContent? content = null)
    {
        var request = new HttpRequestMessage(method, path) { Content = content };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", "test-master-key");
        return request;
    }
}
