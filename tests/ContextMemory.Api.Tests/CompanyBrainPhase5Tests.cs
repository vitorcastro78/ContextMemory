using ContextMemory.Core.CompanyBrain;
using ContextMemory.Core.CompanyBrain.Connectors;
using ContextMemory.Core.Models;
using Xunit;

namespace ContextMemory.Api.Tests;

public class CompanyBrainPhase5Tests
{
    [Fact]
    public void ProcessMatcher_PrefersKeywordMatch()
    {
        var processes = new List<CompanyProcess>
        {
            new()
            {
                ProcessId = "refund",
                CompanyId = "acme",
                Title = "Refund Exception",
                Triggers = ["refund", "reembolso"],
                UpdatedAt = DateTimeOffset.UtcNow
            },
            new()
            {
                ProcessId = "incident",
                CompanyId = "acme",
                Title = "Incident Response",
                Triggers = ["incident"],
                UpdatedAt = DateTimeOffset.UtcNow
            }
        };

        var ranked = ProcessMatcher.Rank(processes, "refund policy", null, null, 2);

        Assert.Equal("refund", ranked[0].ProcessId);
    }

    [Fact]
    public void McpJsonRpcServer_ListsResourcesAndPrompts()
    {
        var skills = SkillsCompiler.Compile("acme", [
            new CompanyProcess
            {
                ProcessId = "refund",
                CompanyId = "acme",
                Title = "Refund",
                Category = ProcessCategory.Operations,
                Steps = [new ProcessStep { Order = 1, Action = "Validate ticket." }],
                UpdatedAt = DateTimeOffset.UtcNow
            }
        ]);

        var ctx = new McpServerContext("acme", skills, (_, _) => skills.Processes);

        var resources = McpJsonRpcServer.Handle(ctx, new JsonRpcRequest { Id = 1, Method = "resources/list" });
        Assert.Null(resources.Error);
        Assert.NotNull(resources.Result);

        var prompts = McpJsonRpcServer.Handle(ctx, new JsonRpcRequest { Id = 2, Method = "prompts/list" });
        Assert.Null(prompts.Error);
        Assert.NotNull(prompts.Result);
    }

    [Fact]
    public void McpJsonRpcServer_SearchToolReturnsMatches()
    {
        var skills = SkillsCompiler.Compile("acme", [
            new CompanyProcess
            {
                ProcessId = "refund",
                CompanyId = "acme",
                Title = "Refund Exception",
                Triggers = ["refund"],
                Steps = [new ProcessStep { Order = 1, Action = "Validate eligibility." }],
                UpdatedAt = DateTimeOffset.UtcNow
            }
        ]);

        var ctx = new McpServerContext(
            "acme",
            skills,
            (query, topK) => ProcessMatcher.Rank(skills.Processes, query, null, null, topK));

        var response = McpJsonRpcServer.Handle(ctx, new JsonRpcRequest
        {
            Id = 3,
            Method = "tools/call",
            Params = System.Text.Json.JsonDocument.Parse("""
                {"name":"company_search_processes","arguments":{"query":"refund"}}
                """).RootElement
        });

        Assert.Null(response.Error);
        Assert.NotNull(response.Result);
    }

    [Fact]
    public void ProcessExecutionLogger_AppendsJsonLine()
    {
        var root = Path.Combine(Path.GetTempPath(), "cm-exec", Guid.NewGuid().ToString("N"));
        var logger = new ProcessExecutionLogger(Microsoft.Extensions.Options.Options.Create(
            new ContextMemory.Core.Configuration.ContextMemoryOptions
            {
                DataPath = root,
                ContentRootPath = root
            }));

        logger.Log("acme", "process_refund", "ticket #42");

        var file = Path.Combine(root, "companies", "acme", "mcp-executions.jsonl");
        Assert.True(File.Exists(file));
        Assert.Contains("process_refund", File.ReadAllText(file));

        Directory.Delete(root, recursive: true);
    }
}
