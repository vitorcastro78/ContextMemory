using System.Collections.Concurrent;
using System.Text.Json;
using ContextMemory.Core.Contracts;
using ContextMemory.Core.Models;
using Microsoft.EntityFrameworkCore;

namespace ContextMemory.Core.Persistence.Postgres;

public sealed class PostgresUserProfileStore : IUserProfileStore
{
    private static readonly TimeSpan FactExpiry = TimeSpan.FromDays(30);
    private readonly IDbContextFactory<ContextMemoryDbContext> _dbFactory;
    private readonly ConcurrentDictionary<string, SemaphoreSlim> _locks = new();

    public PostgresUserProfileStore(IDbContextFactory<ContextMemoryDbContext> dbFactory) =>
        _dbFactory = dbFactory;

    public async Task<UserProfileData> GetProfileAsync(
        string appId,
        string userId,
        CancellationToken cancellationToken = default)
    {
        var gate = GetLock(appId, userId);
        await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var profile = await LoadAsync(appId, userId, cancellationToken).ConfigureAwait(false);
            var activeFacts = profile.Facts
                .Where(f => DateTimeOffset.UtcNow - f.LastConfirmedAt < FactExpiry)
                .OrderByDescending(f => f.LastConfirmedAt)
                .ToList();

            if (activeFacts.Count == profile.Facts.Count)
                return profile;

            var pruned = profile with { Facts = activeFacts };
            await SaveAsync(appId, userId, pruned, cancellationToken).ConfigureAwait(false);
            return pruned;
        }
        finally
        {
            gate.Release();
        }
    }

    public async Task AddOrConfirmFactsAsync(
        string appId,
        string userId,
        IEnumerable<string> factTexts,
        float confidence,
        CancellationToken cancellationToken = default)
    {
        var gate = GetLock(appId, userId);
        await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var profile = await LoadAsync(appId, userId, cancellationToken).ConfigureAwait(false);
            var now = DateTimeOffset.UtcNow;
            var facts = profile.Facts.ToList();

            foreach (var text in factTexts.Where(t => !string.IsNullOrWhiteSpace(t)).Select(t => t.Trim()))
            {
                var existing = facts.FirstOrDefault(f =>
                    string.Equals(f.Text, text, StringComparison.OrdinalIgnoreCase));

                if (existing is not null)
                {
                    facts.Remove(existing);
                    facts.Add(existing with
                    {
                        LastConfirmedAt = now,
                        Confidence = Math.Min(1f, existing.Confidence + 0.1f)
                    });
                }
                else
                {
                    facts.Add(new UserFact
                    {
                        Text = text,
                        LearnedAt = now,
                        LastConfirmedAt = now,
                        Confidence = confidence
                    });
                }
            }

            await SaveAsync(appId, userId, profile with { Facts = facts }, cancellationToken)
                .ConfigureAwait(false);
        }
        finally
        {
            gate.Release();
        }
    }

    public async Task SetSessionContextAsync(
        string appId,
        string userId,
        string? sessionContext,
        CancellationToken cancellationToken = default)
    {
        var gate = GetLock(appId, userId);
        await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var profile = await LoadAsync(appId, userId, cancellationToken).ConfigureAwait(false);
            await SaveAsync(appId, userId, profile with { SessionContext = sessionContext }, cancellationToken)
                .ConfigureAwait(false);
        }
        finally
        {
            gate.Release();
        }
    }

    private async Task<UserProfileData> LoadAsync(
        string appId,
        string userId,
        CancellationToken cancellationToken)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        var row = await db.UserProfiles
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.AppId == appId && x.UserId == userId, cancellationToken)
            .ConfigureAwait(false);

        if (row is null)
            return new UserProfileData();

        var facts = JsonSerializer.Deserialize<List<UserFact>>(row.FactsJson, PostgresJson.CamelCase) ?? [];
        return new UserProfileData
        {
            SessionContext = row.SessionContext,
            Facts = facts
        };
    }

    private async Task SaveAsync(
        string appId,
        string userId,
        UserProfileData profile,
        CancellationToken cancellationToken)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        var row = await db.UserProfiles
            .FirstOrDefaultAsync(x => x.AppId == appId && x.UserId == userId, cancellationToken)
            .ConfigureAwait(false);

        var json = JsonSerializer.Serialize(profile.Facts, PostgresJson.CamelCase);
        if (row is null)
        {
            db.UserProfiles.Add(new UserProfileEntity
            {
                AppId = appId,
                UserId = userId,
                SessionContext = profile.SessionContext,
                FactsJson = json
            });
        }
        else
        {
            row.SessionContext = profile.SessionContext;
            row.FactsJson = json;
        }

        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }

    private SemaphoreSlim GetLock(string appId, string userId) =>
        _locks.GetOrAdd($"{appId}:{userId}", _ => new SemaphoreSlim(1, 1));
}
