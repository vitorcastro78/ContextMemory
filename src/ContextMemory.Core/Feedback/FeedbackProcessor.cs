using ContextMemory.Core.Contracts;
using Microsoft.Extensions.Logging;

namespace ContextMemory.Core.Feedback;

public sealed class FeedbackProcessor : IFeedbackProcessor
{
    private readonly IProfileLearner _profileLearner;
    private readonly ILogger<FeedbackProcessor> _logger;

    public FeedbackProcessor(IProfileLearner profileLearner, ILogger<FeedbackProcessor> logger)
    {
        _profileLearner = profileLearner;
        _logger = logger;
    }

    public void ProcessFeedbackAsync(string appId, string userId, string messageId, int score, string? reason)
    {
        _profileLearner.LearnFromFeedback(appId, userId, score, reason);
        _logger.LogInformation(
            "Feedback delegated to ProfileLearner for {AppId}/{UserId} message {MessageId} score={Score}",
            appId,
            userId,
            messageId,
            score);
    }
}
