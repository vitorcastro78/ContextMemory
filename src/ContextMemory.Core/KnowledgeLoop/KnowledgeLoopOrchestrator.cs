using ContextMemory.Core.Configuration;
using ContextMemory.Core.Contracts;
using ContextMemory.Core.Models;
using ContextMemory.Core.Observability;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace ContextMemory.Core.KnowledgeLoop;

public sealed class KnowledgeLoopOrchestrator : IKnowledgeLoop
{
    private readonly ConversationEvaluator _evaluator;
    private readonly KnowledgeExtractor _extractor;
    private readonly KnowledgeMerger _merger;
    private readonly WikiIngestionService _ingestion;
    private readonly IKnowledgeLoopStore _store;
    private readonly IAppConfigStore _appConfigStore;
    private readonly IPlanStore? _planStore;
    private readonly ITelemetryCollector? _telemetry;
    private readonly ContextMemoryOptions _options;
    private readonly ILogger<KnowledgeLoopOrchestrator> _logger;

    public KnowledgeLoopOrchestrator(
        ConversationEvaluator evaluator,
        KnowledgeExtractor extractor,
        KnowledgeMerger merger,
        WikiIngestionService ingestion,
        IKnowledgeLoopStore store,
        IAppConfigStore appConfigStore,
        IOptions<ContextMemoryOptions> options,
        ILogger<KnowledgeLoopOrchestrator> logger,
        IPlanStore? planStore = null,
        ITelemetryCollector? telemetry = null)
    {
        _evaluator = evaluator;
        _extractor = extractor;
        _merger = merger;
        _ingestion = ingestion;
        _store = store;
        _appConfigStore = appConfigStore;
        _planStore = planStore;
        _telemetry = telemetry;
        _options = options.Value;
        _logger = logger;
    }

    public async Task EvaluateSessionAsync(
        string appId,
        string userId,
        IReadOnlyList<OllamaMessage> sessionMessages,
        CancellationToken cancellationToken = default)
    {
        var config = _appConfigStore.GetConfig(appId);
        if (!IsKnowledgeLoopEnabled(config))
            return;

        if (sessionMessages.Count < config.KnowledgeLoopMinMessages)
            return;

        if (_planStore is not null)
        {
            var plan = await _planStore.GetPlanAsync(appId, cancellationToken).ConfigureAwait(false);
            if (!plan.KnowledgeLoopEnabled)
                return;
        }

        try
        {
            var evaluation = await _evaluator.EvaluateAsync(appId, sessionMessages, cancellationToken)
                .ConfigureAwait(false);

            _telemetry?.RecordKnowledgeLoopEvaluated(appId);

            var autoThreshold = config.KnowledgeLoopAutoApproveThreshold > 0
                ? config.KnowledgeLoopAutoApproveThreshold
                : _options.KnowledgeLoopAutoApproveThreshold;
            var reviewThreshold = config.KnowledgeLoopManualReviewThreshold > 0
                ? config.KnowledgeLoopManualReviewThreshold
                : _options.KnowledgeLoopManualReviewThreshold;

            var status = evaluation.Score >= autoThreshold
                ? KnowledgeLoopStatus.PendingExtraction
                : evaluation.Score >= reviewThreshold
                    ? KnowledgeLoopStatus.PendingReview
                    : KnowledgeLoopStatus.Rejected;

            if (status == KnowledgeLoopStatus.PendingExtraction)
                _telemetry?.RecordKnowledgeLoopApproved(appId);
            else if (status == KnowledgeLoopStatus.Rejected)
                _telemetry?.RecordKnowledgeLoopRejected(appId);

            var entry = new KnowledgeLoopEntry
            {
                SessionId = Guid.NewGuid().ToString("N"),
                AppId = appId,
                UserId = userId,
                Messages = sessionMessages.ToList(),
                Evaluation = evaluation,
                Status = status,
                CreatedAt = DateTimeOffset.UtcNow
            };

            await _store.SaveEntryAsync(entry, cancellationToken).ConfigureAwait(false);

            _logger.LogInformation(
                "KnowledgeLoop eval {AppId}/{SessionId}: score={Score:F2} status={Status}",
                appId,
                entry.SessionId,
                evaluation.Score,
                status);

            if (status == KnowledgeLoopStatus.PendingExtraction)
                _ = ProcessEntryAsync(entry, CancellationToken.None);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "KnowledgeLoop evaluation failed for {AppId}", appId);
        }
    }

    public async Task ProcessPendingAsync(string appId, CancellationToken cancellationToken = default)
    {
        var pending = await _store
            .GetPendingAsync(appId, KnowledgeLoopStatus.PendingExtraction, cancellationToken)
            .ConfigureAwait(false);

        foreach (var entry in pending)
        {
            if (cancellationToken.IsCancellationRequested)
                break;

            await ProcessEntryAsync(entry, cancellationToken).ConfigureAwait(false);
        }
    }

    public Task<KnowledgeLoopStats> GetStatsAsync(string appId, CancellationToken cancellationToken = default) =>
        _store.GetStatsAsync(appId, cancellationToken);

    public Task<IReadOnlyList<KnowledgeLoopEntry>> GetEntriesAsync(
        string appId,
        KnowledgeLoopStatus? status = null,
        CancellationToken cancellationToken = default) =>
        _store.GetByAppAsync(appId, status, cancellationToken);

    public async Task<bool> ApproveAsync(string sessionId, CancellationToken cancellationToken = default)
    {
        var entry = await _store.GetBySessionIdAsync(sessionId, cancellationToken).ConfigureAwait(false);
        if (entry is null || entry.Status != KnowledgeLoopStatus.PendingReview)
            return false;

        await _store.UpdateStatusAsync(sessionId, KnowledgeLoopStatus.PendingExtraction, cancellationToken: cancellationToken)
            .ConfigureAwait(false);

        entry.Status = KnowledgeLoopStatus.PendingExtraction;
        await ProcessEntryAsync(entry, cancellationToken).ConfigureAwait(false);
        return true;
    }

    public async Task<bool> RejectAsync(string sessionId, CancellationToken cancellationToken = default)
    {
        var entry = await _store.GetBySessionIdAsync(sessionId, cancellationToken).ConfigureAwait(false);
        if (entry is null)
            return false;

        await _store
            .UpdateStatusAsync(sessionId, KnowledgeLoopStatus.Rejected, cancellationToken: cancellationToken)
            .ConfigureAwait(false);
        return true;
    }

    private async Task ProcessEntryAsync(KnowledgeLoopEntry entry, CancellationToken cancellationToken)
    {
        var config = _appConfigStore.GetConfig(entry.AppId);
        var maxPerDay = config.KnowledgeLoopMaxChunksPerDay > 0
            ? config.KnowledgeLoopMaxChunksPerDay
            : _options.KnowledgeLoopMaxChunksPerDay;

        var ingestedToday = await _store.CountIngestedTodayAsync(entry.AppId, cancellationToken).ConfigureAwait(false);
        if (ingestedToday >= maxPerDay)
        {
            _logger.LogWarning("KnowledgeLoop daily limit reached for {AppId}", entry.AppId);
            return;
        }

        try
        {
            var knowledge = await _extractor
                .ExtractAsync(entry.AppId, entry.UserId, entry.SessionId, entry.Messages, cancellationToken)
                .ConfigureAwait(false);

            if (knowledge is null)
            {
                await _store
                    .UpdateStatusAsync(entry.SessionId, KnowledgeLoopStatus.Rejected, failureReason: "extraction_failed", cancellationToken: cancellationToken)
                    .ConfigureAwait(false);
                return;
            }

            var mergeResult = await _merger
                .MergeOrCreateAsync(entry.AppId, knowledge, cancellationToken)
                .ConfigureAwait(false);

            await _ingestion.IngestAsync(entry.AppId, mergeResult, cancellationToken).ConfigureAwait(false);

            await _store.UpdateStatusAsync(
                entry.SessionId,
                KnowledgeLoopStatus.Ingested,
                ingestedPath: mergeResult.TargetPath,
                cancellationToken: cancellationToken).ConfigureAwait(false);

            if (mergeResult.Action == MergeAction.Merged)
                _telemetry?.RecordKnowledgeLoopChunkMerged(entry.AppId);
            else
                _telemetry?.RecordKnowledgeLoopChunkCreated(entry.AppId);

            _logger.LogInformation(
                "KnowledgeLoop ingested {Action} chunk '{Path}' for {AppId}",
                mergeResult.Action,
                mergeResult.TargetPath,
                entry.AppId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "KnowledgeLoop processing failed for session {SessionId}", entry.SessionId);
            await _store
                .UpdateStatusAsync(entry.SessionId, KnowledgeLoopStatus.Failed, failureReason: ex.Message, cancellationToken: cancellationToken)
                .ConfigureAwait(false);
        }
    }

    private static bool IsKnowledgeLoopEnabled(AppRuntimeConfig config) =>
        config.KnowledgeLoopEnabled;
}
