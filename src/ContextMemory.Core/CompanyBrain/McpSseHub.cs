using System.Collections.Concurrent;
using System.Text.Json;
using System.Threading.Channels;
using ContextMemory.Core.Models;

namespace ContextMemory.Core.CompanyBrain;

public sealed class McpSseHub
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    private readonly ConcurrentDictionary<string, McpSseSession> _sessions = new();

    public string CreateSession(string companyId)
    {
        var sessionId = Guid.NewGuid().ToString("N");
        _sessions[sessionId] = new McpSseSession(companyId);
        return sessionId;
    }

    public bool TryGetSession(string sessionId, out McpSseSession? session) =>
        _sessions.TryGetValue(sessionId, out session);

    public bool TryPushResponse(string sessionId, JsonRpcResponse response)
    {
        if (!_sessions.TryGetValue(sessionId, out var session))
            return false;

        return session.Channel.Writer.TryWrite(response);
    }

    public void RemoveSession(string sessionId) =>
        _sessions.TryRemove(sessionId, out _);

    public static string SerializeSseEvent(string eventName, string data) =>
        $"event: {eventName}\ndata: {data}\n\n";

    public static string SerializeMessage(JsonRpcResponse response) =>
        JsonSerializer.Serialize(response, JsonOptions);

    public sealed class McpSseSession
    {
        public McpSseSession(string companyId)
        {
            CompanyId = companyId;
            Channel = System.Threading.Channels.Channel.CreateUnbounded<JsonRpcResponse>();
        }

        public string CompanyId { get; }
        public Channel<JsonRpcResponse> Channel { get; }
    }
}
