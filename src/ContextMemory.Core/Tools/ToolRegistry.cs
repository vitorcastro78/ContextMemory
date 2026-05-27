using System.Collections.Concurrent;
using System.Diagnostics;
using ContextMemory.Core.Contracts;

namespace ContextMemory.Core.Tools;

public sealed class ToolRegistry : IToolRegistry
{
    private readonly ConcurrentDictionary<string, ConcurrentDictionary<string, ToolDefinition>> _tools = new();
    private readonly ITelemetryCollector _telemetry;

    public ToolRegistry(ITelemetryCollector telemetry) => _telemetry = telemetry;

    public void Register(string appId, ToolDefinition tool)
    {
        var appTools = _tools.GetOrAdd(appId, _ => new ConcurrentDictionary<string, ToolDefinition>(StringComparer.OrdinalIgnoreCase));
        appTools[tool.Name] = tool;
    }

    public IReadOnlyList<ToolDefinition> GetTools(string appId) =>
        _tools.TryGetValue(appId, out var appTools)
            ? appTools.Values.ToList()
            : [];

    public async Task<string> ExecuteAsync(
        string appId,
        string toolName,
        Dictionary<string, string> args,
        CancellationToken cancellationToken = default)
    {
        if (!_tools.TryGetValue(appId, out var appTools) || !appTools.TryGetValue(toolName, out var tool))
            throw new InvalidOperationException($"Tool '{toolName}' not registered for app '{appId}'.");

        var sw = Stopwatch.StartNew();
        try
        {
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            cts.CancelAfter(TimeSpan.FromSeconds(30));
            var result = await tool.Handler(args, cts.Token).ConfigureAwait(false);
            _telemetry.RecordToolCall(appId, toolName, success: true, sw.ElapsedMilliseconds);
            return result;
        }
        catch (Exception)
        {
            _telemetry.RecordToolCall(appId, toolName, success: false, sw.ElapsedMilliseconds);
            throw;
        }
    }
}
