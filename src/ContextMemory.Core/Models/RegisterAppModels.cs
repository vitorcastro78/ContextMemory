using System.Text.Json.Serialization;

namespace ContextMemory.Core.Models;

public record RegisterAppRequest
{
    [JsonPropertyName("appName")]
    public string AppName { get; init; } = string.Empty;

    [JsonPropertyName("domain")]
    public string Domain { get; init; } = string.Empty;

    [JsonPropertyName("defaultLanguage")]
    public string DefaultLanguage { get; init; } = "pt-PT";

    [JsonPropertyName("wikiPath")]
    public string? WikiPath { get; init; }

    [JsonPropertyName("llmBackend")]
    public string LlmBackend { get; init; } = "ollama";

    [JsonPropertyName("llmModel")]
    public string LlmModel { get; init; } = "llama3.2";

    [JsonPropertyName("promptPersona")]
    public string PromptPersona { get; init; } = string.Empty;
}

public record RegisterAppResponse
{
    [JsonPropertyName("appId")]
    public required string AppId { get; init; }

    [JsonPropertyName("apiKey")]
    public required string ApiKey { get; init; }

    [JsonPropertyName("wikiUploadEndpoint")]
    public required string WikiUploadEndpoint { get; init; }

    [JsonPropertyName("status")]
    public string Status { get; init; } = "ready";
}

public record RegisteredAppRecord
{
    public required string AppId { get; init; }
    public required string ApiKey { get; init; }
    public required string AppName { get; init; }
    public required string Domain { get; init; }
    public string WikiPath { get; init; } = string.Empty;
    public DateTimeOffset RegisteredAt { get; init; }
}
