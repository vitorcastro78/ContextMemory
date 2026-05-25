using System.Collections.Concurrent;
using System.Text;
using ContextMemory.Core.Models;

namespace ContextMemory.Core.CompanyBrain;

public sealed class CompanyBrainTelemetry
{
    private readonly ConcurrentDictionary<string, CompanyMetrics> _companies = new(StringComparer.Ordinal);

    public void RecordSync(string companyId, CompanySyncResult result, bool success)
    {
        var metrics = _companies.GetOrAdd(companyId, _ => new CompanyMetrics());
        Interlocked.Increment(ref metrics.SyncTotal);
        if (!success)
            Interlocked.Increment(ref metrics.SyncErrors);

        Interlocked.Add(ref metrics.ProcessesUpserted, result.ProcessesUpserted);
        if (result.Diff is not null)
        {
            Interlocked.Add(ref metrics.ProcessesAdded, result.Diff.Added.Count);
            Interlocked.Add(ref metrics.ProcessesUpdated, result.Diff.Updated.Count);
            Interlocked.Add(ref metrics.ProcessesRemoved, result.Diff.Removed.Count);
        }
    }

    public void RecordSearch(string companyId) =>
        Interlocked.Increment(ref GetMetrics(companyId).SearchesTotal);

    public void RecordMcpCall(string companyId) =>
        Interlocked.Increment(ref GetMetrics(companyId).McpCallsTotal);

    public void RecordIngest(string companyId, int processesUpserted) =>
        Interlocked.Add(ref GetMetrics(companyId).IngestProcesses, processesUpserted);

    public void RecordWebhook(string companyId) =>
        Interlocked.Increment(ref GetMetrics(companyId).WebhooksTotal);

    public void RecordAlert(string companyId) =>
        Interlocked.Increment(ref GetMetrics(companyId).AlertsTotal);

    public void RecordApproval(string companyId) =>
        Interlocked.Increment(ref GetMetrics(companyId).ApprovalsTotal);

    public CompanyBrainMetricsSnapshot GetSnapshot(string companyId, int pendingApprovals = 0)
    {
        if (!_companies.TryGetValue(companyId, out var m))
        {
            return new CompanyBrainMetricsSnapshot
            {
                CompanyId = companyId,
                PendingApprovals = pendingApprovals
            };
        }

        return new CompanyBrainMetricsSnapshot
        {
            CompanyId = companyId,
            SyncTotal = m.SyncTotal,
            SyncErrors = m.SyncErrors,
            ProcessesUpserted = m.ProcessesUpserted,
            ProcessesAdded = m.ProcessesAdded,
            ProcessesUpdated = m.ProcessesUpdated,
            ProcessesRemoved = m.ProcessesRemoved,
            SearchesTotal = m.SearchesTotal,
            McpCallsTotal = m.McpCallsTotal,
            IngestProcesses = m.IngestProcesses,
            WebhooksTotal = m.WebhooksTotal,
            AlertsTotal = m.AlertsTotal,
            ApprovalsTotal = m.ApprovalsTotal,
            PendingApprovals = pendingApprovals
        };
    }

    public IReadOnlyDictionary<string, CompanyBrainMetricsSnapshot> GetAllSnapshots(
        Func<string, int>? pendingCountResolver = null)
    {
        pendingCountResolver ??= _ => 0;
        return _companies.Keys.ToDictionary(
            id => id,
            id => GetSnapshot(id, pendingCountResolver(id)),
            StringComparer.Ordinal);
    }

    public string ExportPrometheus()
    {
        var sb = new StringBuilder();
        foreach (var (companyId, m) in _companies)
        {
            var label = EscapeLabel(companyId);
            sb.AppendLine($"cm_company_brain_sync_total{{companyId=\"{label}\"}} {m.SyncTotal}");
            sb.AppendLine($"cm_company_brain_sync_errors_total{{companyId=\"{label}\"}} {m.SyncErrors}");
            sb.AppendLine($"cm_company_brain_processes_upserted_total{{companyId=\"{label}\"}} {m.ProcessesUpserted}");
            sb.AppendLine($"cm_company_brain_processes_added_total{{companyId=\"{label}\"}} {m.ProcessesAdded}");
            sb.AppendLine($"cm_company_brain_processes_updated_total{{companyId=\"{label}\"}} {m.ProcessesUpdated}");
            sb.AppendLine($"cm_company_brain_processes_removed_total{{companyId=\"{label}\"}} {m.ProcessesRemoved}");
            sb.AppendLine($"cm_company_brain_searches_total{{companyId=\"{label}\"}} {m.SearchesTotal}");
            sb.AppendLine($"cm_company_brain_mcp_calls_total{{companyId=\"{label}\"}} {m.McpCallsTotal}");
            sb.AppendLine($"cm_company_brain_ingest_processes_total{{companyId=\"{label}\"}} {m.IngestProcesses}");
            sb.AppendLine($"cm_company_brain_webhooks_total{{companyId=\"{label}\"}} {m.WebhooksTotal}");
            sb.AppendLine($"cm_company_brain_alerts_total{{companyId=\"{label}\"}} {m.AlertsTotal}");
            sb.AppendLine($"cm_company_brain_approvals_total{{companyId=\"{label}\"}} {m.ApprovalsTotal}");
        }

        return sb.ToString();
    }

    private CompanyMetrics GetMetrics(string companyId) =>
        _companies.GetOrAdd(companyId, _ => new CompanyMetrics());

    private static string EscapeLabel(string value) =>
        value.Replace("\\", "\\\\").Replace("\"", "\\\"");

    private sealed class CompanyMetrics
    {
        public long SyncTotal;
        public long SyncErrors;
        public long ProcessesUpserted;
        public long ProcessesAdded;
        public long ProcessesUpdated;
        public long ProcessesRemoved;
        public long SearchesTotal;
        public long McpCallsTotal;
        public long IngestProcesses;
        public long WebhooksTotal;
        public long AlertsTotal;
        public long ApprovalsTotal;
    }
}
