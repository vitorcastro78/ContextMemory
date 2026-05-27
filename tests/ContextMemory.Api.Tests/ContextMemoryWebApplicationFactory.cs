using ContextMemory.Adapters;
using ContextMemory.Api.Tests.Fakes;
using ContextMemory.Core.Contracts;
using ContextMemory.Embeddings;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

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
        builder.UseEnvironment("Testing");
        builder.ConfigureAppConfiguration((_, config) =>
        {
            config.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ContextMemory:PersistenceProvider"] = "File",
                ["ContextMemory:DataPath"] = _dataRoot,
                ["ContextMemory:WikiPath"] = _dataRoot,
                ["ContextMemory:MasterKey"] = "test-master-key",
                ["ContextMemory:OllamaEndpoint"] = "http://ollama-stub",
                ["ContextMemory:Apps:kyc-dev:ApiKey"] = "test-api-key",
                ["ContextMemory:Apps:kyc-dev:SystemPrompt"] = "Test persona",
                ["ContextMemory:Apps:kyc-dev:DefaultLanguage"] = "pt-PT",
                ["ContextMemory:Apps:kyc-dev:WikiPath"] = _dataRoot
            });
        });

        builder.ConfigureServices(services =>
        {
            ReplaceSingleton<IEmbeddingEngine>(services, new DeterministicEmbeddingEngine());

            foreach (var d in services.Where(d => d.ServiceType == typeof(OllamaAdapter)).ToList())
                services.Remove(d);

            services.AddHttpClient<OllamaAdapter>()
                .ConfigurePrimaryHttpMessageHandler(_ => new StubOllamaHandler());
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
            catch { /* best effort cleanup */ }
        }
    }
}
