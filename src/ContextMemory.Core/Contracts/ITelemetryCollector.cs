namespace ContextMemory.Core.Contracts;

public interface ITelemetryCollector
{
    void RecordRequest(
        string appId,
        string userId,
        int statusCode,
        double latencyMs,
        int promptTokens,
        int completionTokens,
        bool ragHit);

    void RecordUserActivity(string appId, string userId);

    void RecordFeedback(string appId, int score);
    void RecordContentFiltered(string appId, string reason);
    void RecordKnowledgeLoopEvaluated(string appId);
    void RecordKnowledgeLoopApproved(string appId);
    void RecordKnowledgeLoopRejected(string appId);
    void RecordKnowledgeLoopChunkCreated(string appId);
    void RecordKnowledgeLoopChunkMerged(string appId);
    void RecordToolCall(string appId, string toolName, bool success, double durationMs);
    void RecordQuotaExceeded(string appId, string reason);
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
    public int ActiveUsers { get; init; }
    public Dictionary<string, long> FilteredByReason { get; init; } = new();
}
