using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using ContextMemory.Core.Configuration;
using ContextMemory.Core.Contracts;
using ContextMemory.Core.Models;
using MemoryPack;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace ContextMemory.Core.Knowledge;

public sealed class VectorStore
{
    private readonly ConcurrentDictionary<string, List<VectorEntry>> _stores = new();
    private readonly ConcurrentDictionary<string, SemaphoreSlim> _locks = new();
    private readonly string _cacheRoot;
    private readonly ILogger<VectorStore> _logger;

    public VectorStore(IOptions<ContextMemoryOptions> options, ILogger<VectorStore> logger)
    {
        _cacheRoot = Path.Combine(
            Path.GetFullPath(options.Value.DataPath, options.Value.ContentRootPath),
            "vector-cache");
        Directory.CreateDirectory(_cacheRoot);
        _logger = logger;
    }

    public IReadOnlyList<VectorEntry> GetEntries(string appId) =>
        _stores.TryGetValue(appId, out var entries) ? entries : [];

    public async Task<bool> TryLoadFromCacheAsync(
        string appId,
        string wikiPath,
        CancellationToken cancellationToken = default)
    {
        var gate = GetLock(appId);
        await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var hashPath = GetHashPath(appId);
            var cachePath = GetCachePath(appId);
            if (!File.Exists(hashPath) || !File.Exists(cachePath))
                return false;

            var currentHash = await ComputeWikiHashAsync(wikiPath, cancellationToken).ConfigureAwait(false);
            var storedHash = await File.ReadAllTextAsync(hashPath, cancellationToken).ConfigureAwait(false);
            if (!string.Equals(currentHash, storedHash.Trim(), StringComparison.Ordinal))
                return false;

            await using var stream = File.OpenRead(cachePath);
            var payload = await MemoryPackSerializer
                .DeserializeAsync<VectorCachePayload>(stream, cancellationToken: cancellationToken)
                .ConfigureAwait(false);

            if (payload is null || !string.Equals(payload.AppId, appId, StringComparison.Ordinal))
                return false;

            var entries = payload.Entries
                .Select(e => new VectorEntry
                {
                    AppId = appId,
                    Text = e.Text,
                    Source = e.Source,
                    Vector = e.Vector
                })
                .ToList();

            _stores[appId] = entries;
            _logger.LogInformation("Vector cache loaded for {AppId} ({Count} entries)", appId, entries.Count);
            return true;
        }
        finally
        {
            gate.Release();
        }
    }

    public async Task ReplaceFileEntriesAsync(
        string appId,
        string source,
        IReadOnlyList<VectorEntry> newEntries,
        string wikiPath,
        CancellationToken cancellationToken = default)
    {
        var gate = GetLock(appId);
        await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var entries = _stores.GetOrAdd(appId, _ => []);
            lock (entries)
            {
                entries.RemoveAll(e => string.Equals(e.Source, source, StringComparison.OrdinalIgnoreCase));
                entries.AddRange(newEntries);
            }

            await SaveCacheAsync(appId, wikiPath, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            gate.Release();
        }
    }

    public async Task ReplaceAllEntriesAsync(
        string appId,
        IReadOnlyList<VectorEntry> newEntries,
        string wikiPath,
        CancellationToken cancellationToken = default)
    {
        var gate = GetLock(appId);
        await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            _stores[appId] = newEntries.ToList();
            await SaveCacheAsync(appId, wikiPath, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            gate.Release();
        }
    }

    private async Task SaveCacheAsync(string appId, string wikiPath, CancellationToken cancellationToken)
    {
        if (!_stores.TryGetValue(appId, out var entries))
            return;

        var payload = new VectorCachePayload
        {
            AppId = appId,
            Entries = entries
                .Select(e => new VectorCacheEntry
                {
                    Text = e.Text,
                    Source = e.Source,
                    Vector = e.Vector
                })
                .ToList()
        };

        var cachePath = GetCachePath(appId);
        await using (var stream = File.Create(cachePath))
        {
            await MemoryPackSerializer
                .SerializeAsync(stream, payload, cancellationToken: cancellationToken)
                .ConfigureAwait(false);
        }

        var hash = await ComputeWikiHashAsync(wikiPath, cancellationToken).ConfigureAwait(false);
        await File.WriteAllTextAsync(GetHashPath(appId), hash, cancellationToken).ConfigureAwait(false);
        _logger.LogInformation("Vector cache saved for {AppId} ({Count} entries)", appId, entries.Count);
    }

    internal static async Task<string> ComputeWikiHashAsync(string wikiPath, CancellationToken cancellationToken)
    {
        if (!Directory.Exists(wikiPath))
            return string.Empty;

        var files = Directory
            .EnumerateFiles(wikiPath, "*.md", SearchOption.AllDirectories)
            .OrderBy(f => f, StringComparer.OrdinalIgnoreCase)
            .ToList();

        var builder = new StringBuilder();
        foreach (var file in files)
        {
            cancellationToken.ThrowIfCancellationRequested();
            builder.Append(Path.GetRelativePath(wikiPath, file).Replace('\\', '/'));
            builder.Append('|');
            await using var stream = File.OpenRead(file);
            var hash = await SHA256.HashDataAsync(stream, cancellationToken).ConfigureAwait(false);
            builder.Append(Convert.ToHexString(hash));
            builder.Append('\n');
        }

        var aggregate = SHA256.HashData(Encoding.UTF8.GetBytes(builder.ToString()));
        return Convert.ToHexString(aggregate);
    }

    private SemaphoreSlim GetLock(string appId) =>
        _locks.GetOrAdd(appId, _ => new SemaphoreSlim(1, 1));

    private string GetCachePath(string appId) => Path.Combine(_cacheRoot, $"{appId}.bin");
    private string GetHashPath(string appId) => Path.Combine(_cacheRoot, $"{appId}.hash");
}
