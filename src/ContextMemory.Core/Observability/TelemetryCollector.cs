using System.Collections.Concurrent;
using System.Text;
using ContextMemory.Core.Configuration;
using ContextMemory.Core.Contracts;
using Microsoft.Extensions.Options;

namespace ContextMemory.Core.Observability;

public sealed class TelemetryCollector : ITelemetryCollector
{
    private readonly TimeSpan _activeUserWindow;

    private readonly ConcurrentDictionary<string, AppMetrics> _apps = new(StringComparer.Ordinal);

    public TelemetryCollector(IOptions<ContextMemoryOptions> options)
    {
        var minutes = Math.Max(1, options.Value.ActiveUserWindowMinutes);
        _activeUserWindow = TimeSpan.FromMinutes(minutes);
    }

    public void RecordRequest(
        string appId,
        string userId,
        int statusCode,
        double latencyMs,
        int promptTokens,
        int completionTokens,
        bool ragHit)
    {
        RecordUserActivity(appId, userId);

        var metrics = _apps.GetOrAdd(appId, _ => new AppMetrics());
        Interlocked.Increment(ref metrics.RequestsTotal);
        if (statusCode >= 400)
            Interlocked.Increment(ref metrics.RequestsError);

        Interlocked.Add(ref metrics.TokensPrompt, promptTokens);
        Interlocked.Add(ref metrics.TokensCompletion, completionTokens);
        if (ragHit)
            Interlocked.Increment(ref metrics.RagHits);

        lock (metrics.LatencyLock)
        {
            metrics.LatencySamples.Add(latencyMs);
            if (metrics.LatencySamples.Count > 1000)
                metrics.LatencySamples.RemoveAt(0);
        }
    }

    public void RecordUserActivity(string appId, string userId)
    {
        if (string.IsNullOrWhiteSpace(appId) || string.IsNullOrWhiteSpace(userId))
            return;

        var metrics = _apps.GetOrAdd(appId, _ => new AppMetrics());
        metrics.ActiveUsers[userId] = DateTimeOffset.UtcNow;
        PruneInactiveUsers(metrics);
    }

    public void RecordFeedback(string appId, int score)
    {
        var metrics = _apps.GetOrAdd(appId, _ => new AppMetrics());
        lock (metrics.FeedbackLock)
        {
            metrics.FeedbackScores.Add(score);
            if (metrics.FeedbackScores.Count > 500)
                metrics.FeedbackScores.RemoveAt(0);
        }
    }

    public void RecordContentFiltered(string appId, string reason)
    {
        var metrics = _apps.GetOrAdd(appId, _ => new AppMetrics());
        metrics.FilteredByReason.AddOrUpdate(reason, 1, (_, c) => c + 1);
    }

    public void RecordKnowledgeLoopEvaluated(string appId) =>
        Interlocked.Increment(ref _apps.GetOrAdd(appId, _ => new AppMetrics()).KlEvaluated);

    public void RecordKnowledgeLoopApproved(string appId) =>
        Interlocked.Increment(ref _apps.GetOrAdd(appId, _ => new AppMetrics()).KlApproved);

    public void RecordKnowledgeLoopRejected(string appId) =>
        Interlocked.Increment(ref _apps.GetOrAdd(appId, _ => new AppMetrics()).KlRejected);

    public void RecordKnowledgeLoopChunkCreated(string appId) =>
        Interlocked.Increment(ref _apps.GetOrAdd(appId, _ => new AppMetrics()).KlChunksCreated);

    public void RecordKnowledgeLoopChunkMerged(string appId) =>
        Interlocked.Increment(ref _apps.GetOrAdd(appId, _ => new AppMetrics()).KlChunksMerged);

    public void RecordToolCall(string appId, string toolName, bool success, double durationMs)
    {
        var metrics = _apps.GetOrAdd(appId, _ => new AppMetrics());
        var key = $"{toolName}:{(success ? "success" : "error")}";
        metrics.ToolCalls.AddOrUpdate(key, 1, (_, c) => c + 1);
    }

    public void RecordQuotaExceeded(string appId, string reason)
    {
        var metrics = _apps.GetOrAdd(appId, _ => new AppMetrics());
        metrics.QuotaExceeded.AddOrUpdate(reason, 1, (_, c) => c + 1);
    }

    public AppTelemetrySnapshot GetAppSnapshot(string appId)
    {
        if (!_apps.TryGetValue(appId, out var m))
            return new AppTelemetrySnapshot();

        PruneInactiveUsers(m);

        return new AppTelemetrySnapshot
        {
            RequestsTotal = m.RequestsTotal,
            RequestsError = m.RequestsError,
            TokensPrompt = m.TokensPrompt,
            TokensCompletion = m.TokensCompletion,
            RagHits = m.RagHits,
            AvgLatencyMs = m.LatencySamples.Count > 0 ? m.LatencySamples.Average() : 0,
            FeedbackScoreAvg = m.FeedbackScores.Count > 0 ? m.FeedbackScores.Average() : 0,
            ActiveUsers = m.ActiveUsers.Count,
            FilteredByReason = m.FilteredByReason.ToDictionary(k => k.Key, v => v.Value)
        };
    }

    public IReadOnlyDictionary<string, AppTelemetrySnapshot> GetAllSnapshots() =>
        _apps.Keys.ToDictionary(k => k, GetAppSnapshot, StringComparer.Ordinal);

    public string ExportPrometheus()
    {
        var sb = new StringBuilder();
        foreach (var (appId, m) in _apps)
        {
            PruneInactiveUsers(m);
            var label = EscapeLabel(appId);
            sb.AppendLine($"cm_requests_total{{appId=\"{label}\",status=\"success\"}} {m.RequestsTotal - m.RequestsError}");
            sb.AppendLine($"cm_requests_total{{appId=\"{label}\",status=\"error\"}} {m.RequestsError}");
            sb.AppendLine($"cm_tokens_prompt_total{{appId=\"{label}\"}} {m.TokensPrompt}");
            sb.AppendLine($"cm_tokens_completion_total{{appId=\"{label}\"}} {m.TokensCompletion}");
            sb.AppendLine($"cm_rag_hits_total{{appId=\"{label}\"}} {m.RagHits}");
            sb.AppendLine($"cm_active_users{{appId=\"{label}\"}} {m.ActiveUsers.Count}");

            var p50 = Percentile(m.LatencySamples, 0.5);
            var p95 = Percentile(m.LatencySamples, 0.95);
            var p99 = Percentile(m.LatencySamples, 0.99);
            sb.AppendLine($"cm_latency_ms{{appId=\"{label}\",percentile=\"p50\"}} {p50:F0}");
            sb.AppendLine($"cm_latency_ms{{appId=\"{label}\",percentile=\"p95\"}} {p95:F0}");
            sb.AppendLine($"cm_latency_ms{{appId=\"{label}\",percentile=\"p99\"}} {p99:F0}");

            var avgFeedback = m.FeedbackScores.Count > 0 ? m.FeedbackScores.Average() : 0;
            sb.AppendLine($"cm_feedback_score{{appId=\"{label}\"}} {avgFeedback:F2}");

            foreach (var (reason, count) in m.FilteredByReason)
                sb.AppendLine($"cm_content_filtered_total{{appId=\"{label}\",reason=\"{EscapeLabel(reason)}\"}} {count}");

            sb.AppendLine($"cm_knowledge_loop_sessions_evaluated_total{{appId=\"{label}\"}} {m.KlEvaluated}");
            sb.AppendLine($"cm_knowledge_loop_sessions_approved_total{{appId=\"{label}\"}} {m.KlApproved}");
            sb.AppendLine($"cm_knowledge_loop_sessions_rejected_total{{appId=\"{label}\"}} {m.KlRejected}");
            sb.AppendLine($"cm_knowledge_loop_chunks_created_total{{appId=\"{label}\"}} {m.KlChunksCreated}");
            sb.AppendLine($"cm_knowledge_loop_chunks_merged_total{{appId=\"{label}\"}} {m.KlChunksMerged}");

            foreach (var (toolKey, count) in m.ToolCalls)
            {
                var parts = toolKey.Split(':', 2);
                var toolName = parts.Length > 0 ? parts[0] : "unknown";
                var status = parts.Length > 1 ? parts[1] : "success";
                sb.AppendLine($"cm_tool_calls_total{{appId=\"{label}\",tool_name=\"{EscapeLabel(toolName)}\",status=\"{status}\"}} {count}");
            }

            foreach (var (reason, count) in m.QuotaExceeded)
                sb.AppendLine($"cm_quota_exceeded_total{{appId=\"{label}\",reason=\"{EscapeLabel(reason)}\"}} {count}");
        }

        return sb.ToString();
    }

    private void PruneInactiveUsers(AppMetrics metrics)
    {
        var cutoff = DateTimeOffset.UtcNow - _activeUserWindow;
        foreach (var (userId, lastSeen) in metrics.ActiveUsers.ToArray())
        {
            if (lastSeen < cutoff)
                metrics.ActiveUsers.TryRemove(userId, out _);
        }
    }

    private static double Percentile(List<double> samples, double percentile)
    {
        if (samples.Count == 0)
            return 0;

        var sorted = samples.OrderBy(x => x).ToList();
        var index = (int)Math.Ceiling(percentile * sorted.Count) - 1;
        index = Math.Clamp(index, 0, sorted.Count - 1);
        return sorted[index];
    }

    private static string EscapeLabel(string value) => value.Replace("\\", "\\\\").Replace("\"", "\\\"");

    private sealed class AppMetrics
    {
        public long RequestsTotal;
        public long RequestsError;
        public long TokensPrompt;
        public long TokensCompletion;
        public long RagHits;
        public List<double> LatencySamples { get; } = [];
        public object LatencyLock { get; } = new();
        public List<int> FeedbackScores { get; } = [];
        public object FeedbackLock { get; } = new();
        public ConcurrentDictionary<string, DateTimeOffset> ActiveUsers { get; } = new(StringComparer.Ordinal);
        public ConcurrentDictionary<string, long> FilteredByReason { get; } = new(StringComparer.Ordinal);
        public long KlEvaluated;
        public long KlApproved;
        public long KlRejected;
        public long KlChunksCreated;
        public long KlChunksMerged;
        public ConcurrentDictionary<string, long> ToolCalls { get; } = new(StringComparer.Ordinal);
        public ConcurrentDictionary<string, long> QuotaExceeded { get; } = new(StringComparer.Ordinal);
    }
}
