using System.Text.Json;
using ContextMemory.Core.Configuration;
using ContextMemory.Core.Models;
using Microsoft.Extensions.Options;

namespace ContextMemory.Core.CompanyBrain;

public sealed class ProcessExecutionLogger
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    private readonly string _companiesRoot;

    public ProcessExecutionLogger(IOptions<ContextMemoryOptions> options)
    {
        var config = options.Value;
        _companiesRoot = Path.Combine(
            Path.GetFullPath(config.DataPath, config.ContentRootPath),
            "companies");
    }

    public void Log(string companyId, string toolName, string? context)
    {
        if (string.IsNullOrWhiteSpace(companyId) || string.IsNullOrWhiteSpace(toolName))
            return;

        var companyDir = Path.Combine(_companiesRoot, companyId);
        Directory.CreateDirectory(companyDir);

        var entry = new ProcessExecutionEntry
        {
            CompanyId = companyId,
            ToolName = toolName,
            Context = context?.Trim(),
            ExecutedAt = DateTimeOffset.UtcNow
        };

        var line = JsonSerializer.Serialize(entry, JsonOptions);
        File.AppendAllText(Path.Combine(companyDir, "mcp-executions.jsonl"), line + Environment.NewLine);
    }
}
