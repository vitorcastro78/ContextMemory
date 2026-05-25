using ContextMemory.Core.Configuration;
using ContextMemory.Core.Contracts;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace ContextMemory.Api.Hosting;

public sealed class CompanyBrainSyncHostedService : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<CompanyBrainSyncHostedService> _logger;
    private readonly bool _enabled;
    private readonly TimeSpan _interval;

    public CompanyBrainSyncHostedService(
        IServiceScopeFactory scopeFactory,
        IOptions<ContextMemoryOptions> options,
        ILogger<CompanyBrainSyncHostedService> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
        var config = options.Value;
        _enabled = config.EnableCompanyBrainSync;
        _interval = TimeSpan.FromMinutes(Math.Max(5, config.CompanyBrainSyncIntervalMinutes));
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!_enabled)
        {
            _logger.LogInformation("Company Brain scheduled sync is disabled.");
            return;
        }

        _logger.LogInformation("Company Brain sync enabled. Interval: {IntervalMinutes} minutes.", _interval.TotalMinutes);

        using var timer = new PeriodicTimer(_interval);
        await SyncAllAsync(stoppingToken).ConfigureAwait(false);

        while (await timer.WaitForNextTickAsync(stoppingToken).ConfigureAwait(false))
            await SyncAllAsync(stoppingToken).ConfigureAwait(false);
    }

    private async Task SyncAllAsync(CancellationToken cancellationToken)
    {
        try
        {
            await using var scope = _scopeFactory.CreateAsyncScope();
            var store = scope.ServiceProvider.GetRequiredService<ICompanyBrainStore>();
            var service = scope.ServiceProvider.GetRequiredService<ICompanyBrainService>();

            foreach (var company in store.ListCompanies())
            {
                if (store.ListKnowledgeSources(company.CompanyId).All(s => !s.Enabled))
                    continue;

                try
                {
                    var result = await service.SyncAsync(company.CompanyId, cancellationToken).ConfigureAwait(false);
                    _logger.LogInformation(
                        "Company Brain sync for {CompanyId}: {Sources} source(s), {Processes} process(es).",
                        company.CompanyId,
                        result.SourcesSynced,
                        result.ProcessesUpserted);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Company Brain sync failed for {CompanyId}.", company.CompanyId);
                }
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // shutdown
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Company Brain sync loop failed.");
        }
    }
}
