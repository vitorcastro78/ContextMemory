using System.ComponentModel.DataAnnotations;
using ContextMemory.Core.Models;

namespace ContextMemory.Admin.UI.Models;

public sealed class AdminAppListItem
{
    public string AppId { get; set; } = string.Empty;
    public string Source { get; set; } = string.Empty;
    public AppTelemetryDto? Stats { get; set; }
}

public sealed class AppTelemetryDto
{
    public long RequestsTotal { get; set; }
    public long RequestsError { get; set; }
    public long TokensPrompt { get; set; }
    public long TokensCompletion { get; set; }
    public long RagHits { get; set; }
    public double AvgLatencyMs { get; set; }
    public double FeedbackScoreAvg { get; set; }
    public int ActiveUsers { get; set; }
    public Dictionary<string, long>? FilteredByReason { get; set; }
}

public sealed class AppStatsResponse
{
    public string AppId { get; set; } = string.Empty;
    public int ActiveUsers { get; set; }
    public AppTelemetryDto? Telemetry { get; set; }
    public double? FeedbackAverage { get; set; }
}

public sealed class AppCredentialsDto
{
    public string AppId { get; set; } = string.Empty;
    public string ApiKey { get; set; } = string.Empty;
    public string Source { get; set; } = string.Empty;
    public bool RotationPersists { get; set; }
}

public sealed class UserAdminSummaryDto
{
    public string UserId { get; set; } = string.Empty;
    public int FactCount { get; set; }
    public bool HasSessionContext { get; set; }
}

public sealed class UserAdminDetailDto
{
    public string UserId { get; set; } = string.Empty;
    public object? Profile { get; set; }
    public int ConversationMessageCount { get; set; }
    public bool HasSemanticMemory { get; set; }
}

public sealed class AdminSettings
{
    public string ApiBaseUrl { get; set; } = "http://localhost:5100";
    public string MasterKey { get; set; } = string.Empty;
}

public sealed class AdminApiException : Exception
{
    public int StatusCode { get; }

    public AdminApiException(int statusCode, string message) : base(message) =>
        StatusCode = statusCode;
}

public sealed class RegisterAppForm
{
    [Required(ErrorMessage = "Nome da aplicação é obrigatório.")]
    [StringLength(128, MinimumLength = 2)]
    public string AppName { get; set; } = string.Empty;

    [Required(ErrorMessage = "Domínio é obrigatório.")]
    [RegularExpression(@"^[a-zA-Z0-9-]+$", ErrorMessage = "Use apenas letras, números e hífen.")]
    [StringLength(64, MinimumLength = 2)]
    public string Domain { get; set; } = string.Empty;

    [Required]
    public string DefaultLanguage { get; set; } = "pt-PT";

    [Required]
    public string LlmBackend { get; set; } = "ollama";

    [Required]
    public string LlmModel { get; set; } = "llama3.2";

    public string? PromptPersona { get; set; }

    public string? WikiPath { get; set; }

    public RegisterAppRequest ToRequest() => new()
    {
        AppName = AppName.Trim(),
        Domain = Domain.Trim().ToLowerInvariant(),
        DefaultLanguage = DefaultLanguage.Trim(),
        LlmBackend = LlmBackend.Trim(),
        LlmModel = LlmModel.Trim(),
        PromptPersona = string.IsNullOrWhiteSpace(PromptPersona) ? string.Empty : PromptPersona.Trim(),
        WikiPath = string.IsNullOrWhiteSpace(WikiPath) ? null : WikiPath.Trim()
    };
}
