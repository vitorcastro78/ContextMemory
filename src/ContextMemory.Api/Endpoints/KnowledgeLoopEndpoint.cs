using ContextMemory.Core.Contracts;
using ContextMemory.Core.KnowledgeLoop;
using ContextMemory.Core.Models;

namespace ContextMemory.Api.Endpoints;

public static class KnowledgeLoopEndpoint
{
    public static void MapKnowledgeLoopEndpoints(this WebApplication app)
    {
        app.MapPost("/apps/{appId}/knowledge-loop/run", RunAsync).DisableAntiforgery();
        app.MapGet("/apps/{appId}/knowledge-loop/stats", GetStatsAsync);
        app.MapGet("/admin/apps/{appId}/knowledge-loop", ListEntriesAsync);
        app.MapGet("/admin/apps/{appId}/knowledge-loop/stats", GetStatsAdminAsync);
        app.MapGet("/admin/apps/{appId}/knowledge-loop/pending-review", ListPendingReviewAsync);
        app.MapPost("/admin/apps/{appId}/knowledge-loop/approve/{sessionId}", ApproveAsync).DisableAntiforgery();
        app.MapDelete("/admin/apps/{appId}/knowledge-loop/reject/{sessionId}", RejectAsync);
    }

    private static async Task<IResult> RunAsync(
        string appId,
        HttpContext http,
        IKnowledgeLoop loop,
        CancellationToken cancellationToken)
    {
        if (!IsAppAuthorized(http, appId))
            return Results.Unauthorized();

        await loop.ProcessPendingAsync(appId, cancellationToken).ConfigureAwait(false);
        return Results.Ok(new { status = "processed", appId });
    }

    private static async Task<IResult> GetStatsAsync(
        string appId,
        HttpContext http,
        IKnowledgeLoop loop,
        CancellationToken cancellationToken)
    {
        if (!IsAppAuthorized(http, appId))
            return Results.Unauthorized();

        var stats = await loop.GetStatsAsync(appId, cancellationToken).ConfigureAwait(false);
        return Results.Json(stats);
    }

    private static async Task<IResult> GetStatsAdminAsync(
        string appId,
        IKnowledgeLoop loop,
        CancellationToken cancellationToken)
    {
        var stats = await loop.GetStatsAsync(appId, cancellationToken).ConfigureAwait(false);
        return Results.Json(stats);
    }

    private static async Task<IResult> ListEntriesAsync(
        string appId,
        IKnowledgeLoop loop,
        CancellationToken cancellationToken)
    {
        var entries = await loop.GetEntriesAsync(appId, null, cancellationToken).ConfigureAwait(false);
        return Results.Json(entries.Select(ToDto));
    }

    private static async Task<IResult> ListPendingReviewAsync(
        string appId,
        IKnowledgeLoop loop,
        CancellationToken cancellationToken)
    {
        var entries = await loop
            .GetEntriesAsync(appId, KnowledgeLoopStatus.PendingReview, cancellationToken)
            .ConfigureAwait(false);
        return Results.Json(entries.Select(ToDto));
    }

    private static async Task<IResult> ApproveAsync(
        string appId,
        string sessionId,
        IKnowledgeLoop loop,
        CancellationToken cancellationToken)
    {
        var ok = await loop.ApproveAsync(sessionId, cancellationToken).ConfigureAwait(false);
        return ok ? Results.Ok(new { status = "approved", sessionId }) : Results.NotFound();
    }

    private static async Task<IResult> RejectAsync(
        string appId,
        string sessionId,
        IKnowledgeLoop loop,
        CancellationToken cancellationToken)
    {
        var ok = await loop.RejectAsync(sessionId, cancellationToken).ConfigureAwait(false);
        return ok ? Results.Ok(new { status = "rejected", sessionId }) : Results.NotFound();
    }

    private static bool IsAppAuthorized(HttpContext http, string appId)
    {
        if (!http.Items.TryGetValue("AppId", out var value))
            return false;
        return string.Equals(value as string, appId, StringComparison.Ordinal);
    }

    private static object ToDto(KnowledgeLoopEntry e) => new
    {
        e.SessionId,
        e.AppId,
        e.UserId,
        e.Status,
        e.Evaluation,
        e.IngestedPath,
        e.FailureReason,
        e.CreatedAt,
        e.ProcessedAt,
        messageCount = e.Messages.Count
    };
}
