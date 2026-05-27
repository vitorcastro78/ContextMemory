using System.Collections.Concurrent;
using System.Text.Json;
using ContextMemory.Core.Configuration;
using ContextMemory.Core.Contracts;
using Microsoft.Extensions.Options;

namespace ContextMemory.Core.Billing;

public sealed class PlanStore : IPlanStore
{
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

    private readonly IAppConfigStore _appConfigStore;
    private readonly string _usageRoot;
    private readonly ContextMemoryOptions _options;
    private readonly ConcurrentDictionary<string, SemaphoreSlim> _locks = new();

    public PlanStore(IAppConfigStore appConfigStore, IOptions<ContextMemoryOptions> options)
    {
        _appConfigStore = appConfigStore;
        _options = options.Value;
        _usageRoot = Path.Combine(
            Path.GetFullPath(_options.DataPath, _options.ContentRootPath),
            "billing-usage");
        Directory.CreateDirectory(_usageRoot);
    }

    public Task<PlanDefinition> GetPlanAsync(string appId, CancellationToken cancellationToken = default)
    {
        var config = _appConfigStore.GetConfig(appId);
        var planId = string.IsNullOrWhiteSpace(config.PlanId) ? _options.DefaultPlan : config.PlanId;
        return Task.FromResult(PlanDefinition.GetBuiltIn(planId));
    }

    public async Task SetPlanAsync(string appId, string planId, CancellationToken cancellationToken = default)
    {
        await _appConfigStore.UpdateAsync(
            appId,
            new Models.AppConfigPatchRequest(),
            cancellationToken).ConfigureAwait(false);
        // Plan is stored via config PlanId — caller should patch config; simplified via direct file update in File mode
        var dir = Path.Combine(
            Path.GetFullPath(_options.DataPath, _options.ContentRootPath),
            "app-profiles",
            appId);
        var configPath = Path.Combine(dir, "config.json");
        if (!File.Exists(configPath))
            return;

        var gate = GetLock(appId);
        await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var json = await File.ReadAllTextAsync(configPath, cancellationToken).ConfigureAwait(false);
            using var doc = JsonDocument.Parse(json);
            var dict = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(json, JsonOptions)
                       ?? new Dictionary<string, JsonElement>();
            var updated = new Dictionary<string, object?>();
            foreach (var prop in doc.RootElement.EnumerateObject())
                updated[prop.Name] = prop.Value.Clone();

            updated["planId"] = planId;
            await File.WriteAllTextAsync(
                configPath,
                JsonSerializer.Serialize(updated, JsonOptions),
                cancellationToken).ConfigureAwait(false);
            await _appConfigStore.ReloadAsync(appId, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            gate.Release();
        }
    }

    public async Task<UsageSnapshot> GetDailyUsageAsync(string appId, CancellationToken cancellationToken = default)
    {
        var usage = await LoadUsageAsync(appId, cancellationToken).ConfigureAwait(false);
        return new UsageSnapshot
        {
            RequestsToday = usage.Requests,
            TokensToday = usage.Tokens,
            ResetAt = usage.DayUtc.AddDays(1)
        };
    }

    public async Task<bool> CheckQuotaAsync(string appId, int estimatedTokens, CancellationToken cancellationToken = default)
    {
        if (!_options.BillingEnabled)
            return true;

        var plan = await GetPlanAsync(appId, cancellationToken).ConfigureAwait(false);
        var usage = await LoadUsageAsync(appId, cancellationToken).ConfigureAwait(false);
        if (usage.Requests >= plan.DailyRequestLimit)
            return false;
        if (usage.Tokens + estimatedTokens > plan.DailyTokenLimit)
            return false;
        return true;
    }

    public async Task RecordUsageAsync(string appId, int requests, int tokens, CancellationToken cancellationToken = default)
    {
        if (!_options.BillingEnabled)
            return;

        var gate = GetLock(appId);
        await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var usage = await LoadUsageAsync(appId, cancellationToken).ConfigureAwait(false);
            usage.Requests += requests;
            usage.Tokens += tokens;
            await SaveUsageAsync(appId, usage, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            gate.Release();
        }
    }

    private async Task<DailyUsage> LoadUsageAsync(string appId, CancellationToken cancellationToken)
    {
        var today = DateTime.UtcNow.Date;
        var path = GetUsagePath(appId);
        if (!File.Exists(path))
            return new DailyUsage { DayUtc = today };

        var json = await File.ReadAllTextAsync(path, cancellationToken).ConfigureAwait(false);
        var usage = JsonSerializer.Deserialize<DailyUsage>(json, JsonOptions) ?? new DailyUsage();
        if (usage.DayUtc != today)
            return new DailyUsage { DayUtc = today };
        return usage;
    }

    private async Task SaveUsageAsync(string appId, DailyUsage usage, CancellationToken cancellationToken)
    {
        await File.WriteAllTextAsync(GetUsagePath(appId), JsonSerializer.Serialize(usage, JsonOptions), cancellationToken)
            .ConfigureAwait(false);
    }

    private string GetUsagePath(string appId) => Path.Combine(_usageRoot, $"{appId}.json");

    private SemaphoreSlim GetLock(string appId) => _locks.GetOrAdd(appId, _ => new SemaphoreSlim(1, 1));

    private sealed class DailyUsage
    {
        public DateTime DayUtc { get; set; } = DateTime.UtcNow.Date;
        public int Requests { get; set; }
        public long Tokens { get; set; }
    }
}
