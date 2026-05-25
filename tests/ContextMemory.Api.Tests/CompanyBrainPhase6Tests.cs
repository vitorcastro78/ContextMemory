using ContextMemory.Core.CompanyBrain;
using ContextMemory.Core.Models;
using Xunit;

namespace ContextMemory.Api.Tests;

public class CompanyBrainPhase6Tests
{
    [Fact]
    public void ProcessSyncDiffer_DetectsAddedUpdatedRemoved()
    {
        var before = new List<CompanyProcess>
        {
            CreateProcess("keep", "Keep"),
            CreateProcess("update", "Old title"),
            CreateProcess("remove", "Gone")
        };

        var after = new List<CompanyProcess>
        {
            CreateProcess("keep", "Keep"),
            CreateProcess("update", "New title"),
            CreateProcess("added", "Added")
        };

        var diff = ProcessSyncDiffer.Compute(before, after);

        Assert.Equal(["added"], diff.Added);
        Assert.Equal(["update"], diff.Updated);
        Assert.Equal(["remove"], diff.Removed);
        Assert.Equal(3, diff.TotalBefore);
        Assert.Equal(3, diff.TotalAfter);
    }

    [Fact]
    public void CompanyBrainTelemetry_ExportsPrometheusMetrics()
    {
        var telemetry = new CompanyBrainTelemetry();
        telemetry.RecordSync("acme", new CompanySyncResult
        {
            CompanyId = "acme",
            SourcesSynced = 1,
            ProcessesUpserted = 2,
            Diff = new ProcessSyncDiff
            {
                Added = ["a"],
                Updated = ["b"],
                Removed = []
            }
        }, success: true);
        telemetry.RecordSearch("acme");
        telemetry.RecordMcpCall("acme");

        var output = telemetry.ExportPrometheus();
        Assert.Contains("cm_company_brain_sync_total", output);
        Assert.Contains("companyId=\"acme\"", output);
        Assert.Contains("cm_company_brain_searches_total", output);
        Assert.Contains("cm_company_brain_mcp_calls_total", output);
    }

    [Fact]
    public void SyncHistoryStore_AppendsAndReadsEntries()
    {
        var root = Path.Combine(Path.GetTempPath(), "cm-sync", Guid.NewGuid().ToString("N"));
        var store = new SyncHistoryStore(Microsoft.Extensions.Options.Options.Create(
            new ContextMemory.Core.Configuration.ContextMemoryOptions
            {
                DataPath = root,
                ContentRootPath = root
            }));

        store.Append(new CompanySyncHistoryEntry
        {
            CompanyId = "acme",
            SyncedAt = DateTimeOffset.UtcNow,
            SourcesSynced = 1,
            ProcessesUpserted = 1,
            Diff = new ProcessSyncDiff { Added = ["p1"] }
        });

        var history = store.ListRecent("acme");
        Assert.Single(history);
        Assert.Equal(["p1"], history[0].Diff.Added);

        Directory.Delete(root, recursive: true);
    }

    private static CompanyProcess CreateProcess(string id, string title) => new()
    {
        ProcessId = id,
        CompanyId = "acme",
        Title = title,
        UpdatedAt = DateTimeOffset.UtcNow
    };
}
