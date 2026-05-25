using ContextMemory.Core.CompanyBrain;
using ContextMemory.Core.Models;
using Xunit;

namespace ContextMemory.Api.Tests;

public class CompanyBrainPhase9SideBySideTests
{
    [Fact]
    public void SideBySideBuilder_ProducesBeforeAndAfterForUpdatedProcess()
    {
        var before = new List<CompanyProcess>
        {
            new()
            {
                ProcessId = "refund",
                CompanyId = "acme",
                Title = "Refund Flow",
                Summary = "Old summary",
                Steps = [new ProcessStep { Order = 1, Action = "Validate." }],
                UpdatedAt = DateTimeOffset.UtcNow
            }
        };

        var after = new List<CompanyProcess>
        {
            before[0] with
            {
                Summary = "New summary",
                Steps =
                [
                    new ProcessStep { Order = 1, Action = "Validate." },
                    new ProcessStep { Order = 2, Action = "Approve." }
                ]
            }
        };

        var diff = ProcessSyncDiffer.Compute(before, after);
        var detail = ProcessSyncDiffEnricher.Enrich(diff, before, after);

        Assert.Single(detail.SideBySide);
        var entry = detail.SideBySide[0];
        Assert.Equal("updated", entry.ChangeType);
        Assert.Contains("Old summary", entry.BeforeText);
        Assert.Contains("New summary", entry.AfterText);
        Assert.Contains(entry.Changes, c => c.Field == "Resumo");
        Assert.Contains(entry.Changes, c => c.Field == "Passos");
    }
}
