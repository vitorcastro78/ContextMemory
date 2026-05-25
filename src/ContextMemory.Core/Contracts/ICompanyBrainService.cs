using ContextMemory.Core.CompanyBrain;
using ContextMemory.Core.Models;

namespace ContextMemory.Core.Contracts;

public interface ICompanyBrainService
{
    CompanyProfile RegisterCompany(RegisterCompanyRequest request);
    CompanyProcess UpsertProcess(string companyId, UpsertProcessRequest request);
    KnowledgeSource AddKnowledgeSource(string companyId, AddKnowledgeSourceRequest request);
    void LinkApp(string companyId, string appId);
    void UnlinkApp(string companyId, string appId);
    Task<CompanySyncResult> SyncAsync(string companyId, CancellationToken cancellationToken = default);
    CompanySkillsFile CompileSkills(string companyId);
    IReadOnlyList<CompanyProcess> MatchProcessesForQuery(string appId, string query, int topK = 3);
    IReadOnlyList<CompanyProcess> SearchProcesses(string companyId, string query, int topK = 5);
    IngestKnowledgeResult IngestKnowledge(string companyId, IngestKnowledgeRequest request);
    WebhookSecretInfo RotateWebhookSecret(string companyId);
    bool ValidateWebhookSignature(string companyId, string body, string? signatureHeader);
    bool ValidateCompanyToken(string companyId, string token);
    JsonRpcResponse HandleMcpRequest(string companyId, JsonRpcRequest request);
    CompanyImportResult ImportFromDisk();
    IReadOnlyList<CompanySyncHistoryEntry> GetSyncHistory(string companyId, int limit = 20);
    CompanyAlertConfig GetAlertConfig(string companyId);
    CompanyAlertConfig UpdateAlertConfig(string companyId, UpdateCompanyAlertConfigRequest request);
    IReadOnlyList<CompanyProcess> ListPendingProcesses(string companyId);
    ProcessApprovalResult ApproveProcess(string companyId, string processId);
    BulkApprovalResult ApproveAllPending(string companyId);
    CompanyBrainMetricsSnapshot GetMetrics(string companyId);
    CompanyBrainGlobalDashboard GetGlobalDashboard();
    CompanySyncHistoryEntry? GetSyncHistoryEntry(string companyId, string entryId);
    SharePointOAuthStartResult StartSharePointOAuth(string companyId, string sourceId);
    Task<string> CompleteSharePointOAuthAsync(string code, string state, CancellationToken cancellationToken = default);
    SharePointOAuthStatus GetSharePointOAuthStatus(string companyId, string sourceId);
}
