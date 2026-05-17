using System.Collections.Concurrent;
using System.Text.Json;
using ContextMemory.Core.Configuration;
using ContextMemory.Core.Contracts;
using ContextMemory.Core.Models;
using Microsoft.Extensions.Options;

namespace ContextMemory.Core.Profile;

public sealed class UserProfileStore : IUserProfileStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true
    };

    private static readonly TimeSpan FactExpiry = TimeSpan.FromDays(30);

    private readonly string _profilesRoot;
    private readonly ConcurrentDictionary<string, SemaphoreSlim> _locks = new();

    public UserProfileStore(IOptions<ContextMemoryOptions> options)
    {
        _profilesRoot = Path.Combine(
            Path.GetFullPath(options.Value.DataPath, options.Value.ContentRootPath),
            "user-profiles");
        Directory.CreateDirectory(_profilesRoot);
    }

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
        var path = GetFilePath(appId, userId);
        if (!File.Exists(path))
            return new UserProfileData();

        await using var stream = File.OpenRead(path);
        return await JsonSerializer
            .DeserializeAsync<UserProfileData>(stream, JsonOptions, cancellationToken)
            .ConfigureAwait(false)
            ?? new UserProfileData();
    }

    private async Task SaveAsync(
        string appId,
        string userId,
        UserProfileData profile,
        CancellationToken cancellationToken)
    {
        var appDir = Path.Combine(_profilesRoot, appId);
        Directory.CreateDirectory(appDir);
        var path = GetFilePath(appId, userId);

        await using var stream = File.Create(path);
        await JsonSerializer.SerializeAsync(stream, profile, JsonOptions, cancellationToken)
            .ConfigureAwait(false);
    }

    private string GetFilePath(string appId, string userId) =>
        Path.Combine(_profilesRoot, appId, $"{userId}.json");

    private SemaphoreSlim GetLock(string appId, string userId) =>
        _locks.GetOrAdd($"{appId}:{userId}", _ => new SemaphoreSlim(1, 1));
}
