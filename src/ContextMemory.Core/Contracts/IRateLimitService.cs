using ContextMemory.Core.Models;

namespace ContextMemory.Core.Contracts;

public interface IRateLimitService
{
    RateLimitAcquireResult TryAcquire(string appId, string userId, int estimatedTokens, RateLimitConfig config);
}

public sealed class RateLimitAcquireResult
{
    public bool IsAcquired { get; init; }
    public int RetryAfterSeconds { get; init; }

    public static RateLimitAcquireResult Success() => new() { IsAcquired = true };
    public static RateLimitAcquireResult Rejected(int retryAfterSeconds) =>
        new() { IsAcquired = false, RetryAfterSeconds = retryAfterSeconds };
}
