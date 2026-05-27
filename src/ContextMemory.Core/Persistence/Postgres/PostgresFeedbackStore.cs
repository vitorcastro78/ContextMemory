using ContextMemory.Core.Contracts;
using ContextMemory.Core.Models;
using Microsoft.EntityFrameworkCore;

namespace ContextMemory.Core.Persistence.Postgres;

public sealed class PostgresFeedbackStore : IFeedbackStore
{
    private readonly IDbContextFactory<ContextMemoryDbContext> _dbFactory;

    public PostgresFeedbackStore(IDbContextFactory<ContextMemoryDbContext> dbFactory) =>
        _dbFactory = dbFactory;

    public async Task RecordAsync(FeedbackEntry entry, CancellationToken cancellationToken = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        db.Feedback.Add(new FeedbackEntity
        {
            MessageId = entry.MessageId,
            AppId = entry.AppId,
            UserId = entry.UserId,
            Score = entry.Score,
            Reason = entry.Reason,
            Timestamp = entry.Timestamp,
            IsImplicit = entry.IsImplicit
        });
        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<FeedbackEntry>> GetByAppAsync(
        string appId,
        CancellationToken cancellationToken = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        var rows = await db.Feedback
            .AsNoTracking()
            .Where(f => f.AppId == appId)
            .OrderBy(f => f.Timestamp)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        return rows.Select(r => new FeedbackEntry
        {
            MessageId = r.MessageId,
            AppId = r.AppId,
            UserId = r.UserId,
            Score = r.Score,
            Reason = r.Reason,
            Timestamp = r.Timestamp,
            IsImplicit = r.IsImplicit
        }).ToList();
    }

    public async Task<double> GetAverageScoreAsync(string appId, CancellationToken cancellationToken = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        var avg = await db.Feedback
            .Where(f => f.AppId == appId)
            .AverageAsync(f => (double?)f.Score, cancellationToken)
            .ConfigureAwait(false);

        return avg ?? 0;
    }

    public async Task<int> CountNegativeByReasonAsync(
        string appId,
        string reasonContains,
        CancellationToken cancellationToken = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        return await db.Feedback
            .Where(f => f.AppId == appId && f.Score < 0 && f.Reason != null && f.Reason.Contains(reasonContains))
            .CountAsync(cancellationToken)
            .ConfigureAwait(false);
    }
}
