using ContextMemory.Api.Middleware;
using ContextMemory.Core.Contracts;
using ContextMemory.Core.Models;

namespace ContextMemory.Api.Endpoints;

public static class FeedbackEndpoint
{
    public static void MapFeedbackEndpoint(this WebApplication app)
    {
        app.MapPost("/api/chat/feedback", HandleFeedbackAsync).DisableAntiforgery();
    }

    private static async Task<IResult> HandleFeedbackAsync(
        HttpContext httpContext,
        FeedbackRequest request,
        IFeedbackStore feedbackStore,
        IFeedbackProcessor feedbackProcessor,
        ITelemetryCollector telemetry,
        CancellationToken cancellationToken)
    {
        var appId = (string)httpContext.Items[AuthMiddleware.AppIdItemKey]!;
        var userId = (string)httpContext.Items[AuthMiddleware.UserIdItemKey]!;

        if (request.Score is < -1 or > 1)
            return Results.BadRequest(new { error = "Score must be -1, 0, or 1." });

        var entry = new FeedbackEntry
        {
            MessageId = request.MessageId,
            AppId = appId,
            UserId = userId,
            Score = request.Score,
            Reason = request.Reason,
            Timestamp = DateTimeOffset.UtcNow,
            IsImplicit = false
        };

        await feedbackStore.RecordAsync(entry, cancellationToken).ConfigureAwait(false);
        telemetry.RecordFeedback(appId, request.Score);
        feedbackProcessor.ProcessFeedbackAsync(appId, userId, request.MessageId, request.Score, request.Reason);

        return Results.Ok(new { status = "recorded" });
    }
}
