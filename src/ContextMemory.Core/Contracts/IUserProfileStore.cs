using ContextMemory.Core.Models;

namespace ContextMemory.Core.Contracts;

public interface IUserProfileStore
{
    Task<UserProfileData> GetProfileAsync(
        string appId,
        string userId,
        CancellationToken cancellationToken = default);

    Task AddOrConfirmFactsAsync(
        string appId,
        string userId,
        IEnumerable<string> factTexts,
        float confidence,
        CancellationToken cancellationToken = default);

    Task SetSessionContextAsync(
        string appId,
        string userId,
        string? sessionContext,
        CancellationToken cancellationToken = default);
}
