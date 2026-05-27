namespace ContextMemory.Core.Contracts;

public record ToolDefinition(
    string Name,
    string Description,
    Func<Dictionary<string, string>, CancellationToken, Task<string>> Handler);

public interface IToolRegistry
{
    void Register(string appId, ToolDefinition tool);
    IReadOnlyList<ToolDefinition> GetTools(string appId);
    Task<string> ExecuteAsync(
        string appId,
        string toolName,
        Dictionary<string, string> args,
        CancellationToken cancellationToken = default);
}
