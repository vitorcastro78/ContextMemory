using ContextMemory.Api.Tests.Fakes;
using ContextMemory.Core.CompanyBrain;
using Microsoft.Extensions.Http;
using ContextMemory.Core.Configuration;
using ContextMemory.Core.Contracts;
using ContextMemory.Core.Models;
using ContextMemory.Core.Profile;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace ContextMemory.Api.Tests;

public class CompanyBrainPhase9Tests
{
    [Fact]
    public void GetGlobalDashboard_AggregatesAcrossCompanies()
    {
        var store = new InMemoryCompanyBrainStore();
        var telemetry = new CompanyBrainTelemetry();
        var service = CreateService(store, telemetry);

        service.RegisterCompany(new RegisterCompanyRequest { CompanyId = "acme", Name = "Acme" });
        service.RegisterCompany(new RegisterCompanyRequest { CompanyId = "beta", Name = "Beta" });

        service.UpsertProcess("acme", new UpsertProcessRequest
        {
            ProcessId = "flow-a",
            Title = "Flow A",
            PublishImmediately = true,
            Steps = [new ProcessStep { Order = 1, Action = "Step." }]
        });
        service.UpsertProcess("acme", new UpsertProcessRequest
        {
            ProcessId = "flow-b",
            Title = "Flow B",
            PublishImmediately = false,
            Steps = [new ProcessStep { Order = 1, Action = "Step." }]
        });
        service.UpsertProcess("beta", new UpsertProcessRequest
        {
            ProcessId = "flow-c",
            Title = "Flow C",
            IsCritical = true,
            Steps = [new ProcessStep { Order = 1, Action = "Step." }]
        });

        telemetry.RecordSearch("acme");
        telemetry.RecordSearch("acme");
        telemetry.RecordMcpCall("beta");

        var dashboard = service.GetGlobalDashboard();

        Assert.Equal(2, dashboard.CompanyCount);
        Assert.Equal(3, dashboard.TotalProcesses);
        Assert.Equal(2, dashboard.TotalPublished);
        Assert.Equal(1, dashboard.TotalPendingApprovals);
        Assert.Equal(1, dashboard.TotalCriticalProcesses);
        Assert.Equal(2, dashboard.TotalSearches);
        Assert.Equal(1, dashboard.TotalMcpCalls);
        Assert.Equal(2, dashboard.Companies.Count);

        var acmeRow = dashboard.Companies.First(c => c.CompanyId == "acme");
        Assert.Equal(2, acmeRow.ProcessCount);
        Assert.Equal(1, acmeRow.PendingApprovals);
        Assert.Equal(2, acmeRow.Metrics.SearchesTotal);
    }

    private static CompanyBrainService CreateService(
        InMemoryCompanyBrainStore store,
        CompanyBrainTelemetry telemetry)
    {
        var options = Options.Create(new ContextMemoryOptions
        {
            DataPath = "data",
            ContentRootPath = Path.GetTempPath()
        });
        var alertStore = new CompanyAlertConfigStore(options);
        var alertDispatcher = new SyncAlertDispatcher(
            new HttpClient(),
            alertStore,
            NullLogger<SyncAlertDispatcher>.Instance);
        var oauthState = new SharePointOAuthStateStore(options);
        var httpFactory = new SingleNamedHttpClientFactory(nameof(SharePointOAuthService));
        var sharePointOAuth = new SharePointOAuthService(
            httpFactory,
            oauthState,
            store,
            options,
            NullLogger<SharePointOAuthService>.Instance);

        return new CompanyBrainService(
            store,
            [],
            new AppRegistry(options),
            new CompanyBrainDiskImporter(store, options),
            new ProcessEmbeddingIndex(new DeterministicEmbeddingEngine()),
            new ProcessExecutionLogger(options),
            new SyncHistoryStore(options),
            telemetry,
            alertDispatcher,
            alertStore,
            sharePointOAuth);
    }

    private sealed class SingleNamedHttpClientFactory(string name) : IHttpClientFactory
    {
        public HttpClient CreateClient(string clientName) =>
            string.Equals(clientName, name, StringComparison.Ordinal)
                ? new HttpClient()
                : new HttpClient();
    }

    private sealed class InMemoryCompanyBrainStore : ICompanyBrainStore
    {
        private readonly Dictionary<string, CompanyProfile> _companies = new(StringComparer.Ordinal);
        private readonly Dictionary<string, List<CompanyProcess>> _processes = new(StringComparer.Ordinal);
        private readonly Dictionary<string, List<KnowledgeSource>> _sources = new(StringComparer.Ordinal);
        private readonly Dictionary<string, HashSet<string>> _linkedApps = new(StringComparer.Ordinal);
        private readonly Dictionary<string, CompanySkillsFile> _skills = new(StringComparer.Ordinal);
        private readonly Dictionary<string, string> _webhookSecrets = new(StringComparer.Ordinal);

        public bool TryGetCompany(string companyId, out CompanyProfile? company) =>
            _companies.TryGetValue(companyId, out company);

        public IReadOnlyList<CompanyProfile> ListCompanies() => _companies.Values.ToList();

        public bool RegisterCompany(CompanyProfile company)
        {
            _companies[company.CompanyId] = company;
            _processes.TryAdd(company.CompanyId, []);
            _sources.TryAdd(company.CompanyId, []);
            _linkedApps.TryAdd(company.CompanyId, new HashSet<string>(StringComparer.Ordinal));
            return true;
        }

        public bool UpsertProcess(CompanyProcess process)
        {
            if (!_processes.TryGetValue(process.CompanyId, out var list))
                return false;

            list.RemoveAll(p => p.ProcessId == process.ProcessId);
            list.Add(process);
            return true;
        }

        public IReadOnlyList<CompanyProcess> ListProcesses(string companyId) =>
            _processes.TryGetValue(companyId, out var list) ? list : [];

        public bool TryGetProcess(string companyId, string processId, out CompanyProcess? process)
        {
            process = ListProcesses(companyId).FirstOrDefault(p => p.ProcessId == processId);
            return process is not null;
        }

        public bool UpsertKnowledgeSource(KnowledgeSource source) => true;

        public IReadOnlyList<KnowledgeSource> ListKnowledgeSources(string companyId) =>
            _sources.TryGetValue(companyId, out var list) ? list : [];

        public bool LinkApp(string companyId, string appId) => true;

        public bool UnlinkApp(string companyId, string appId) => true;

        public bool TryGetCompanyForApp(string appId, out string? companyId)
        {
            companyId = null;
            return false;
        }

        public IReadOnlyList<string> ListLinkedApps(string companyId) =>
            _linkedApps.TryGetValue(companyId, out var set) ? set.ToList() : [];

        public void SaveSkillsCache(string companyId, CompanySkillsFile skillsFile) =>
            _skills[companyId] = skillsFile;

        public bool TryGetSkillsCache(string companyId, out CompanySkillsFile? skillsFile) =>
            _skills.TryGetValue(companyId, out skillsFile);

        public bool TryGetWebhookSecret(string companyId, out string? secret) =>
            _webhookSecrets.TryGetValue(companyId, out secret);

        public string SetWebhookSecret(string companyId, string? secret = null)
        {
            secret ??= Guid.NewGuid().ToString("N");
            _webhookSecrets[companyId] = secret;
            return secret;
        }
    }
}
