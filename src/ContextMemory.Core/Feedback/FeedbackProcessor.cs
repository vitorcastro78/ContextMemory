using ContextMemory.Core.Contracts;
using ContextMemory.Core.Models;
using Microsoft.Extensions.Logging;

namespace ContextMemory.Core.Feedback;

public sealed class FeedbackProcessor : IFeedbackProcessor
{
    private readonly IFeedbackStore _feedbackStore;
    private readonly IUserProfileStore _userProfileStore;
    private readonly IAppConfigStore _appConfigStore;
    private readonly ILogger<FeedbackProcessor> _logger;

    public FeedbackProcessor(
        IFeedbackStore feedbackStore,
        IUserProfileStore userProfileStore,
        IAppConfigStore appConfigStore,
        ILogger<FeedbackProcessor> logger)
    {
        _feedbackStore = feedbackStore;
        _userProfileStore = userProfileStore;
        _appConfigStore = appConfigStore;
        _logger = logger;
    }

    public void ProcessFeedbackAsync(string appId, string userId, string messageId, int score, string? reason)
    {
        _ = ProcessInternalAsync(appId, userId, messageId, score, reason);
    }

    private async Task ProcessInternalAsync(
        string appId,
        string userId,
        string messageId,
        int score,
        string? reason)
    {
        try
        {
            if (score < 0)
            {
                if (reason is not null && (
                    reason.Contains("curto", StringComparison.OrdinalIgnoreCase)
                    || reason.Contains("long", StringComparison.OrdinalIgnoreCase)))
                {
                    var count = await _feedbackStore
                        .CountNegativeByReasonAsync(appId, "long", CancellationToken.None)
                        .ConfigureAwait(false);

                    if (count >= 3)
                    {
                        await _userProfileStore
                            .AddOrConfirmFactsAsync(appId, userId, ["Prefere respostas curtas e concisas"], 0.85f, CancellationToken.None)
                            .ConfigureAwait(false);
                    }
                }

                if (reason is not null && reason.Contains("format", StringComparison.OrdinalIgnoreCase))
                {
                    var count = await _feedbackStore
                        .CountNegativeByReasonAsync(appId, "format", CancellationToken.None)
                        .ConfigureAwait(false);

                    if (count >= 3)
                    {
                        var config = _appConfigStore.GetConfig(appId);
                        await _appConfigStore.UpdateAsync(appId, new AppConfigPatchRequest
                        {
                            FormatRules = config.FormatRules + "\n- Prioriza respostas em formato de lista numerada."
                        }, CancellationToken.None).ConfigureAwait(false);
                    }
                }
            }

            _logger.LogInformation(
                "Feedback processed for {AppId}/{UserId} message {MessageId} score={Score}",
                appId,
                userId,
                messageId,
                score);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Feedback processing failed for {AppId}/{UserId}", appId, userId);
        }
    }
}
