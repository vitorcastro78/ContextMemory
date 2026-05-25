using System.Text;
using System.Text.Json;
using ContextMemory.Core.Models;

namespace ContextMemory.Core.CompanyBrain;

public sealed record McpServerContext(
    string CompanyId,
    CompanySkillsFile Skills,
    Func<string, int, IReadOnlyList<CompanyProcess>> SearchProcesses,
    Action<string, string?>? OnToolExecuted = null);

public static class McpJsonRpcServer
{
    public const string SearchToolName = "company_search_processes";

    public static JsonRpcResponse Handle(McpServerContext ctx, JsonRpcRequest request)
    {
        return request.Method switch
        {
            "initialize" => Success(request, new
            {
                protocolVersion = "2024-11-05",
                serverInfo = new { name = "ContextMemory Company Brain", version = ctx.Skills.Version },
                capabilities = new
                {
                    tools = new { },
                    resources = new { subscribe = false, listChanged = false },
                    prompts = new { listChanged = false }
                }
            }),
            "tools/list" => Success(request, new { tools = BuildTools(ctx.Skills) }),
            "tools/call" => HandleToolCall(ctx, request),
            "resources/list" => Success(request, new { resources = ListResources(ctx.Skills) }),
            "resources/read" => HandleResourceRead(ctx, request),
            "prompts/list" => Success(request, new { prompts = ListPrompts(ctx.Skills) }),
            "prompts/get" => HandlePromptGet(ctx, request),
            "ping" => Success(request, new { }),
            _ => Error(request, -32601, $"Method not found: {request.Method}")
        };
    }

    private static IReadOnlyList<object> BuildTools(CompanySkillsFile skills)
    {
        var tools = new List<object>
        {
            new
            {
                name = SearchToolName,
                description = "Pesquisa semântica de processos executáveis da empresa por query natural.",
                inputSchema = new
                {
                    type = "object",
                    properties = new
                    {
                        query = new
                        {
                            type = "string",
                            description = "Pergunta, ticket ou tema a pesquisar nos processos."
                        },
                        topK = new
                        {
                            type = "integer",
                            description = "Número máximo de processos a devolver.",
                            minimum = 1,
                            maximum = 20
                        }
                    },
                    required = new[] { "query" }
                }
            }
        };

        tools.AddRange(SkillsExporter.ToMcp(skills).Tools.Select(t => new
        {
            t.Name,
            t.Description,
            t.InputSchema
        }));

        return tools;
    }

    private static JsonRpcResponse HandleToolCall(McpServerContext ctx, JsonRpcRequest request)
    {
        if (request.Params is null || !request.Params.Value.TryGetProperty("name", out var nameEl))
            return Error(request, -32602, "Missing tool name.");

        var toolName = nameEl.GetString();
        if (string.IsNullOrWhiteSpace(toolName))
            return Error(request, -32602, "Invalid tool name.");

        if (string.Equals(toolName, SearchToolName, StringComparison.OrdinalIgnoreCase))
            return HandleSearchTool(ctx, request);

        var process = ctx.Skills.Processes.FirstOrDefault(p =>
            string.Equals(SkillsExporter.ToMcpToolName(p.ProcessId), toolName, StringComparison.OrdinalIgnoreCase));

        if (process is null)
            return Error(request, -32602, $"Unknown tool: {toolName}");

        var context = ReadContextArgument(request.Params.Value);
        ctx.OnToolExecuted?.Invoke(toolName, context);

        var text = SkillsCompiler.FormatProcessBlock(process);
        if (!string.IsNullOrWhiteSpace(context))
            text = $"Contexto: {context.Trim()}\n\n{text}";

        return Success(request, new
        {
            content = new[] { new { type = "text", text } },
            isError = false
        });
    }

    private static JsonRpcResponse HandleSearchTool(McpServerContext ctx, JsonRpcRequest request)
    {
        if (request.Params is null)
            return Error(request, -32602, "Missing search parameters.");

        var query = string.Empty;
        if (request.Params.Value.TryGetProperty("arguments", out var args)
            && args.TryGetProperty("query", out var queryEl))
            query = queryEl.GetString() ?? string.Empty;

        if (string.IsNullOrWhiteSpace(query))
            return Error(request, -32602, "Missing query argument.");

        var topK = 5;
        if (request.Params.Value.TryGetProperty("arguments", out var argsTop)
            && argsTop.TryGetProperty("topK", out var topEl)
            && topEl.TryGetInt32(out var parsedTop))
            topK = Math.Clamp(parsedTop, 1, 20);

        var matches = ctx.SearchProcesses(query, topK);
        var sb = new StringBuilder();
        sb.AppendLine($"Processos encontrados ({matches.Count}):");
        foreach (var process in matches)
        {
            sb.AppendLine();
            sb.AppendLine(SkillsCompiler.FormatProcessBlock(process));
        }

        ctx.OnToolExecuted?.Invoke(SearchToolName, query);

        return Success(request, new
        {
            content = new[] { new { type = "text", text = sb.ToString().Trim() } },
            isError = false
        });
    }

    private static IReadOnlyList<object> ListResources(CompanySkillsFile skills)
    {
        var resources = new List<object>
        {
            new
            {
                uri = $"company://{skills.CompanyId}/skills.yaml",
                name = "Skills YAML",
                description = "Export YAML completo de skills e processos.",
                mimeType = "text/yaml"
            }
        };

        foreach (var process in skills.Processes)
        {
            resources.Add(new
            {
                uri = $"company://{skills.CompanyId}/process/{process.ProcessId}",
                name = process.Title,
                description = process.Summary,
                mimeType = "text/markdown"
            });
        }

        return resources;
    }

    private static JsonRpcResponse HandleResourceRead(McpServerContext ctx, JsonRpcRequest request)
    {
        if (request.Params is null || !request.Params.Value.TryGetProperty("uri", out var uriEl))
            return Error(request, -32602, "Missing resource uri.");

        var uri = uriEl.GetString();
        if (string.IsNullOrWhiteSpace(uri))
            return Error(request, -32602, "Invalid resource uri.");

        if (uri.EndsWith("/skills.yaml", StringComparison.OrdinalIgnoreCase))
        {
            var yaml = SkillsExporter.ToYaml(ctx.Skills);
            return Success(request, new
            {
                contents = new[]
                {
                    new { uri, mimeType = "text/yaml", text = yaml }
                }
            });
        }

        const string processMarker = "/process/";
        var processIndex = uri.IndexOf(processMarker, StringComparison.OrdinalIgnoreCase);
        if (processIndex >= 0)
        {
            var processId = uri[(processIndex + processMarker.Length)..];
            var process = ctx.Skills.Processes.FirstOrDefault(p =>
                string.Equals(p.ProcessId, processId, StringComparison.OrdinalIgnoreCase));

            if (process is null)
                return Error(request, -32602, $"Unknown process resource: {processId}");

            return Success(request, new
            {
                contents = new[]
                {
                    new
                    {
                        uri,
                        mimeType = "text/markdown",
                        text = SkillsCompiler.FormatProcessBlock(process)
                    }
                }
            });
        }

        return Error(request, -32602, $"Unknown resource: {uri}");
    }

    private static IReadOnlyList<object> ListPrompts(CompanySkillsFile skills) =>
        skills.Skills.Select(skill => new
        {
            name = $"skill_{skill.SkillId}",
            description = skill.Description,
            arguments = Array.Empty<object>()
        }).ToList();

    private static JsonRpcResponse HandlePromptGet(McpServerContext ctx, JsonRpcRequest request)
    {
        if (request.Params is null || !request.Params.Value.TryGetProperty("name", out var nameEl))
            return Error(request, -32602, "Missing prompt name.");

        var name = nameEl.GetString();
        if (string.IsNullOrWhiteSpace(name))
            return Error(request, -32602, "Invalid prompt name.");

        var skillId = name.StartsWith("skill_", StringComparison.OrdinalIgnoreCase)
            ? name["skill_".Length..]
            : name;

        var skill = ctx.Skills.Skills.FirstOrDefault(s =>
            string.Equals(s.SkillId, skillId, StringComparison.OrdinalIgnoreCase));

        if (skill is null)
            return Error(request, -32602, $"Unknown prompt: {name}");

        return Success(request, new
        {
            description = skill.Description,
            messages = new[]
            {
                new
                {
                    role = "user",
                    content = new
                    {
                        type = "text",
                        text = skill.Instructions
                    }
                }
            }
        });
    }

    private static string ReadContextArgument(JsonElement paramsElement)
    {
        if (!paramsElement.TryGetProperty("arguments", out var args)
            || !args.TryGetProperty("context", out var ctx))
            return string.Empty;

        return ctx.GetString() ?? string.Empty;
    }

    private static JsonRpcResponse Success(JsonRpcRequest request, object result) =>
        new() { Id = request.Id, Result = result };

    private static JsonRpcResponse Error(JsonRpcRequest request, int code, string message) =>
        new() { Id = request.Id, Error = new JsonRpcError { Code = code, Message = message } };
}
