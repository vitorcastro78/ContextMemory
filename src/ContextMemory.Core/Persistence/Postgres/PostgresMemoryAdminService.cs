using ContextMemory.Core.Contracts;
using ContextMemory.Core.Models;
using ContextMemory.Core.Security;
using Microsoft.EntityFrameworkCore;

namespace ContextMemory.Core.Persistence.Postgres;

public sealed class PostgresMemoryAdminService : IMemoryAdminService
{
    private readonly IDbContextFactory<ContextMemoryDbContext> _dbFactory;
    private readonly IUserProfileStore _userProfileStore;
    private readonly IConversationMemory _conversationMemory;

    public PostgresMemoryAdminService(
        IDbContextFactory<ContextMemoryDbContext> dbFactory,
        IUserProfileStore userProfileStore,
        IConversationMemory conversationMemory)
    {
        _dbFactory = dbFactory;
        _userProfileStore = userProfileStore;
        _conversationMemory = conversationMemory;
    }

    public async Task DeleteUserMemoryAsync(string appId, string userId, CancellationToken cancellationToken = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);

        var history = await db.ConversationHistories
            .FirstOrDefaultAsync(x => x.AppId == appId && x.UserId == userId, cancellationToken)
            .ConfigureAwait(false);
        if (history is not null)
            db.ConversationHistories.Remove(history);

        var profile = await db.UserProfiles
            .FirstOrDefaultAsync(x => x.AppId == appId && x.UserId == userId, cancellationToken)
            .ConfigureAwait(false);
        if (profile is not null)
            db.UserProfiles.Remove(profile);

        var semantic = await db.SemanticFacts
            .Where(x => x.AppId == appId && x.UserId == userId)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
        if (semantic.Count > 0)
            db.SemanticFacts.RemoveRange(semantic);

        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task<UserAdminDetail> GetUserDetailAsync(
        string appId,
        string userId,
        CancellationToken cancellationToken = default)
    {
        var profile = await _userProfileStore.GetProfileAsync(appId, userId, cancellationToken).ConfigureAwait(false);
        var count = await _conversationMemory.GetMessageCountAsync(appId, userId, cancellationToken).ConfigureAwait(false);

        await using var db = await _dbFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        var hasSemantic = await db.SemanticFacts
            .AnyAsync(x => x.AppId == appId && x.UserId == userId, cancellationToken)
            .ConfigureAwait(false);

        return new UserAdminDetail
        {
            UserId = userId,
            Profile = profile,
            ConversationMessageCount = count,
            HasSemanticMemory = hasSemantic
        };
    }

    public async Task<IReadOnlyList<UserAdminSummary>> ListUsersAsync(
        string appId,
        CancellationToken cancellationToken = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);

        var fromProfiles = await db.UserProfiles
            .AsNoTracking()
            .Where(x => x.AppId == appId)
            .Select(x => x.UserId)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var fromHistory = await db.ConversationHistories
            .AsNoTracking()
            .Where(x => x.AppId == appId)
            .Select(x => x.UserId)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var userIds = fromProfiles.Concat(fromHistory).Distinct(StringComparer.Ordinal);
        var summaries = new List<UserAdminSummary>();

        foreach (var userId in userIds)
        {
            if (!IdentifierValidator.IsValid(userId))
                continue;

            var profile = await _userProfileStore.GetProfileAsync(appId, userId, cancellationToken).ConfigureAwait(false);
            summaries.Add(new UserAdminSummary
            {
                UserId = userId,
                FactCount = profile.Facts.Count,
                HasSessionContext = !string.IsNullOrWhiteSpace(profile.SessionContext)
            });
        }

        return summaries;
    }
}
