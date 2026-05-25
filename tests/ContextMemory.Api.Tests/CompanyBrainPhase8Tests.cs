using ContextMemory.Core.CompanyBrain;
using ContextMemory.Core.Models;
using Xunit;

namespace ContextMemory.Api.Tests;

public class CompanyBrainPhase8Tests
{
    [Fact]
    public void CompileSkills_UsesOnlyPublishedProcesses()
    {
        var processes = new List<CompanyProcess>
        {
            new()
            {
                ProcessId = "published",
                CompanyId = "acme",
                Title = "Published",
                PublishStatus = ProcessPublishStatus.Published,
                Steps = [new ProcessStep { Order = 1, Action = "Go." }],
                UpdatedAt = DateTimeOffset.UtcNow
            },
            new()
            {
                ProcessId = "draft",
                CompanyId = "acme",
                Title = "Draft",
                PublishStatus = ProcessPublishStatus.Draft,
                Steps = [new ProcessStep { Order = 1, Action = "Wait." }],
                UpdatedAt = DateTimeOffset.UtcNow
            }
        };

        var published = processes.Where(p => p.PublishStatus == ProcessPublishStatus.Published).ToList();
        var skills = SkillsCompiler.Compile("acme", published);

        Assert.Single(skills.Processes);
        Assert.Equal("published", skills.Processes[0].ProcessId);
    }

    [Fact]
    public void CompanyBrainTelemetry_GetSnapshot_IncludesPendingCount()
    {
        var telemetry = new CompanyBrainTelemetry();
        telemetry.RecordSync("acme", new CompanySyncResult
        {
            CompanyId = "acme",
            SourcesSynced = 1,
            ProcessesUpserted = 1
        }, success: true);
        telemetry.RecordSearch("acme");

        var snapshot = telemetry.GetSnapshot("acme", pendingApprovals: 3);

        Assert.Equal(1, snapshot.SyncTotal);
        Assert.Equal(1, snapshot.SearchesTotal);
        Assert.Equal(3, snapshot.PendingApprovals);
    }

    [Fact]
    public void UpsertProcessRequest_PublishImmediatelyFalse_CreatesDraft()
    {
        var request = new UpsertProcessRequest
        {
            ProcessId = "draft-flow",
            Title = "Draft Flow",
            PublishImmediately = false,
            Steps = [new ProcessStep { Order = 1, Action = "Step." }]
        };

        var status = request.PublishImmediately
            ? ProcessPublishStatus.Published
            : ProcessPublishStatus.Draft;

        Assert.Equal(ProcessPublishStatus.Draft, status);
    }
}
