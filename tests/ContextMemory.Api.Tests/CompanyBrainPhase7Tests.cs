using ContextMemory.Core.CompanyBrain;
using ContextMemory.Core.CompanyBrain.Connectors;
using ContextMemory.Core.Models;
using Xunit;

namespace ContextMemory.Api.Tests;

public class CompanyBrainPhase7Tests
{
    [Fact]
    public void ProcessSyncDiffEnricher_BuildsDetailAndCriticalAlerts()
    {
        var before = new List<CompanyProcess>
        {
            new()
            {
                ProcessId = "pep",
                CompanyId = "acme",
                Title = "PEP Flow",
                IsCritical = true,
                UpdatedAt = DateTimeOffset.UtcNow
            }
        };

        var after = new List<CompanyProcess>
        {
            new()
            {
                ProcessId = "pep",
                CompanyId = "acme",
                Title = "PEP Flow v2",
                IsCritical = true,
                UpdatedAt = DateTimeOffset.UtcNow
            }
        };

        var diff = ProcessSyncDiffer.Compute(before, after);
        var detail = ProcessSyncDiffEnricher.Enrich(diff, before, after);
        var alerts = ProcessSyncDiffEnricher.BuildCriticalAlerts(detail);

        Assert.Single(detail.Updated);
        Assert.Equal("PEP Flow v2", detail.Updated[0].Title);
        Assert.Single(alerts);
        Assert.Equal("updated", alerts[0].ChangeType);
    }

    [Fact]
    public void MarkdownWikiConnector_ParsesCriticalFlag()
    {
        const string markdown = """
            ## Process: Incident Response
            Category: Engineering
            Critical: yes

            1. Acknowledge alert.
            """;

        var processes = MarkdownWikiConnector.ExtractProcesses("acme", "incident.md", markdown);
        Assert.Single(processes);
        Assert.True(processes[0].IsCritical);
    }

    [Fact]
    public void MarkdownWikiConnector_ComplianceDefaultsToCritical()
    {
        const string markdown = """
            ## Process: AML Check
            Category: Compliance

            1. Run screening.
            """;

        var processes = MarkdownWikiConnector.ExtractProcesses("acme", "aml.md", markdown);
        Assert.Single(processes);
        Assert.True(processes[0].IsCritical);
    }

    [Fact]
    public void CompanyAlertConfigStore_SavesAndLoads()
    {
        var root = Path.Combine(Path.GetTempPath(), "cm-alert", Guid.NewGuid().ToString("N"));
        var store = new CompanyAlertConfigStore(Microsoft.Extensions.Options.Options.Create(
            new ContextMemory.Core.Configuration.ContextMemoryOptions
            {
                DataPath = root,
                ContentRootPath = root
            }));

        store.Save("acme", new CompanyAlertConfig
        {
            CompanyId = "acme",
            OutboundWebhookUrl = "https://example.com/hook",
            Enabled = true
        });

        var loaded = store.Get("acme");
        Assert.Equal("https://example.com/hook", loaded.OutboundWebhookUrl);
        Assert.True(loaded.Enabled);

        Directory.Delete(root, recursive: true);
    }
}
