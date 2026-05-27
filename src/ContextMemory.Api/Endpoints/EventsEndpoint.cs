using ContextMemory.Core.Contracts;
using ContextMemory.Core.Models;

namespace ContextMemory.Api.Endpoints;

public static class EventsEndpoint
{
    public static void MapEventsEndpoint(this WebApplication app) =>
        app.MapPost("/apps/{appId}/events", HandleExternalEventAsync).DisableAntiforgery();

    private static async Task<IResult> HandleExternalEventAsync(
        string appId,
        ExternalEventRequest body,
        HttpContext http,
        IConversationMemory conversationMemory,
        IKnowledgeLoop knowledgeLoop,
        IAppRegistry appRegistry,
        CancellationToken cancellationToken)
    {
        if (!http.Items.TryGetValue("AppId", out var ctxAppId)
            || !string.Equals(ctxAppId as string, appId, StringComparison.Ordinal))
            return Results.Unauthorized();

        if (!appRegistry.TryGetApp(appId, out _))
            return Results.NotFound(new { error = "app_not_found" });

        if (!string.IsNullOrWhiteSpace(body.UserId))
        {
            var eventMessage = new OllamaMessage
            {
                Role = "system",
                Content = $"[EVENTO EXTERNO - {body.EventType}] {body.Payload}"
            };
            await conversationMemory
                .AppendAsync(appId, body.UserId, [eventMessage], 100, cancellationToken)
                .ConfigureAwait(false);
        }

        if (body.IngestAsKnowledge)
        {
            var syntheticMessage = new OllamaMessage { Role = "user", Content = body.Payload };
            await knowledgeLoop
                .EvaluateSessionAsync(appId, "system-event", [syntheticMessage], cancellationToken)
                .ConfigureAwait(false);
        }

        return Results.Ok(new { status = "processed", eventId = Guid.NewGuid().ToString("N") });
    }
}

public record ExternalEventRequest
{
    public string? UserId { get; init; }
    public required string EventType { get; init; }
    public required string Payload { get; init; }
    public bool IngestAsKnowledge { get; init; }
}
