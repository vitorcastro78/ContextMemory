using ContextMemory.Core.Contracts;

namespace ContextMemory.Core.Billing;

public sealed class QuotaEnforcer
{
    private readonly IPlanStore _planStore;
    private readonly ITelemetryCollector _telemetry;

    public QuotaEnforcer(IPlanStore planStore, ITelemetryCollector telemetry)
    {
        _planStore = planStore;
        _telemetry = telemetry;
    }

    public async Task<QuotaCheckResult> CheckAsync(
        string appId,
        int estimatedTokens,
        CancellationToken cancellationToken = default)
    {
        var allowed = await _planStore.CheckQuotaAsync(appId, estimatedTokens, cancellationToken).ConfigureAwait(false);
        if (allowed)
        {
            var usage = await _planStore.GetDailyUsageAsync(appId, cancellationToken).ConfigureAwait(false);
            var plan = await _planStore.GetPlanAsync(appId, cancellationToken).ConfigureAwait(false);
            return new QuotaCheckResult
            {
                Allowed = true,
                RemainingRequests = Math.Max(0, plan.DailyRequestLimit - usage.RequestsToday),
                RemainingTokens = Math.Max(0, plan.DailyTokenLimit - usage.TokensToday),
                ResetAt = usage.ResetAt
            };
        }

        _telemetry.RecordQuotaExceeded(appId, "daily_quota_exceeded");
        var snapshot = await _planStore.GetDailyUsageAsync(appId, cancellationToken).ConfigureAwait(false);
        return new QuotaCheckResult
        {
            Allowed = false,
            Reason = "daily_quota_exceeded",
            ResetAt = snapshot.ResetAt
        };
    }
}

public record QuotaCheckResult
{
    public bool Allowed { get; init; }
    public string? Reason { get; init; }
    public int RemainingRequests { get; init; }
    public long RemainingTokens { get; init; }
    public DateTimeOffset ResetAt { get; init; }
}
