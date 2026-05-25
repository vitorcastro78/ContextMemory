using System.Text.Json;

namespace ContextMemory.Core.Models;

public record CompanyProfile
{
    public required string CompanyId { get; init; }
    public required string Name { get; init; }
    public string Description { get; init; } = string.Empty;
    public DateTimeOffset CreatedAt { get; init; }
    public DateTimeOffset UpdatedAt { get; init; }
}

public enum ProcessCategory
{
    General,
    Operations,
    Support,
    Engineering,
    Finance,
    Compliance
}

public record ProcessStep
{
    public int Order { get; init; }
    public required string Action { get; init; }
    public string? Condition { get; init; }
    public string? ToolHint { get; init; }
}

public enum ProcessPublishStatus
{
    Draft,
    Published
}

public record CompanyProcess
{
    public required string ProcessId { get; init; }
    public required string CompanyId { get; init; }
    public required string Title { get; init; }
    public string Summary { get; init; } = string.Empty;
    public ProcessCategory Category { get; init; } = ProcessCategory.General;
    public IReadOnlyList<string> Triggers { get; init; } = [];
    public IReadOnlyList<ProcessStep> Steps { get; init; } = [];
    public IReadOnlyList<string> Guardrails { get; init; } = [];
    public string? SourceRef { get; init; }
    public bool IsCritical { get; init; }
    public ProcessPublishStatus PublishStatus { get; init; } = ProcessPublishStatus.Published;
    public DateTimeOffset UpdatedAt { get; init; }
}

public enum KnowledgeSourceType
{
    MarkdownWiki,
    ProcessJsonFolder,
    Confluence,
    Slack,
    Zendesk,
    Notion,
    GitHub,
    GoogleDrive,
    SharePoint
}

public record KnowledgeSource
{
    public required string SourceId { get; init; }
    public required string CompanyId { get; init; }
    public KnowledgeSourceType Type { get; init; }
    public required string DisplayName { get; init; }
    public Dictionary<string, string> Settings { get; init; } = new();
    public bool Enabled { get; init; } = true;
    public DateTimeOffset? LastSyncedAt { get; init; }
}

public record CompanySkill
{
    public required string SkillId { get; init; }
    public required string Name { get; init; }
    public string Description { get; init; } = string.Empty;
    public IReadOnlyList<string> ProcessIds { get; init; } = [];
    public string Instructions { get; init; } = string.Empty;
}

public record CompanySkillsFile
{
    public required string CompanyId { get; init; }
    public required string Version { get; init; }
    public DateTimeOffset GeneratedAt { get; init; }
    public IReadOnlyList<CompanySkill> Skills { get; init; } = [];
    public IReadOnlyList<CompanyProcess> Processes { get; init; } = [];
}

public record RegisterCompanyRequest
{
    public required string CompanyId { get; init; }
    public required string Name { get; init; }
    public string Description { get; init; } = string.Empty;
}

public record UpsertProcessRequest
{
    public required string ProcessId { get; init; }
    public required string Title { get; init; }
    public string Summary { get; init; } = string.Empty;
    public ProcessCategory Category { get; init; } = ProcessCategory.General;
    public IReadOnlyList<string> Triggers { get; init; } = [];
    public IReadOnlyList<ProcessStep> Steps { get; init; } = [];
    public IReadOnlyList<string> Guardrails { get; init; } = [];
    public bool IsCritical { get; init; }
    public bool PublishImmediately { get; init; } = true;
}

public record AddKnowledgeSourceRequest
{
    public required string SourceId { get; init; }
    public KnowledgeSourceType Type { get; init; } = KnowledgeSourceType.MarkdownWiki;
    public required string DisplayName { get; init; }
    public Dictionary<string, string> Settings { get; init; } = new();
    public bool Enabled { get; init; } = true;
}

public record LinkAppRequest
{
    public required string AppId { get; init; }
}

public record CompanySyncResult
{
    public required string CompanyId { get; init; }
    public int SourcesSynced { get; init; }
    public int ProcessesUpserted { get; init; }
    public ProcessSyncDiff? Diff { get; init; }
    public ProcessSyncDiffDetail? DiffDetail { get; init; }
    public IReadOnlyList<SyncCriticalAlert> CriticalAlerts { get; init; } = [];
    public bool AlertDispatched { get; init; }
    public IReadOnlyList<string> Messages { get; init; } = [];
}

public record ProcessSyncDiff
{
    public IReadOnlyList<string> Added { get; init; } = [];
    public IReadOnlyList<string> Updated { get; init; } = [];
    public IReadOnlyList<string> Removed { get; init; } = [];
    public int TotalBefore { get; init; }
    public int TotalAfter { get; init; }
}

public record ProcessDiffItem
{
    public required string ProcessId { get; init; }
    public required string Title { get; init; }
    public required string ChangeType { get; init; }
    public bool IsCritical { get; init; }
}

public record ProcessFieldChange
{
    public required string Field { get; init; }
    public string? Before { get; init; }
    public string? After { get; init; }
}

public record ProcessSideBySideEntry
{
    public required string ProcessId { get; init; }
    public required string Title { get; init; }
    public required string ChangeType { get; init; }
    public bool IsCritical { get; init; }
    public string BeforeText { get; init; } = string.Empty;
    public string AfterText { get; init; } = string.Empty;
    public IReadOnlyList<ProcessFieldChange> Changes { get; init; } = [];
}

public record ProcessSyncDiffDetail
{
    public IReadOnlyList<ProcessDiffItem> Added { get; init; } = [];
    public IReadOnlyList<ProcessDiffItem> Updated { get; init; } = [];
    public IReadOnlyList<ProcessDiffItem> Removed { get; init; } = [];
    public int TotalBefore { get; init; }
    public int TotalAfter { get; init; }
    public IReadOnlyList<ProcessSideBySideEntry> SideBySide { get; init; } = [];
}

public record SyncCriticalAlert
{
    public required string ProcessId { get; init; }
    public required string Title { get; init; }
    public required string ChangeType { get; init; }
}

public record CompanyAlertConfig
{
    public required string CompanyId { get; init; }
    public string? OutboundWebhookUrl { get; init; }
    public bool Enabled { get; init; } = true;
}

public record UpdateCompanyAlertConfigRequest
{
    public string? OutboundWebhookUrl { get; init; }
    public bool Enabled { get; init; } = true;
}

public record SyncAlertPayload
{
    public required string CompanyId { get; init; }
    public DateTimeOffset SyncedAt { get; init; }
    public ProcessSyncDiffDetail? Diff { get; init; }
    public IReadOnlyList<SyncCriticalAlert> Alerts { get; init; } = [];
}

public record CompanySyncHistoryEntry
{
    public string EntryId { get; init; } = Guid.NewGuid().ToString("N");
    public required string CompanyId { get; init; }
    public DateTimeOffset SyncedAt { get; init; }
    public int SourcesSynced { get; init; }
    public int ProcessesUpserted { get; init; }
    public ProcessSyncDiff Diff { get; init; } = new();
    public ProcessSyncDiffDetail? DiffDetail { get; init; }
    public IReadOnlyList<SyncCriticalAlert> CriticalAlerts { get; init; } = [];
    public IReadOnlyList<string> Messages { get; init; } = [];
}

public record CompanyDetailResponse
{
    public required CompanyProfile Company { get; init; }
    public IReadOnlyList<string> LinkedApps { get; init; } = [];
    public int ProcessCount { get; init; }
    public int SourceCount { get; init; }
}

public record IngestKnowledgeRequest
{
    public string Format { get; init; } = "markdown";
    public required string Content { get; init; }
    public string? SourceLabel { get; init; }
}

public record IngestKnowledgeResult
{
    public required string CompanyId { get; init; }
    public int ProcessesUpserted { get; init; }
    public IReadOnlyList<string> ProcessIds { get; init; } = [];
}

public record McpSkillsExport
{
    public required string SchemaVersion { get; init; }
    public required string CompanyId { get; init; }
    public DateTimeOffset GeneratedAt { get; init; }
    public IReadOnlyList<McpToolDefinition> Tools { get; init; } = [];
    public IReadOnlyList<CompanySkill> Skills { get; init; } = [];
}

public record McpToolDefinition
{
    public required string Name { get; init; }
    public required string Description { get; init; }
    public required object InputSchema { get; init; }
}

public record WebhookSecretInfo
{
    public required string CompanyId { get; init; }
    public required string WebhookSecret { get; init; }
}

public record CompanyWebhookRequest
{
    public string Format { get; init; } = "markdown";
    public required string Content { get; init; }
    public string? SourceLabel { get; init; }
}

public record JsonRpcRequest
{
    public string Jsonrpc { get; init; } = "2.0";
    public object? Id { get; init; }
    public required string Method { get; init; }
    public JsonElement? Params { get; init; }
}

public record JsonRpcResponse
{
    public string Jsonrpc { get; init; } = "2.0";
    public object? Id { get; init; }
    public object? Result { get; init; }
    public JsonRpcError? Error { get; init; }
}

public record JsonRpcError
{
    public int Code { get; init; }
    public required string Message { get; init; }
}

public record CompanyImportResult
{
    public int CompaniesImported { get; init; }
    public int ProcessesImported { get; init; }
    public int SourcesImported { get; init; }
    public int AppLinksImported { get; init; }
    public IReadOnlyList<string> Messages { get; init; } = [];
}

public record ProcessSearchRequest
{
    public required string Query { get; init; }
    public int TopK { get; init; } = 5;
}

public record ProcessSearchResponse
{
    public required string CompanyId { get; init; }
    public required string Query { get; init; }
    public IReadOnlyList<CompanyProcess> Processes { get; init; } = [];
}

public record ProcessExecutionEntry
{
    public required string CompanyId { get; init; }
    public required string ToolName { get; init; }
    public string? Context { get; init; }
    public DateTimeOffset ExecutedAt { get; init; }
}

public record CompanyBrainMetricsSnapshot
{
    public required string CompanyId { get; init; }
    public long SyncTotal { get; init; }
    public long SyncErrors { get; init; }
    public long ProcessesUpserted { get; init; }
    public long ProcessesAdded { get; init; }
    public long ProcessesUpdated { get; init; }
    public long ProcessesRemoved { get; init; }
    public long SearchesTotal { get; init; }
    public long McpCallsTotal { get; init; }
    public long IngestProcesses { get; init; }
    public long WebhooksTotal { get; init; }
    public long AlertsTotal { get; init; }
    public long ApprovalsTotal { get; init; }
    public int PendingApprovals { get; init; }
}

public record CompanyBrainDashboardRow
{
    public required string CompanyId { get; init; }
    public required string Name { get; init; }
    public int ProcessCount { get; init; }
    public int PublishedCount { get; init; }
    public int PendingApprovals { get; init; }
    public int CriticalCount { get; init; }
    public int SourceCount { get; init; }
    public int LinkedAppCount { get; init; }
    public required CompanyBrainMetricsSnapshot Metrics { get; init; }
}

public record CompanyBrainGlobalDashboard
{
    public int CompanyCount { get; init; }
    public int TotalProcesses { get; init; }
    public int TotalPublished { get; init; }
    public int TotalPendingApprovals { get; init; }
    public int TotalCriticalProcesses { get; init; }
    public int TotalSources { get; init; }
    public int TotalLinkedApps { get; init; }
    public long TotalSyncs { get; init; }
    public long TotalSyncErrors { get; init; }
    public long TotalSearches { get; init; }
    public long TotalMcpCalls { get; init; }
    public long TotalApprovals { get; init; }
    public long TotalAlerts { get; init; }
    public IReadOnlyList<CompanyBrainDashboardRow> Companies { get; init; } = [];
}

public record ProcessApprovalResult
{
    public required string CompanyId { get; init; }
    public required string ProcessId { get; init; }
    public ProcessPublishStatus PublishStatus { get; init; }
}

public record BulkApprovalResult
{
    public required string CompanyId { get; init; }
    public int ApprovedCount { get; init; }
    public IReadOnlyList<string> ProcessIds { get; init; } = [];
}
