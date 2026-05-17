namespace ContextMemory.Core.Models;

public record AuditLogEntry
{
    public required string AppId { get; init; }
    public required string UserId { get; init; }
    public required string Phase { get; init; }
    public required string Reason { get; init; }
    public DateTimeOffset Timestamp { get; init; }
}
