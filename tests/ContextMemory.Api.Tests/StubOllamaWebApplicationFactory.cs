using ContextMemory.Adapters;
using ContextMemory.Api.Tests.Fakes;
using ContextMemory.Core.Contracts;
using ContextMemory.Embeddings;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace ContextMemory.Api.Tests;

public sealed class StubOllamaWebApplicationFactory : WebApplicationFactory<Program>
{
    public StubOllamaHandler OllamaHandler { get; } = new();
    private readonly string _dataRoot;

    public string DemoWikiPath { get; }

    public StubOllamaWebApplicationFactory()
    {
        _dataRoot = Path.Combine(Path.GetTempPath(), "cm-stub-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_dataRoot);
        DemoWikiPath = Path.Combine(_dataRoot, "wiki-demo");
        Directory.CreateDirectory(DemoWikiPath);
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
                ["ContextMemory:OllamaEndpoint"] = "http://ollama-stub",
                ["ContextMemory:MasterKey"] = "test-master-key",
                ["ContextMemory:EnableContentFilter"] = "false",
                ["ContextMemory:Apps:demo-app:ApiKey"] = "test-api-key",
                ["ContextMemory:Apps:demo-app:SystemPrompt"] = "Test persona",
                ["ContextMemory:Apps:demo-app:DefaultLanguage"] = "en-US",
                ["ContextMemory:Apps:demo-app:WikiPath"] = DemoWikiPath
            });
        });

        builder.ConfigureServices(services =>
        {
            ReplaceSingleton<IEmbeddingEngine>(services, new DeterministicEmbeddingEngine());

            foreach (var d in services.Where(d => d.ServiceType == typeof(OllamaAdapter)).ToList())
                services.Remove(d);

            services.AddHttpClient<OllamaAdapter>()
                .ConfigurePrimaryHttpMessageHandler(_ => OllamaHandler);
        });
    }

    private static void ReplaceSingleton<T>(IServiceCollection services, T instance)
        where T : class
    {
        foreach (var d in services.Where(d => d.ServiceType == typeof(T)).ToList())
            services.Remove(d);

        services.AddSingleton(instance);
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        if (Directory.Exists(_dataRoot))
        {
            try { Directory.Delete(_dataRoot, recursive: true); }
            catch { /* best effort */ }
        }
    }
}
