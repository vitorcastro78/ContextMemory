using ContextMemory.Core.Models;

namespace ContextMemory.Core.Contracts;

public interface IFeedbackStore
{
    Task RecordAsync(FeedbackEntry entry, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<FeedbackEntry>> GetByAppAsync(string appId, CancellationToken cancellationToken = default);
    Task<double> GetAverageScoreAsync(string appId, CancellationToken cancellationToken = default);
    Task<int> CountNegativeByReasonAsync(string appId, string reasonContains, CancellationToken cancellationToken = default);
}
