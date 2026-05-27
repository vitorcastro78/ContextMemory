using ContextMemory.Core.Configuration;
using ContextMemory.Core.Contracts;
using Microsoft.Extensions.Options;

namespace ContextMemory.Api.Hosting;

public sealed class KnowledgeLoopBackgroundService : BackgroundService
{
    private readonly IServiceProvider _services;
    private readonly ContextMemoryOptions _options;

    public KnowledgeLoopBackgroundService(
        IServiceProvider services,
        IOptions<ContextMemoryOptions> options)
    {
        _services = services;
        _options = options.Value;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var interval = TimeSpan.FromHours(Math.Max(1, _options.KnowledgeLoopProcessIntervalHours));

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using var scope = _services.CreateScope();
                var registry = scope.ServiceProvider.GetRequiredService<IAppRegistry>();
                var loop = scope.ServiceProvider.GetRequiredService<IKnowledgeLoop>();

                foreach (var app in registry.GetAllApps())
                {
                    if (stoppingToken.IsCancellationRequested)
                        break;

                    await loop.ProcessPendingAsync(app.AppId, stoppingToken).ConfigureAwait(false);
                }
            }
            catch (Exception ex)
            {
                var logger = _services.GetRequiredService<ILogger<KnowledgeLoopBackgroundService>>();
                logger.LogError(ex, "KnowledgeLoop background processing failed");
            }

            await Task.Delay(interval, stoppingToken).ConfigureAwait(false);
        }
    }
}
