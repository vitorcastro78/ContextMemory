using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using ContextMemory.Core.Models;
using Xunit;

namespace ContextMemory.Api.Tests;

public class CompanyBrainIntegrationTests : IClassFixture<ContextMemoryWebApplicationFactory>
{
    private readonly HttpClient _client;

    public CompanyBrainIntegrationTests(ContextMemoryWebApplicationFactory factory) =>
        _client = factory.CreateClient();

    private HttpRequestMessage WithMasterKey(HttpMethod method, string path, HttpContent? content = null)
    {
        var request = new HttpRequestMessage(method, path) { Content = content };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", "test-master-key");
        return request;
    }

    [Fact]
    public async Task Companies_WithoutMasterKey_Returns401()
    {
        var response = await _client.GetAsync("/companies");
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task RegisterCompany_LinkApp_SyncAndExportSkills()
    {
        var companyId = $"test-co-{Guid.NewGuid():N}"[..16];

        using (var register = WithMasterKey(HttpMethod.Post, "/companies/register",
                   JsonContent.Create(new RegisterCompanyRequest
                   {
                       CompanyId = companyId,
                       Name = "Test Company",
                       Description = "Company Brain integration test"
                   })))
        {
            var registerResponse = await _client.SendAsync(register);
            Assert.Equal(HttpStatusCode.Created, registerResponse.StatusCode);
        }

        using (var link = WithMasterKey(HttpMethod.Post, $"/companies/{companyId}/apps/link",
                   JsonContent.Create(new LinkAppRequest { AppId = "kyc-dev" })))
        {
            var linkResponse = await _client.SendAsync(link);
            Assert.Equal(HttpStatusCode.OK, linkResponse.StatusCode);
        }

        using (var upsert = WithMasterKey(HttpMethod.Post, $"/companies/{companyId}/processes",
                   JsonContent.Create(new UpsertProcessRequest
                   {
                       ProcessId = "pep-check",
                       Title = "PEP Screening",
                       Category = ProcessCategory.Compliance,
                       Triggers = ["pep", "politically exposed"],
                       Steps =
                       [
                           new ProcessStep { Order = 1, Action = "Collect customer identity data." },
                           new ProcessStep { Order = 2, Action = "Run PEP list screening." }
                       ],
                       Guardrails = ["Do not skip enhanced due diligence for PEP matches."]
                   })))
        {
            var upsertResponse = await _client.SendAsync(upsert);
            Assert.Equal(HttpStatusCode.Created, upsertResponse.StatusCode);
        }

        using var skillsRequest = WithMasterKey(HttpMethod.Get, $"/companies/{companyId}/skills");
        var skillsResponse = await _client.SendAsync(skillsRequest);
        Assert.Equal(HttpStatusCode.OK, skillsResponse.StatusCode);

        var skills = await skillsResponse.Content.ReadFromJsonAsync<CompanySkillsFile>();
        Assert.NotNull(skills);
        Assert.Equal(companyId, skills!.CompanyId);
        Assert.NotEmpty(skills.Processes);
        Assert.NotEmpty(skills.Skills);
    }

    [Fact]
    public async Task IngestMarkdown_CreatesProcesses()
    {
        var companyId = $"ingest-{Guid.NewGuid():N}"[..16];

        using (var register = WithMasterKey(HttpMethod.Post, "/companies/register",
                   JsonContent.Create(new RegisterCompanyRequest { CompanyId = companyId, Name = "Ingest Test" })))
        {
            Assert.Equal(HttpStatusCode.Created, (await _client.SendAsync(register)).StatusCode);
        }

        const string markdown = """
            ## Process: Support Escalation
            Category: Support
            1. Gather ticket details.
            2. Escalate to tier 2.
            """;

        using var ingest = WithMasterKey(HttpMethod.Post, $"/companies/{companyId}/ingest",
            JsonContent.Create(new IngestKnowledgeRequest { Format = "markdown", Content = markdown }));
        var ingestResponse = await _client.SendAsync(ingest);
        Assert.Equal(HttpStatusCode.Created, ingestResponse.StatusCode);
        var ingestResult = await ingestResponse.Content.ReadFromJsonAsync<IngestKnowledgeResult>();
        Assert.NotNull(ingestResult);
        Assert.Contains("support-escalation", ingestResult!.ProcessIds, StringComparer.OrdinalIgnoreCase);

        using var pendingRequest = WithMasterKey(HttpMethod.Get, $"/companies/{companyId}/processes/pending");
        var pending = await (await _client.SendAsync(pendingRequest)).Content.ReadFromJsonAsync<List<CompanyProcess>>();
        Assert.NotNull(pending);
        Assert.Contains(pending!, p => p.ProcessId == "support-escalation");

        using var approve = WithMasterKey(HttpMethod.Post, $"/companies/{companyId}/processes/support-escalation/approve");
        Assert.Equal(HttpStatusCode.OK, (await _client.SendAsync(approve)).StatusCode);

        using var skillsRequest = WithMasterKey(HttpMethod.Get, $"/companies/{companyId}/skills.yaml");
        var yamlResponse = await _client.SendAsync(skillsRequest);
        Assert.Equal(HttpStatusCode.OK, yamlResponse.StatusCode);
        var yaml = await yamlResponse.Content.ReadAsStringAsync();
        Assert.Contains("support-escalation", yaml, StringComparison.OrdinalIgnoreCase);
    }
}
