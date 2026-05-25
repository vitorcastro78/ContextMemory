using ContextMemory.Core.CompanyBrain;
using ContextMemory.Core.CompanyBrain.Connectors;
using ContextMemory.Core.Models;
using ContextMemory.Core.Security;
using Xunit;

namespace ContextMemory.Api.Tests;

public class CompanyBrainPhase3Tests
{
    [Fact]
    public void WebhookAuth_ValidatesHmacSignature()
    {
        const string secret = "cm_wh_testsecret";
        const string body = """{"format":"markdown","content":"## Process: Test\n1. Step"}""";
        var signature = CompanyWebhookAuth.ComputeSignature(secret, body);

        Assert.True(CompanyWebhookAuth.Validate(secret, body, signature));
        Assert.False(CompanyWebhookAuth.Validate(secret, body, "sha256=invalid"));
    }

    [Fact]
    public void McpJsonRpc_ReturnsToolsList()
    {
        var skills = SkillsCompiler.Compile("acme", [
            new CompanyProcess
            {
                ProcessId = "refund-handling",
                CompanyId = "acme",
                Title = "Refund",
                Steps = [new ProcessStep { Order = 1, Action = "Verify." }],
                UpdatedAt = DateTimeOffset.UtcNow
            }
        ]);

        var ctx = new McpServerContext("acme", skills, (_, _) => skills.Processes);
        var response = McpJsonRpcServer.Handle(ctx, new JsonRpcRequest
        {
            Id = 1,
            Method = "tools/list"
        });

        Assert.Null(response.Error);
        Assert.NotNull(response.Result);
    }

    [Fact]
    public void ConfluenceConnector_StripHtml_RemovesTags()
    {
        var text = ConfluenceWikiConnector.StripHtml("<p>Hello <strong>world</strong></p>");
        Assert.Contains("Hello", text);
        Assert.Contains("world", text);
        Assert.DoesNotContain("<p>", text);
    }
}
