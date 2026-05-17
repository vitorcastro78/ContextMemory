using System.Text.Json;
using ContextMemory.Core.Configuration;
using ContextMemory.Core.Contracts;
using ContextMemory.Core.Models;
using Microsoft.Extensions.Options;

namespace ContextMemory.Core.Profile;

public sealed class MemoryAdminService : IMemoryAdminService
{
    private readonly string _dataRoot;
    private readonly IUserProfileStore _userProfileStore;
    private readonly IConversationMemory _conversationMemory;

    public MemoryAdminService(
        IOptions<ContextMemoryOptions> options,
        IUserProfileStore userProfileStore,
        IConversationMemory conversationMemory)
    {
        var config = options.Value;
        _dataRoot = Path.GetFullPath(config.DataPath, config.ContentRootPath);
        _userProfileStore = userProfileStore;
        _conversationMemory = conversationMemory;
    }

    public async Task DeleteUserMemoryAsync(string appId, string userId, CancellationToken cancellationToken = default)
    {
        var historyPath = Path.Combine(_dataRoot, "conversation-history", appId, $"{userId}.json");
        var profilePath = Path.Combine(_dataRoot, "user-profiles", appId, $"{userId}.json");
        var semanticPath = Path.Combine(_dataRoot, "user-profiles", appId, $"{userId}.semantic.bin");

        if (File.Exists(historyPath))
            File.Delete(historyPath);
        if (File.Exists(profilePath))
            File.Delete(profilePath);
        if (File.Exists(semanticPath))
            File.Delete(semanticPath);

        await Task.CompletedTask;
    }

    public async Task<UserAdminDetail> GetUserDetailAsync(
        string appId,
        string userId,
        CancellationToken cancellationToken = default)
    {
        var profile = await _userProfileStore.GetProfileAsync(appId, userId, cancellationToken).ConfigureAwait(false);
        var count = await _conversationMemory.GetMessageCountAsync(appId, userId, cancellationToken).ConfigureAwait(false);
        var semanticPath = Path.Combine(_dataRoot, "user-profiles", appId, $"{userId}.semantic.bin");

        return new UserAdminDetail
        {
            UserId = userId,
            Profile = profile,
            ConversationMessageCount = count,
            HasSemanticMemory = File.Exists(semanticPath)
        };
    }

    public async Task<IReadOnlyList<UserAdminSummary>> ListUsersAsync(
        string appId,
        CancellationToken cancellationToken = default)
    {
        var usersDir = Path.Combine(_dataRoot, "user-profiles", appId);
        if (!Directory.Exists(usersDir))
            return [];

        var summaries = new List<UserAdminSummary>();
        foreach (var file in Directory.EnumerateFiles(usersDir, "*.json"))
        {
            var userId = Path.GetFileNameWithoutExtension(file);
            if (!Security.IdentifierValidator.IsValid(userId))
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
