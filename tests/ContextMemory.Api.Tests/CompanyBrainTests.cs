using ContextMemory.Core.CompanyBrain;
using ContextMemory.Core.CompanyBrain.Connectors;
using ContextMemory.Core.Models;
using Xunit;

namespace ContextMemory.Api.Tests;

public class CompanyBrainTests
{
    [Fact]
    public void MarkdownWikiConnector_ExtractsProcessesFromHeadings()
    {
        const string markdown = """
            # Wiki

            ## Process: Refund Handling
            Category: Operations
            Handle customer refund requests consistently.

            #### Triggers
            - refund
            - reembolso

            1. Verify purchase date and eligibility.
            2. Approve or escalate based on policy.

            #### Guardrails
            - Never promise refunds outside policy.
            """;

        var processes = MarkdownWikiConnector.ExtractProcesses("acme-corp", "refunds.md", markdown);

        Assert.Single(processes);
        var process = processes[0];
        Assert.Equal("refund-handling", process.ProcessId);
        Assert.Equal(ProcessCategory.Operations, process.Category);
        Assert.Equal(2, process.Steps.Count);
        Assert.Contains("refund", process.Triggers);
        Assert.Single(process.Guardrails);
    }

    [Fact]
    public void SkillsCompiler_GroupsProcessesByCategory()
    {
        var processes = new List<CompanyProcess>
        {
            new()
            {
                ProcessId = "refund-handling",
                CompanyId = "acme-corp",
                Title = "Refund Handling",
                Category = ProcessCategory.Operations,
                Steps = [new ProcessStep { Order = 1, Action = "Verify eligibility." }],
                UpdatedAt = DateTimeOffset.UtcNow
            },
            new()
            {
                ProcessId = "incident-response",
                CompanyId = "acme-corp",
                Title = "Incident Response",
                Category = ProcessCategory.Engineering,
                Steps = [new ProcessStep { Order = 1, Action = "Acknowledge alert." }],
                UpdatedAt = DateTimeOffset.UtcNow
            }
        };

        var skills = SkillsCompiler.Compile("acme-corp", processes);

        Assert.Equal(2, skills.Skills.Count);
        Assert.Equal(2, skills.Processes.Count);
        Assert.Contains(skills.Skills, s => s.SkillId == "operations");
        Assert.Contains(skills.Skills, s => s.SkillId == "engineering");
    }

    [Fact]
    public void SkillsExporter_ProducesYamlAndMcp()
    {
        var skills = SkillsCompiler.Compile("acme-corp", [
            new CompanyProcess
            {
                ProcessId = "refund-handling",
                CompanyId = "acme-corp",
                Title = "Refund Handling",
                Category = ProcessCategory.Operations,
                Triggers = ["refund"],
                Steps = [new ProcessStep { Order = 1, Action = "Verify eligibility." }],
                UpdatedAt = DateTimeOffset.UtcNow
            }
        ]);

        var yaml = SkillsExporter.ToYaml(skills);
        var mcp = SkillsExporter.ToMcp(skills);

        Assert.Contains("company_id: acme-corp", yaml);
        Assert.Contains("refund-handling", yaml);
        Assert.Single(mcp.Tools);
        Assert.Equal("process_refund_handling", mcp.Tools[0].Name);
    }

    [Fact]
    public void ProcessJsonFolderConnector_ParsesSingleProcess()
    {
        const string json = """
            {
              "processId": "onboarding",
              "title": "Customer Onboarding",
              "category": "Compliance",
              "steps": [{ "order": 1, "action": "Collect ID." }]
            }
            """;

        var processes = ProcessJsonFolderConnector.ParseJsonFile("acme", "onboarding.json", json);
        Assert.Single(processes);
        Assert.Equal("onboarding", processes[0].ProcessId);
    }
}
