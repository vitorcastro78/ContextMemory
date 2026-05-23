using System.Collections.Concurrent;
using ContextMemory.Core.Configuration;
using ContextMemory.Core.Contracts;
using ContextMemory.Core.Profile;
using Microsoft.Extensions.Options;

namespace ContextMemory.Api.Hosting;

public sealed class WikiWatcherHostedService : IHostedService, IDisposable
{
    private readonly IWikiIndexService _wikiIndex;
    private readonly IAppRegistry _appRegistry;
    private readonly ContextMemoryOptions _options;
    private readonly ILogger<WikiWatcherHostedService> _logger;
    private readonly ConcurrentDictionary<string, Timer> _debounceTimers = new();
    private readonly List<FileSystemWatcher> _watchers = [];
    private readonly TimeSpan _debounceDelay = TimeSpan.FromMilliseconds(500);

    public WikiWatcherHostedService(
        IWikiIndexService wikiIndex,
        IAppRegistry appRegistry,
        IOptions<ContextMemoryOptions> options,
        ILogger<WikiWatcherHostedService> logger)
    {
        _wikiIndex = wikiIndex;
        _appRegistry = appRegistry;
        _options = options.Value;
        _logger = logger;
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        foreach (var profile in _appRegistry.GetAllApps())
        {
            var appId = profile.AppId;
            var wikiPath = profile.WikiPath;
            if (!Directory.Exists(wikiPath))
            {
                _logger.LogWarning("Wiki directory missing for {AppId}: {WikiPath}", appId, wikiPath);
                continue;
            }

            await _wikiIndex.EnsureIndexedAsync(appId, wikiPath, cancellationToken).ConfigureAwait(false);
            RegisterWatcher(appId, wikiPath);
        }
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        foreach (var watcher in _watchers.ToArray())
            watcher.Dispose();

        _watchers.Clear();

        foreach (var timer in _debounceTimers.Values.ToArray())
            timer.Dispose();

        _debounceTimers.Clear();
        return Task.CompletedTask;
    }

    private void RegisterWatcher(string appId, string wikiPath)
    {
        var watcher = new FileSystemWatcher(wikiPath)
        {
            IncludeSubdirectories = true,
            Filter = "*.md",
            NotifyFilter = NotifyFilters.FileName
                | NotifyFilters.LastWrite
                | NotifyFilters.CreationTime
                | NotifyFilters.Size
        };

        FileSystemEventHandler handler = (_, args) =>
            ScheduleReindex(appId, wikiPath, args.FullPath);

        RenamedEventHandler renameHandler = (_, args) =>
        {
            if (!string.IsNullOrEmpty(args.OldFullPath))
                ScheduleReindex(appId, wikiPath, args.OldFullPath);
            ScheduleReindex(appId, wikiPath, args.FullPath);
        };

        watcher.Changed += handler;
        watcher.Created += handler;
        watcher.Deleted += handler;
        watcher.Renamed += renameHandler;
        watcher.EnableRaisingEvents = true;
        _watchers.Add(watcher);

        _logger.LogInformation("FileSystemWatcher active for {AppId} at {WikiPath}", appId, wikiPath);
    }

    private void ScheduleReindex(string appId, string wikiPath, string fullPath)
    {
        if (!fullPath.EndsWith(".md", StringComparison.OrdinalIgnoreCase))
            return;

        var key = $"{appId}:{fullPath}";

        if (_debounceTimers.TryGetValue(key, out var existing))
        {
            existing.Change(_debounceDelay, Timeout.InfiniteTimeSpan);
            return;
        }

        var timer = new Timer(
            _ => _ = ReindexAsync(appId, wikiPath, fullPath),
            null,
            _debounceDelay,
            Timeout.InfiniteTimeSpan);

        if (!_debounceTimers.TryAdd(key, timer))
            timer.Dispose();
    }

    private async Task ReindexAsync(string appId, string wikiPath, string fullPath)
    {
        try
        {
            if (!File.Exists(fullPath) && !Directory.Exists(Path.GetDirectoryName(fullPath)!))
                return;

            var relative = Path.GetRelativePath(wikiPath, fullPath).Replace('\\', '/');
            await _wikiIndex.ReindexFileAsync(appId, wikiPath, relative, CancellationToken.None)
                .ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to re-index wiki file {File} for {AppId}", fullPath, appId);
        }
    }

    public void Dispose()
    {
        foreach (var watcher in _watchers)
            watcher.Dispose();
        _watchers.Clear();

        foreach (var timer in _debounceTimers.Values)
            timer.Dispose();
        _debounceTimers.Clear();
    }
}
