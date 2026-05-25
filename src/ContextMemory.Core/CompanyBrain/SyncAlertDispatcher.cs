using System.Net.Http.Json;
using System.Text.Json;
using ContextMemory.Core.Configuration;
using ContextMemory.Core.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace ContextMemory.Core.CompanyBrain;

public sealed class CompanyAlertConfigStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true
    };

    private readonly string _companiesRoot;

    public CompanyAlertConfigStore(IOptions<ContextMemoryOptions> options)
    {
        var config = options.Value;
        _companiesRoot = Path.Combine(
            Path.GetFullPath(config.DataPath, config.ContentRootPath),
            "companies");
    }

    public CompanyAlertConfig Get(string companyId)
    {
        var path = ConfigPath(companyId);
        if (!File.Exists(path))
            return new CompanyAlertConfig { CompanyId = companyId };

        try
        {
            return JsonSerializer.Deserialize<CompanyAlertConfig>(File.ReadAllText(path), JsonOptions)
                ?? new CompanyAlertConfig { CompanyId = companyId };
        }
        catch
        {
            return new CompanyAlertConfig { CompanyId = companyId };
        }
    }

    public CompanyAlertConfig Save(string companyId, CompanyAlertConfig config)
    {
        var companyDir = Path.Combine(_companiesRoot, companyId);
        Directory.CreateDirectory(companyDir);

        var saved = config with { CompanyId = companyId };
        File.WriteAllText(ConfigPath(companyId), JsonSerializer.Serialize(saved, JsonOptions));
        return saved;
    }

    private string ConfigPath(string companyId) =>
        Path.Combine(_companiesRoot, companyId, "alert-config.json");
}

public sealed class SyncAlertDispatcher
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    private readonly HttpClient _http;
    private readonly CompanyAlertConfigStore _configStore;
    private readonly ILogger<SyncAlertDispatcher> _logger;

    public SyncAlertDispatcher(
        HttpClient http,
        CompanyAlertConfigStore configStore,
        ILogger<SyncAlertDispatcher> logger)
    {
        _http = http;
        _configStore = configStore;
        _logger = logger;
    }

    public async Task<bool> DispatchAsync(
        string companyId,
        CompanySyncResult syncResult,
        CancellationToken cancellationToken = default)
    {
        var alerts = syncResult.CriticalAlerts;
        if (alerts.Count == 0)
            return false;

        var config = _configStore.Get(companyId);
        if (!config.Enabled || string.IsNullOrWhiteSpace(config.OutboundWebhookUrl))
            return false;

        var payload = new SyncAlertPayload
        {
            CompanyId = companyId,
            SyncedAt = DateTimeOffset.UtcNow,
            Diff = syncResult.DiffDetail,
            Alerts = alerts
        };

        try
        {
            using var response = await _http.PostAsJsonAsync(
                config.OutboundWebhookUrl.Trim(),
                payload,
                JsonOptions,
                cancellationToken).ConfigureAwait(false);

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning(
                    "Sync alert webhook returned {StatusCode} for company {CompanyId}.",
                    (int)response.StatusCode,
                    companyId);
                return false;
            }

            return true;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to dispatch sync alert for company {CompanyId}.", companyId);
            return false;
        }
    }
}
