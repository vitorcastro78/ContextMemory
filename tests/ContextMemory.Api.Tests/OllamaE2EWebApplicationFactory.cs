using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;

namespace ContextMemory.Api.Tests;

public sealed class OllamaE2EWebApplicationFactory : WebApplicationFactory<Program>
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Development");
        var ollamaUrl = Environment.GetEnvironmentVariable("OLLAMA_E2E_URL")
            ?? "http://localhost:11434";

        builder.ConfigureAppConfiguration((_, config) =>
        {
            config.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ContextMemory:OllamaEndpoint"] = ollamaUrl,
                ["ContextMemory:DataPath"] = Path.Combine(Path.GetTempPath(), "cm-ollama-e2e", Guid.NewGuid().ToString("N")),
                ["ContextMemory:MasterKey"] = "test-master-key",
                ["ContextMemory:EnableContentFilter"] = "false",
                ["ContextMemory:Apps:kyc-dev:ApiKey"] = "test-api-key",
                ["ContextMemory:Apps:kyc-dev:SystemPrompt"] = "Test",
                ["ContextMemory:Apps:kyc-dev:DefaultLanguage"] = "en-US",
                ["ContextMemory:Apps:kyc-dev:WikiPath"] = Path.GetTempPath()
            });
        });
    }
}
