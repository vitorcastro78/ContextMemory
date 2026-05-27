using ContextMemory.Core.Billing;

namespace ContextMemory.Core.Contracts;

public interface IPlanStore
{
    Task<PlanDefinition> GetPlanAsync(string appId, CancellationToken cancellationToken = default);
    Task SetPlanAsync(string appId, string planId, CancellationToken cancellationToken = default);
    Task<UsageSnapshot> GetDailyUsageAsync(string appId, CancellationToken cancellationToken = default);
    Task<bool> CheckQuotaAsync(string appId, int estimatedTokens, CancellationToken cancellationToken = default);
    Task RecordUsageAsync(string appId, int requests, int tokens, CancellationToken cancellationToken = default);
}
