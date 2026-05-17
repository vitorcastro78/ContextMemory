using System.Collections.Concurrent;
using System.Text;
using ContextMemory.Core.Contracts;

namespace ContextMemory.Core.Observability;

public sealed class TelemetryCollector : ITelemetryCollector
{
    private readonly ConcurrentDictionary<string, AppMetrics> _apps = new(StringComparer.Ordinal);

    public void RecordRequest(
        string appId,
        int statusCode,
        double latencyMs,
        int promptTokens,
        int completionTokens,
        bool ragHit)
    {
        var metrics = _apps.GetOrAdd(appId, _ => new AppMetrics());
        Interlocked.Increment(ref metrics.RequestsTotal);
        if (statusCode >= 400)
            Interlocked.Increment(ref metrics.RequestsError);

        Interlocked.Add(ref metrics.TokensPrompt, promptTokens);
        Interlocked.Add(ref metrics.TokensCompletion, completionTokens);
        if (ragHit)
            Interlocked.Increment(ref metrics.RagHits);

        metrics.LatencySamples.Add(latencyMs);
        if (metrics.LatencySamples.Count > 1000)
            metrics.LatencySamples.RemoveAt(0);
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

    public AppTelemetrySnapshot GetAppSnapshot(string appId)
    {
        if (!_apps.TryGetValue(appId, out var m))
            return new AppTelemetrySnapshot();

        return new AppTelemetrySnapshot
        {
            RequestsTotal = m.RequestsTotal,
            RequestsError = m.RequestsError,
            TokensPrompt = m.TokensPrompt,
            TokensCompletion = m.TokensCompletion,
            RagHits = m.RagHits,
            AvgLatencyMs = m.LatencySamples.Count > 0 ? m.LatencySamples.Average() : 0,
            FeedbackScoreAvg = m.FeedbackScores.Count > 0 ? m.FeedbackScores.Average() : 0,
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
            var label = EscapeLabel(appId);
            sb.AppendLine($"cm_requests_total{{appId=\"{label}\",status=\"success\"}} {m.RequestsTotal - m.RequestsError}");
            sb.AppendLine($"cm_requests_total{{appId=\"{label}\",status=\"error\"}} {m.RequestsError}");
            sb.AppendLine($"cm_tokens_prompt_total{{appId=\"{label}\"}} {m.TokensPrompt}");
            sb.AppendLine($"cm_tokens_completion_total{{appId=\"{label}\"}} {m.TokensCompletion}");
            sb.AppendLine($"cm_rag_hits_total{{appId=\"{label}\"}} {m.RagHits}");

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
        }

        return sb.ToString();
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
        public List<int> FeedbackScores { get; } = [];
        public object FeedbackLock { get; } = new();
        public ConcurrentDictionary<string, long> FilteredByReason { get; } = new(StringComparer.Ordinal);
    }
}
