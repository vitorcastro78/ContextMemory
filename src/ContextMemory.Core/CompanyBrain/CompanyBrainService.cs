using ContextMemory.Core.CompanyBrain.Connectors;
using ContextMemory.Core.Contracts;
using ContextMemory.Core.Models;
using ContextMemory.Core.Security;

namespace ContextMemory.Core.CompanyBrain;

public sealed class CompanyBrainService : ICompanyBrainService
{
    private readonly ICompanyBrainStore _store;
    private readonly IReadOnlyDictionary<KnowledgeSourceType, IKnowledgeSourceConnector> _connectors;
    private readonly IAppRegistry _appRegistry;
    private readonly CompanyBrainDiskImporter _diskImporter;
    private readonly ProcessEmbeddingIndex _embeddingIndex;
    private readonly ProcessExecutionLogger _executionLogger;
    private readonly SyncHistoryStore _syncHistory;
    private readonly CompanyBrainTelemetry _telemetry;
    private readonly SyncAlertDispatcher _alertDispatcher;
    private readonly CompanyAlertConfigStore _alertConfigStore;
    private readonly SharePointOAuthService _sharePointOAuth;

    public CompanyBrainService(
        ICompanyBrainStore store,
        IEnumerable<IKnowledgeSourceConnector> connectors,
        IAppRegistry appRegistry,
        CompanyBrainDiskImporter diskImporter,
        ProcessEmbeddingIndex embeddingIndex,
        ProcessExecutionLogger executionLogger,
        SyncHistoryStore syncHistory,
        CompanyBrainTelemetry telemetry,
        SyncAlertDispatcher alertDispatcher,
        CompanyAlertConfigStore alertConfigStore,
        SharePointOAuthService sharePointOAuth)
    {
        _store = store;
        _connectors = connectors.ToDictionary(c => c.SourceType);
        _appRegistry = appRegistry;
        _diskImporter = diskImporter;
        _embeddingIndex = embeddingIndex;
        _executionLogger = executionLogger;
        _syncHistory = syncHistory;
        _telemetry = telemetry;
        _alertDispatcher = alertDispatcher;
        _alertConfigStore = alertConfigStore;
        _sharePointOAuth = sharePointOAuth;
    }

    public CompanyProfile RegisterCompany(RegisterCompanyRequest request)
    {
        if (!IdentifierValidator.IsValid(request.CompanyId))
            throw new ArgumentException("Invalid companyId format.");

        if (string.IsNullOrWhiteSpace(request.Name))
            throw new ArgumentException("Name is required.");

        var now = DateTimeOffset.UtcNow;
        var company = new CompanyProfile
        {
            CompanyId = request.CompanyId,
            Name = request.Name.Trim(),
            Description = request.Description.Trim(),
            CreatedAt = now,
            UpdatedAt = now
        };

        if (!_store.RegisterCompany(company))
            throw new InvalidOperationException($"Company '{request.CompanyId}' already exists.");

        return company;
    }

    public CompanyProcess UpsertProcess(string companyId, UpsertProcessRequest request)
    {
        EnsureCompanyExists(companyId);

        if (!IdentifierValidator.IsValid(request.ProcessId))
            throw new ArgumentException("Invalid processId format.");

        if (string.IsNullOrWhiteSpace(request.Title))
            throw new ArgumentException("Title is required.");

        var process = new CompanyProcess
        {
            ProcessId = request.ProcessId,
            CompanyId = companyId,
            Title = request.Title.Trim(),
            Summary = request.Summary.Trim(),
            Category = request.Category,
            Triggers = request.Triggers,
            Steps = request.Steps,
            Guardrails = request.Guardrails,
            IsCritical = request.IsCritical,
            PublishStatus = request.PublishImmediately
                ? ProcessPublishStatus.Published
                : ProcessPublishStatus.Draft,
            UpdatedAt = DateTimeOffset.UtcNow
        };

        if (!_store.UpsertProcess(process))
            throw new InvalidOperationException("Failed to save process.");

        if (process.PublishStatus == ProcessPublishStatus.Published)
            CompileSkills(companyId);

        return process;
    }

    public KnowledgeSource AddKnowledgeSource(string companyId, AddKnowledgeSourceRequest request)
    {
        EnsureCompanyExists(companyId);

        if (!IdentifierValidator.IsValid(request.SourceId))
            throw new ArgumentException("Invalid sourceId format.");

        if (string.IsNullOrWhiteSpace(request.DisplayName))
            throw new ArgumentException("DisplayName is required.");

        var source = new KnowledgeSource
        {
            SourceId = request.SourceId,
            CompanyId = companyId,
            Type = request.Type,
            DisplayName = request.DisplayName.Trim(),
            Settings = new Dictionary<string, string>(request.Settings, StringComparer.OrdinalIgnoreCase),
            Enabled = request.Enabled
        };

        if (!_store.UpsertKnowledgeSource(source))
            throw new InvalidOperationException("Failed to save knowledge source.");

        return source;
    }

    public void LinkApp(string companyId, string appId)
    {
        EnsureCompanyExists(companyId);

        if (!IdentifierValidator.IsValid(appId))
            throw new ArgumentException("Invalid appId format.");

        if (!_appRegistry.TryGetApp(appId, out _))
            throw new InvalidOperationException($"App '{appId}' not found.");

        if (!_store.LinkApp(companyId, appId))
            throw new InvalidOperationException($"App '{appId}' is already linked to another company.");
    }

    public void UnlinkApp(string companyId, string appId)
    {
        EnsureCompanyExists(companyId);

        if (!_store.UnlinkApp(companyId, appId))
            throw new InvalidOperationException($"App '{appId}' is not linked to company '{companyId}'.");
    }

    public async Task<CompanySyncResult> SyncAsync(string companyId, CancellationToken cancellationToken = default)
    {
        EnsureCompanyExists(companyId);

        var before = _store.ListProcesses(companyId);
        var sources = _store.ListKnowledgeSources(companyId).Where(s => s.Enabled).ToList();
        var upserted = 0;
        var messages = new List<string>();

        try
        {
            foreach (var source in sources)
            {
                if (!_connectors.TryGetValue(source.Type, out var connector))
                {
                    messages.Add($"No connector registered for source type '{source.Type}'.");
                    continue;
                }

                var result = await connector.SyncAsync(source, cancellationToken).ConfigureAwait(false);
                messages.AddRange(result.Messages);

                foreach (var process in result.Processes)
                {
                    var draft = process with { PublishStatus = ProcessPublishStatus.Draft };
                    if (_store.UpsertProcess(draft))
                        upserted++;
                }

                _store.UpsertKnowledgeSource(source with { LastSyncedAt = DateTimeOffset.UtcNow });
            }

            var skills = CompileSkills(companyId);
            _store.SaveSkillsCache(companyId, skills);

            var syncResult = BuildSyncResult(companyId, before, sources.Count, upserted, messages);
            syncResult = await FinalizeSyncAsync(companyId, syncResult, cancellationToken).ConfigureAwait(false);
            _telemetry.RecordSync(companyId, syncResult, success: true);
            _syncHistory.Append(ToHistoryEntry(syncResult));
            return syncResult;
        }
        catch
        {
            var failedResult = BuildSyncResult(companyId, before, sources.Count, upserted, messages);
            _telemetry.RecordSync(companyId, failedResult, success: false);
            throw;
        }
    }

    public CompanySkillsFile CompileSkills(string companyId)
    {
        EnsureCompanyExists(companyId);
        var processes = GetPublishedProcesses(companyId);
        var skills = SkillsCompiler.Compile(companyId, processes);
        _store.SaveSkillsCache(companyId, skills);
        RefreshProcessIndex(companyId);
        return skills;
    }

    public IReadOnlyList<CompanyProcess> MatchProcessesForQuery(string appId, string query, int topK = 3)
    {
        if (!_store.TryGetCompanyForApp(appId, out var companyId) || companyId is null)
            return [];

        return SearchProcesses(companyId, query, topK);
    }

    public IReadOnlyList<CompanyProcess> SearchProcesses(string companyId, string query, int topK = 5)
    {
        EnsureCompanyExists(companyId);
        _telemetry.RecordSearch(companyId);
        var processes = GetPublishedProcesses(companyId);
        if (processes.Count == 0)
            return [];

        var embeddings = _embeddingIndex.TryGet(companyId);
        var queryVector = _embeddingIndex.EmbedQuery(query);
        return ProcessMatcher.Rank(processes, query, embeddings, queryVector, topK);
    }

    public IngestKnowledgeResult IngestKnowledge(string companyId, IngestKnowledgeRequest request)
    {
        EnsureCompanyExists(companyId);

        if (string.IsNullOrWhiteSpace(request.Content))
            throw new ArgumentException("Content is required.");

        var format = request.Format.Trim().ToLowerInvariant();
        var processes = format switch
        {
            "markdown" or "md" => MarkdownWikiConnector.ExtractProcesses(
                companyId,
                request.SourceLabel ?? "ingest",
                request.Content),
            "json" => ProcessJsonFolderConnector.ParseJsonFile(
                companyId,
                request.SourceLabel ?? "ingest.json",
                request.Content),
            _ => throw new ArgumentException($"Unsupported ingest format '{request.Format}'. Use markdown or json.")
        };

        var ids = new List<string>();
        foreach (var process in processes)
        {
            var draft = process with { PublishStatus = ProcessPublishStatus.Draft };
            if (_store.UpsertProcess(draft))
                ids.Add(process.ProcessId);
        }

        if (ids.Count > 0)
            _store.SaveSkillsCache(companyId, CompileSkills(companyId));

        _telemetry.RecordIngest(companyId, ids.Count);

        return new IngestKnowledgeResult
        {
            CompanyId = companyId,
            ProcessesUpserted = ids.Count,
            ProcessIds = ids
        };
    }

    public WebhookSecretInfo RotateWebhookSecret(string companyId)
    {
        EnsureCompanyExists(companyId);
        var secret = _store.SetWebhookSecret(companyId);
        return new WebhookSecretInfo { CompanyId = companyId, WebhookSecret = secret };
    }

    public bool ValidateWebhookSignature(string companyId, string body, string? signatureHeader)
    {
        if (!_store.TryGetWebhookSecret(companyId, out var secret) || secret is null)
            return false;
        return CompanyWebhookAuth.Validate(secret, body, signatureHeader);
    }

    public bool ValidateCompanyToken(string companyId, string token)
    {
        if (string.IsNullOrWhiteSpace(token))
            return false;
        return _store.TryGetWebhookSecret(companyId, out var secret)
            && string.Equals(secret, token, StringComparison.Ordinal);
    }

    public JsonRpcResponse HandleMcpRequest(string companyId, JsonRpcRequest request)
    {
        EnsureCompanyExists(companyId);
        var skills = CompileSkills(companyId);
        var ctx = new McpServerContext(
            companyId,
            skills,
            (query, topK) => SearchProcesses(companyId, query, topK),
            (toolName, context) =>
            {
                _telemetry.RecordMcpCall(companyId);
                _executionLogger.Log(companyId, toolName, context);
            });
        return McpJsonRpcServer.Handle(ctx, request);
    }

    public CompanyImportResult ImportFromDisk() => _diskImporter.ImportAll();

    public IReadOnlyList<CompanySyncHistoryEntry> GetSyncHistory(string companyId, int limit = 20)
    {
        EnsureCompanyExists(companyId);
        return _syncHistory.ListRecent(companyId, limit);
    }

    public CompanyAlertConfig GetAlertConfig(string companyId)
    {
        EnsureCompanyExists(companyId);
        return _alertConfigStore.Get(companyId);
    }

    public CompanyAlertConfig UpdateAlertConfig(string companyId, UpdateCompanyAlertConfigRequest request)
    {
        EnsureCompanyExists(companyId);
        return _alertConfigStore.Save(companyId, new CompanyAlertConfig
        {
            CompanyId = companyId,
            OutboundWebhookUrl = request.OutboundWebhookUrl?.Trim(),
            Enabled = request.Enabled
        });
    }

    public IReadOnlyList<CompanyProcess> ListPendingProcesses(string companyId)
    {
        EnsureCompanyExists(companyId);
        return _store.ListProcesses(companyId)
            .Where(p => p.PublishStatus == ProcessPublishStatus.Draft)
            .OrderBy(p => p.Title)
            .ToList();
    }

    public ProcessApprovalResult ApproveProcess(string companyId, string processId)
    {
        EnsureCompanyExists(companyId);

        if (!_store.TryGetProcess(companyId, processId, out var process) || process is null)
            throw new InvalidOperationException($"Process '{processId}' not found.");

        if (process.PublishStatus == ProcessPublishStatus.Published)
        {
            return new ProcessApprovalResult
            {
                CompanyId = companyId,
                ProcessId = processId,
                PublishStatus = ProcessPublishStatus.Published
            };
        }

        var published = process with
        {
            PublishStatus = ProcessPublishStatus.Published,
            UpdatedAt = DateTimeOffset.UtcNow
        };

        if (!_store.UpsertProcess(published))
            throw new InvalidOperationException("Failed to approve process.");

        CompileSkills(companyId);
        _telemetry.RecordApproval(companyId);

        return new ProcessApprovalResult
        {
            CompanyId = companyId,
            ProcessId = processId,
            PublishStatus = ProcessPublishStatus.Published
        };
    }

    public BulkApprovalResult ApproveAllPending(string companyId)
    {
        EnsureCompanyExists(companyId);
        var pending = ListPendingProcesses(companyId);
        var approved = new List<string>();

        foreach (var process in pending)
        {
            var published = process with
            {
                PublishStatus = ProcessPublishStatus.Published,
                UpdatedAt = DateTimeOffset.UtcNow
            };
            if (_store.UpsertProcess(published))
                approved.Add(process.ProcessId);
        }

        if (approved.Count > 0)
        {
            CompileSkills(companyId);
            for (var i = 0; i < approved.Count; i++)
                _telemetry.RecordApproval(companyId);
        }

        return new BulkApprovalResult
        {
            CompanyId = companyId,
            ApprovedCount = approved.Count,
            ProcessIds = approved
        };
    }

    public CompanyBrainMetricsSnapshot GetMetrics(string companyId)
    {
        EnsureCompanyExists(companyId);
        var pending = ListPendingProcesses(companyId).Count;
        return _telemetry.GetSnapshot(companyId, pending);
    }

    public CompanySyncHistoryEntry? GetSyncHistoryEntry(string companyId, string entryId)
    {
        EnsureCompanyExists(companyId);
        return _syncHistory.TryGetEntry(companyId, entryId);
    }

    public SharePointOAuthStartResult StartSharePointOAuth(string companyId, string sourceId) =>
        _sharePointOAuth.StartAuthorization(companyId, sourceId);

    public Task<string> CompleteSharePointOAuthAsync(
        string code,
        string state,
        CancellationToken cancellationToken = default) =>
        _sharePointOAuth.CompleteAuthorizationAsync(code, state, cancellationToken);

    public SharePointOAuthStatus GetSharePointOAuthStatus(string companyId, string sourceId) =>
        _sharePointOAuth.GetSourceStatus(companyId, sourceId);

    public CompanyBrainGlobalDashboard GetGlobalDashboard()
    {
        var companies = _store.ListCompanies();
        var rows = new List<CompanyBrainDashboardRow>(companies.Count);
        var totalProcesses = 0;
        var totalPublished = 0;
        var totalPending = 0;
        var totalCritical = 0;
        var totalSources = 0;
        var totalLinkedApps = 0;
        long totalSyncs = 0;
        long totalSyncErrors = 0;
        long totalSearches = 0;
        long totalMcpCalls = 0;
        long totalApprovals = 0;
        long totalAlerts = 0;

        foreach (var company in companies)
        {
            var processes = _store.ListProcesses(company.CompanyId);
            var pending = processes.Count(p => p.PublishStatus == ProcessPublishStatus.Draft);
            var published = processes.Count - pending;
            var critical = processes.Count(p => p.IsCritical);
            var metrics = _telemetry.GetSnapshot(company.CompanyId, pending);
            var sources = _store.ListKnowledgeSources(company.CompanyId).Count;
            var linkedApps = _store.ListLinkedApps(company.CompanyId).Count;

            rows.Add(new CompanyBrainDashboardRow
            {
                CompanyId = company.CompanyId,
                Name = company.Name,
                ProcessCount = processes.Count,
                PublishedCount = published,
                PendingApprovals = pending,
                CriticalCount = critical,
                SourceCount = sources,
                LinkedAppCount = linkedApps,
                Metrics = metrics
            });

            totalProcesses += processes.Count;
            totalPublished += published;
            totalPending += pending;
            totalCritical += critical;
            totalSources += sources;
            totalLinkedApps += linkedApps;
            totalSyncs += metrics.SyncTotal;
            totalSyncErrors += metrics.SyncErrors;
            totalSearches += metrics.SearchesTotal;
            totalMcpCalls += metrics.McpCallsTotal;
            totalApprovals += metrics.ApprovalsTotal;
            totalAlerts += metrics.AlertsTotal;
        }

        rows.Sort((a, b) =>
        {
            var pendingCompare = b.PendingApprovals.CompareTo(a.PendingApprovals);
            return pendingCompare != 0 ? pendingCompare : string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase);
        });

        return new CompanyBrainGlobalDashboard
        {
            CompanyCount = companies.Count,
            TotalProcesses = totalProcesses,
            TotalPublished = totalPublished,
            TotalPendingApprovals = totalPending,
            TotalCriticalProcesses = totalCritical,
            TotalSources = totalSources,
            TotalLinkedApps = totalLinkedApps,
            TotalSyncs = totalSyncs,
            TotalSyncErrors = totalSyncErrors,
            TotalSearches = totalSearches,
            TotalMcpCalls = totalMcpCalls,
            TotalApprovals = totalApprovals,
            TotalAlerts = totalAlerts,
            Companies = rows
        };
    }

    private IReadOnlyList<CompanyProcess> GetPublishedProcesses(string companyId) =>
        _store.ListProcesses(companyId)
            .Where(p => p.PublishStatus == ProcessPublishStatus.Published)
            .ToList();

    private void RefreshProcessIndex(string companyId)
    {
        var processes = GetPublishedProcesses(companyId);
        _embeddingIndex.Rebuild(companyId, processes);
    }

    private CompanySyncResult BuildSyncResult(
        string companyId,
        IReadOnlyList<CompanyProcess> before,
        int sourcesSynced,
        int upserted,
        IReadOnlyList<string> messages)
    {
        var after = _store.ListProcesses(companyId);
        var diff = ProcessSyncDiffer.Compute(before, after);
        var detail = ProcessSyncDiffEnricher.Enrich(diff, before, after);
        var criticalAlerts = ProcessSyncDiffEnricher.BuildCriticalAlerts(detail);

        return new CompanySyncResult
        {
            CompanyId = companyId,
            SourcesSynced = sourcesSynced,
            ProcessesUpserted = upserted,
            Diff = diff,
            DiffDetail = detail,
            CriticalAlerts = criticalAlerts,
            Messages = messages
        };
    }

    private async Task<CompanySyncResult> FinalizeSyncAsync(
        string companyId,
        CompanySyncResult result,
        CancellationToken cancellationToken)
    {
        if (result.CriticalAlerts.Count == 0)
            return result;

        var dispatched = await _alertDispatcher.DispatchAsync(companyId, result, cancellationToken)
            .ConfigureAwait(false);
        if (dispatched)
            _telemetry.RecordAlert(companyId);

        return result with { AlertDispatched = dispatched };
    }

    private static CompanySyncHistoryEntry ToHistoryEntry(CompanySyncResult result) =>
        new()
        {
            EntryId = Guid.NewGuid().ToString("N"),
            CompanyId = result.CompanyId,
            SyncedAt = DateTimeOffset.UtcNow,
            SourcesSynced = result.SourcesSynced,
            ProcessesUpserted = result.ProcessesUpserted,
            Diff = result.Diff ?? new ProcessSyncDiff(),
            DiffDetail = result.DiffDetail,
            CriticalAlerts = result.CriticalAlerts,
            Messages = result.Messages
        };

    private void EnsureCompanyExists(string companyId)
    {
        if (!_store.TryGetCompany(companyId, out _))
            throw new InvalidOperationException($"Company '{companyId}' not found.");
    }
}
