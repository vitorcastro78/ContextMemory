using ContextMemory.Core.CompanyBrain;
using ContextMemory.Core.CompanyBrain.Connectors;
using ContextMemory.Core.Models;
using Xunit;

namespace ContextMemory.Api.Tests;

public class CompanyBrainPhase4Tests
{
    [Fact]
    public void McpSseHub_CreatesSessionAndPushesMessage()
    {
        var hub = new McpSseHub();
        var sessionId = hub.CreateSession("acme");

        Assert.True(hub.TryGetSession(sessionId, out var session));
        Assert.Equal("acme", session!.CompanyId);

        var response = new JsonRpcResponse { Id = 1, Result = new { ok = true } };
        Assert.True(hub.TryPushResponse(sessionId, response));
    }

    [Fact]
    public void NotionDatabaseConnector_ExtractsTitle()
    {
        using var doc = System.Text.Json.JsonDocument.Parse("""
            {
              "properties": {
                "Name": {
                  "title": [{ "plain_text": "Runbook Refunds" }]
                }
              }
            }
            """);

        var title = NotionDatabaseConnector.ExtractNotionTitle(doc.RootElement);
        Assert.Equal("Runbook Refunds", title);
    }

    [Fact]
    public void McpSseHub_SerializesEndpointEvent()
    {
        var sse = McpSseHub.SerializeSseEvent("endpoint", "/companies/acme/mcp/messages?sessionId=x");
        Assert.Contains("event: endpoint", sse);
        Assert.Contains("data: /companies/acme/mcp/messages", sse);
    }
}
