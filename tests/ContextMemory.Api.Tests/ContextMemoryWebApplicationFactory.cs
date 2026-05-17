using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;

namespace ContextMemory.Api.Tests;

public sealed class ContextMemoryWebApplicationFactory : WebApplicationFactory<Program>
{
    private readonly string _dataRoot;

    public ContextMemoryWebApplicationFactory()
    {
        _dataRoot = Path.Combine(Path.GetTempPath(), "cm-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_dataRoot);
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Development");
        builder.ConfigureAppConfiguration((_, config) =>
        {
            config.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ContextMemory:DataPath"] = _dataRoot,
                ["ContextMemory:WikiPath"] = _dataRoot,
                ["ContextMemory:MasterKey"] = "test-master-key",
                ["ContextMemory:OllamaEndpoint"] = "http://127.0.0.1:1",
                ["ContextMemory:Apps:kyc-dev:ApiKey"] = "test-api-key",
                ["ContextMemory:Apps:kyc-dev:SystemPrompt"] = "Test persona",
                ["ContextMemory:Apps:kyc-dev:DefaultLanguage"] = "pt-PT",
                ["ContextMemory:Apps:kyc-dev:WikiPath"] = _dataRoot
            });
        });
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        if (Directory.Exists(_dataRoot))
        {
            try { Directory.Delete(_dataRoot, recursive: true); }
            catch { /* best effort cleanup */ }
        }
    }
}
