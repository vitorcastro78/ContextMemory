using ContextMemory.Core.Models;

namespace ContextMemory.Core.Contracts;

public interface ICompanyBrainStore
{
    bool TryGetCompany(string companyId, out CompanyProfile? company);
    IReadOnlyList<CompanyProfile> ListCompanies();
    bool RegisterCompany(CompanyProfile company);
    bool UpsertProcess(CompanyProcess process);
    IReadOnlyList<CompanyProcess> ListProcesses(string companyId);
    bool TryGetProcess(string companyId, string processId, out CompanyProcess? process);
    bool UpsertKnowledgeSource(KnowledgeSource source);
    IReadOnlyList<KnowledgeSource> ListKnowledgeSources(string companyId);
    bool LinkApp(string companyId, string appId);
    bool UnlinkApp(string companyId, string appId);
    bool TryGetCompanyForApp(string appId, out string? companyId);
    IReadOnlyList<string> ListLinkedApps(string companyId);
    void SaveSkillsCache(string companyId, CompanySkillsFile skillsFile);
    bool TryGetSkillsCache(string companyId, out CompanySkillsFile? skillsFile);
    bool TryGetWebhookSecret(string companyId, out string? secret);
    string SetWebhookSecret(string companyId, string? secret = null);
}
