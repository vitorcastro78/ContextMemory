namespace ContextMemory.Core.Configuration;

public class ContextMemoryOptions
{
    public const string SectionName = "ContextMemory";

    public string ContentRootPath { get; set; } = AppContext.BaseDirectory;
    public string DataPath { get; set; } = "./data";
    public string WikiPath { get; set; } = "./wikis";
    public string OllamaEndpoint { get; set; } = "http://localhost:11434";
    public int OllamaRequestTimeoutSeconds { get; set; } = 600;
    public int MaxHistoryMessages { get; set; } = 20;
    public int MaxPayloadBytes { get; set; } = 1_048_576;
    public int WikiChunksTopK { get; set; } = 5;
    public float SimilarityThreshold { get; set; } = 0.65f;
    public int MaxChunkTokens { get; set; } = 512;
    public int ChunkOverlapTokens { get; set; } = 50;
    public int SummarizeAfterMessages { get; set; } = 50;
    public string MasterKey { get; set; } = string.Empty;
    public string LmStudioEndpoint { get; set; } = "http://localhost:1234";
    public string OpenAiEndpoint { get; set; } = "https://api.openai.com";
    public string OpenAiApiKey { get; set; } = string.Empty;
    public string AuditLogPath { get; set; } = "./data/audit";
    public bool EnableContentFilter { get; set; } = true;
    public bool EnableFeedback { get; set; } = true;
    public bool EnableMetrics { get; set; } = true;
    public bool AdminEnabled { get; set; } = true;
    public int DefaultRateLimitRpm { get; set; } = 60;
    public int DefaultRateLimitTpm { get; set; } = 100_000;
    public int ActiveUserWindowMinutes { get; set; } = 15;
    public List<string> AdminCorsOrigins { get; set; } = [];
    /// <summary>File (default) or Postgres. When Postgres, set ConnectionStrings:ContextMemory.</summary>
    public string PersistenceProvider { get; set; } = "File";
    public bool KnowledgeLoopEnabled { get; set; } = true;
    public int KnowledgeLoopMinMessages { get; set; } = 6;
    public float KnowledgeLoopAutoApproveThreshold { get; set; } = 0.75f;
    public float KnowledgeLoopManualReviewThreshold { get; set; } = 0.50f;
    public int KnowledgeLoopMaxChunksPerDay { get; set; } = 20;
    public int KnowledgeLoopProcessIntervalHours { get; set; } = 1;
    public bool PgVectorEnabled { get; set; } = true;
    public bool ToolCallEnabled { get; set; } = true;
    public int ToolCallMaxIterations { get; set; } = 5;
    public bool BillingEnabled { get; set; }
    public string DefaultPlan { get; set; } = "pro";
    public Dictionary<string, AppOptionsEntry> Apps { get; set; } = new();
}

public class AppOptionsEntry
{
    public string ApiKey { get; set; } = string.Empty;
    public string SystemPrompt { get; set; } = string.Empty;
    public string DefaultLanguage { get; set; } = "pt-PT";
    public string WikiPath { get; set; } = string.Empty;
    public int MaxHistoryMessages { get; set; } = 20;
    public int WikiChunksTopK { get; set; }
    public float SimilarityThreshold { get; set; }
}
