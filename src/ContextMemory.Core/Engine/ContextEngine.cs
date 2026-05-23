using System.Diagnostics;
using System.Text.Json;
using ContextMemory.Core.Configuration;
using ContextMemory.Core.Contracts;
using ContextMemory.Core.Models;
using Microsoft.Extensions.Options;

namespace ContextMemory.Core.Engine;

public sealed class ContextEngine : IContextEngine
{
    private readonly IAppRegistry _appRegistry;
    private readonly IAppConfigStore _appConfigStore;
    private readonly IConversationMemory _conversationMemory;
    private readonly IUserProfileStore _userProfileStore;
    private readonly ISemanticMemory _semanticMemory;
    private readonly IWikiIndexService _wikiIndex;
    private readonly ILlmAdapterResolver _adapterResolver;
    private readonly IIntentDetector _intentDetector;
    private readonly IProfileLearner _profileLearner;
    private readonly ISessionSummarizer _sessionSummarizer;
    private readonly PromptComposer _promptComposer;
    private readonly IContentFilter _contentFilter;
    private readonly IContentRulesStore _contentRulesStore;
    private readonly IAuditLog _auditLog;
    private readonly IImplicitFeedbackDetector _implicitFeedbackDetector;
    private readonly IFeedbackStore _feedbackStore;
    private readonly IFeedbackProcessor _feedbackProcessor;
    private readonly IMessageIdTracker _messageIdTracker;
    private readonly ITelemetryCollector _telemetry;
    private readonly bool _contentFilterEnabled;
    private readonly bool _feedbackEnabled;

    public ContextEngine(
        IAppRegistry appRegistry,
        IAppConfigStore appConfigStore,
        IConversationMemory conversationMemory,
        IUserProfileStore userProfileStore,
        ISemanticMemory semanticMemory,
        IWikiIndexService wikiIndex,
        ILlmAdapterResolver adapterResolver,
        IIntentDetector intentDetector,
        IProfileLearner profileLearner,
        ISessionSummarizer sessionSummarizer,
        PromptComposer promptComposer,
        IContentFilter contentFilter,
        IContentRulesStore contentRulesStore,
        IAuditLog auditLog,
        IImplicitFeedbackDetector implicitFeedbackDetector,
        IFeedbackStore feedbackStore,
        IFeedbackProcessor feedbackProcessor,
        IMessageIdTracker messageIdTracker,
        ITelemetryCollector telemetry,
        IOptions<ContextMemoryOptions> options)
    {
        _appRegistry = appRegistry;
        _appConfigStore = appConfigStore;
        _conversationMemory = conversationMemory;
        _userProfileStore = userProfileStore;
        _semanticMemory = semanticMemory;
        _wikiIndex = wikiIndex;
        _adapterResolver = adapterResolver;
        _intentDetector = intentDetector;
        _profileLearner = profileLearner;
        _sessionSummarizer = sessionSummarizer;
        _promptComposer = promptComposer;
        _contentFilter = contentFilter;
        _contentRulesStore = contentRulesStore;
        _auditLog = auditLog;
        _implicitFeedbackDetector = implicitFeedbackDetector;
        _feedbackStore = feedbackStore;
        _feedbackProcessor = feedbackProcessor;
        _messageIdTracker = messageIdTracker;
        _telemetry = telemetry;
        _contentFilterEnabled = options.Value.EnableContentFilter;
        _feedbackEnabled = options.Value.EnableFeedback;
    }

    public async Task<ChatPipelineResult> ProcessChatAsync(
        string appId,
        string userId,
        OllamaRequest request,
        CancellationToken cancellationToken = default)
    {
        var sw = Stopwatch.StartNew();
        if (!_appRegistry.TryGetApp(appId, out var app) || app is null)
            throw new InvalidOperationException($"App '{appId}' not found.");

        var runtimeConfig = _appConfigStore.GetConfig(appId);
        var lastUserMessage = GetLastUserMessage(request);

        var preResult = await RunPrePipelineAsync(app, userId, lastUserMessage, cancellationToken).ConfigureAwait(false);
        if (preResult is not null)
        {
            RecordTelemetry(appId, userId, preResult.StatusCode, sw.ElapsedMilliseconds, 0, 0, false);
            return preResult;
        }

        var (enriched, ragHit) = await BuildEnrichedRequestAsync(app, userId, request, cancellationToken).ConfigureAwait(false);
        var promptTokens = EstimateTokens(enriched.Messages);

        var adapter = _adapterResolver.Resolve(runtimeConfig.LlmBackend);
        var response = await adapter.ChatAsync(enriched, cancellationToken).ConfigureAwait(false);

        var result = await PostProcessResponseAsync(
            app, userId, request, response, runtimeConfig, promptTokens, ragHit, cancellationToken).ConfigureAwait(false);

        RecordTelemetry(appId, userId, result.StatusCode, sw.ElapsedMilliseconds, promptTokens, result.EstimatedCompletionTokens, result.RagHit);
        return result;
    }

    public async IAsyncEnumerable<OllamaResponse> ProcessChatStreamAsync(
        string appId,
        string userId,
        OllamaRequest request,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        if (!_appRegistry.TryGetApp(appId, out var app) || app is null)
            throw new InvalidOperationException($"App '{appId}' not found.");

        var runtimeConfig = _appConfigStore.GetConfig(appId);
        var lastUserMessage = GetLastUserMessage(request);

        var preResult = await RunPrePipelineAsync(app, userId, lastUserMessage, cancellationToken).ConfigureAwait(false);
        if (preResult is not null)
        {
            yield return preResult.Response ?? new OllamaResponse
            {
                Model = request.Model,
                Message = new OllamaMessage { Role = "assistant", Content = preResult.ErrorBody ?? "Blocked." },
                Done = true
            };
            yield break;
        }

        var (enriched, _) = await BuildEnrichedRequestAsync(app, userId, request, cancellationToken).ConfigureAwait(false);
        var adapter = _adapterResolver.Resolve(runtimeConfig.LlmBackend);

        await foreach (var chunk in adapter.ChatStreamAsync(enriched, cancellationToken).ConfigureAwait(false))
            yield return chunk;
    }

    public Task<ChatPipelineResult> FinalizeStreamAsync(
        string appId,
        string userId,
        OllamaRequest request,
        string assistantContent,
        CancellationToken cancellationToken = default)
    {
        if (!_appRegistry.TryGetApp(appId, out var app) || app is null)
            throw new InvalidOperationException($"App '{appId}' not found.");

        var runtimeConfig = _appConfigStore.GetConfig(appId);
        var response = new OllamaResponse
        {
            Model = request.Model,
            Message = new OllamaMessage { Role = "assistant", Content = assistantContent },
            Done = true
        };

        return PostProcessResponseAsync(app, userId, request, response, runtimeConfig, 0, ragHit: false, cancellationToken);
    }

    private async Task<ChatPipelineResult?> RunPrePipelineAsync(
        AppProfile app,
        string userId,
        OllamaMessage? lastUserMessage,
        CancellationToken cancellationToken)
    {
        if (lastUserMessage is null)
            return null;

        if (_feedbackEnabled)
            await ProcessImplicitFeedbackAsync(app.AppId, userId, lastUserMessage.Content, cancellationToken)
                .ConfigureAwait(false);

        if (!_contentFilterEnabled)
            return null;

        var rules = _contentRulesStore.GetRules(app.AppId);
        var pre = _contentFilter.FilterPre(app.AppId, userId, lastUserMessage.Content, rules);

        if (pre.IsBlocked)
        {
            await _auditLog.AppendAsync(new AuditLogEntry
            {
                AppId = app.AppId,
                UserId = userId,
                Phase = "pre",
                Reason = pre.AuditReason,
                Timestamp = DateTimeOffset.UtcNow
            }, cancellationToken).ConfigureAwait(false);

            _telemetry.RecordContentFiltered(app.AppId, pre.AuditReason);

            return new ChatPipelineResult
            {
                IsBlocked = true,
                StatusCode = 400,
                ErrorBody = JsonSerializer.Serialize(new { error = pre.BlockReason })
            };
        }

        return null;
    }

    private async Task<ChatPipelineResult> PostProcessResponseAsync(
        AppProfile app,
        string userId,
        OllamaRequest request,
        OllamaResponse response,
        AppRuntimeConfig runtimeConfig,
        int promptTokens,
        bool ragHit,
        CancellationToken cancellationToken)
    {
        var content = response.Message?.Content ?? response.Response ?? string.Empty;

        if (_contentFilterEnabled && !string.IsNullOrEmpty(content))
        {
            var rules = _contentRulesStore.GetRules(app.AppId);
            var post = _contentFilter.FilterPost(app.AppId, userId, content, rules, runtimeConfig.DefaultLanguage);
            content = post.ModifiedContent ?? content;

            if (!string.IsNullOrEmpty(post.AuditReason))
            {
                await _auditLog.AppendAsync(new AuditLogEntry
                {
                    AppId = app.AppId,
                    UserId = userId,
                    Phase = "post",
                    Reason = post.AuditReason,
                    Timestamp = DateTimeOffset.UtcNow
                }, cancellationToken).ConfigureAwait(false);
            }
        }

        var finalResponse = response with
        {
            Message = new OllamaMessage { Role = "assistant", Content = content }
        };

        var messageId = _messageIdTracker.CreateAndTrack(app.AppId, userId);
        await PersistTurnAsync(app, userId, request, finalResponse, runtimeConfig, cancellationToken)
            .ConfigureAwait(false);

        return new ChatPipelineResult
        {
            Response = finalResponse,
            MessageId = messageId,
            RagHit = ragHit,
            EstimatedPromptTokens = promptTokens,
            EstimatedCompletionTokens = EstimateTokens(content)
        };
    }

    private async Task ProcessImplicitFeedbackAsync(
        string appId,
        string userId,
        string userMessage,
        CancellationToken cancellationToken)
    {
        if (!_implicitFeedbackDetector.TryDetect(userMessage, out var score, out var reason))
            return;

        if (!_messageIdTracker.TryGetLast(appId, userId, out var messageId) || messageId is null)
            return;

        var entry = new FeedbackEntry
        {
            MessageId = messageId,
            AppId = appId,
            UserId = userId,
            Score = score,
            Reason = reason,
            Timestamp = DateTimeOffset.UtcNow,
            IsImplicit = true
        };

        await _feedbackStore.RecordAsync(entry, cancellationToken).ConfigureAwait(false);
        _telemetry.RecordFeedback(appId, score);
        _feedbackProcessor.ProcessFeedbackAsync(appId, userId, messageId, score, reason);
    }

    private async Task<(OllamaRequest Request, bool RagHit)> BuildEnrichedRequestAsync(
        AppProfile app,
        string userId,
        OllamaRequest request,
        CancellationToken cancellationToken)
    {
        var runtimeConfig = _appConfigStore.GetConfig(app.AppId);

        var history = await _conversationMemory
            .GetHistoryAsync(app.AppId, userId, runtimeConfig.MaxHistoryMessages, cancellationToken)
            .ConfigureAwait(false);

        var userProfile = await _userProfileStore
            .GetProfileAsync(app.AppId, userId, cancellationToken)
            .ConfigureAwait(false);

        var lastUserMessage = GetLastUserMessage(request);

        IReadOnlyList<WikiChunk> wikiChunks = [];
        IReadOnlyList<string> semanticFacts = [];

        if (lastUserMessage?.Content is { Length: > 0 } query)
        {
            wikiChunks = await _wikiIndex
                .SearchAsync(app.AppId, query, runtimeConfig.WikiChunksTopK, runtimeConfig.SimilarityThreshold, cancellationToken)
                .ConfigureAwait(false);

            semanticFacts = await _semanticMemory
                .SearchAsync(app.AppId, userId, query, topK: 3, cancellationToken: cancellationToken)
                .ConfigureAwait(false);
        }

        var mergedProfile = MergeSemanticFacts(userProfile, semanticFacts);
        var intent = _intentDetector.Detect(lastUserMessage?.Content);

        var promptContext = new PromptContext
        {
            AppConfig = runtimeConfig,
            UserProfile = mergedProfile,
            WikiChunks = wikiChunks,
            Intent = intent,
            SessionContext = mergedProfile.SessionContext
        };

        var systemPrompt = _promptComposer.Compose(promptContext);
        var messages = new List<OllamaMessage> { new() { Role = "system", Content = systemPrompt } };
        messages.AddRange(history);

        if (lastUserMessage is not null)
        {
            var userContent = lastUserMessage.Content;
            if (_contentFilterEnabled)
            {
                var rules = _contentRulesStore.GetRules(app.AppId);
                var pre = _contentFilter.FilterPre(app.AppId, userId, userContent, rules);
                userContent = pre.ModifiedContent ?? userContent;
            }

            messages.Add(lastUserMessage with { Content = userContent });
        }

        return (request with { Messages = messages }, wikiChunks.Count > 0);
    }

    private async Task PersistTurnAsync(
        AppProfile app,
        string userId,
        OllamaRequest request,
        OllamaResponse response,
        AppRuntimeConfig runtimeConfig,
        CancellationToken cancellationToken)
    {
        var lastUserMessage = GetLastUserMessage(request);
        if (lastUserMessage is null)
            return;

        var toStore = new List<OllamaMessage> { lastUserMessage };

        if (response.Message is { Content: { Length: > 0 } } assistantMessage)
        {
            toStore.Add(assistantMessage with { Role = "assistant" });
            _profileLearner.LearnFromTurn(app.AppId, userId, lastUserMessage.Content, assistantMessage.Content);
        }

        await _conversationMemory
            .AppendAsync(app.AppId, userId, toStore, runtimeConfig.MaxHistoryMessages, cancellationToken)
            .ConfigureAwait(false);

        var model = string.IsNullOrWhiteSpace(request.Model) ? runtimeConfig.LlmModel : request.Model;
        _ = _sessionSummarizer.MaybeSummarizeAsync(app.AppId, userId, model, runtimeConfig.LlmBackend, CancellationToken.None);
    }

    private void RecordTelemetry(
        string appId,
        string userId,
        int statusCode,
        double latencyMs,
        int promptTokens,
        int completionTokens,
        bool ragHit) =>
        _telemetry.RecordRequest(appId, userId, statusCode, latencyMs, promptTokens, completionTokens, ragHit);

    private static OllamaMessage? GetLastUserMessage(OllamaRequest request) =>
        request.Messages.LastOrDefault(m => string.Equals(m.Role, "user", StringComparison.OrdinalIgnoreCase));

    private static UserProfileData MergeSemanticFacts(UserProfileData profile, IReadOnlyList<string> semanticFacts)
    {
        if (semanticFacts.Count == 0)
            return profile;

        var existing = new HashSet<string>(profile.Facts.Select(f => f.Text), StringComparer.OrdinalIgnoreCase);
        var mergedFacts = profile.Facts.ToList();
        var now = DateTimeOffset.UtcNow;

        foreach (var text in semanticFacts)
        {
            if (existing.Add(text))
            {
                mergedFacts.Add(new UserFact
                {
                    Text = text,
                    LearnedAt = now,
                    LastConfirmedAt = now,
                    Confidence = 0.75f
                });
            }
        }

        return profile with { Facts = mergedFacts };
    }

    private static int EstimateTokens(IEnumerable<OllamaMessage> messages) =>
        messages.Sum(m => EstimateTokens(m.Content));

    private static int EstimateTokens(string text) =>
        string.IsNullOrEmpty(text) ? 0 : Math.Max(1, text.Length / 4);
}
