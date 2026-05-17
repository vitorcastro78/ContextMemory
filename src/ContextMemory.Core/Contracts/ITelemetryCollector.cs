namespace ContextMemory.Core.Contracts;

public interface ITelemetryCollector
{
    void RecordRequest(
        string appId,
        int statusCode,
        double latencyMs,
        int promptTokens,
        int completionTokens,
        bool ragHit);

    void RecordFeedback(string appId, int score);
    void RecordContentFiltered(string appId, string reason);
    string ExportPrometheus();
    AppTelemetrySnapshot GetAppSnapshot(string appId);
    IReadOnlyDictionary<string, AppTelemetrySnapshot> GetAllSnapshots();
}

public sealed class AppTelemetrySnapshot
{
    public long RequestsTotal { get; init; }
    public long RequestsError { get; init; }
    public long TokensPrompt { get; init; }
    public long TokensCompletion { get; init; }
    public long RagHits { get; init; }
    public double AvgLatencyMs { get; init; }
    public double FeedbackScoreAvg { get; init; }
    public Dictionary<string, long> FilteredByReason { get; init; } = new();
}
