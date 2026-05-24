using System.Text.Json;
using ContextMemory.Admin.UI.Models;
using ContextMemory.Admin.UI.Services;

namespace ContextMemory.Admin.Desktop;

public sealed class DesktopAdminSettingsStorage : IAdminSettingsStorage
{
    private const string ApiUrlKey = "contextmemory.admin.apiurl";
    private const string MasterKeyKey = "contextmemory.admin.masterkey";

    public Task<AdminSettings?> LoadAsync(CancellationToken cancellationToken = default)
    {
        var apiBaseUrl = Preferences.Get(ApiUrlKey, "http://localhost:5100");
        var masterKey = Preferences.Get(MasterKeyKey, string.Empty);
        if (string.IsNullOrWhiteSpace(apiBaseUrl) && string.IsNullOrWhiteSpace(masterKey))
            return Task.FromResult<AdminSettings?>(null);

        return Task.FromResult<AdminSettings?>(new AdminSettings
        {
            ApiBaseUrl = apiBaseUrl,
            MasterKey = masterKey
        });
    }

    public Task SaveAsync(AdminSettings settings, CancellationToken cancellationToken = default)
    {
        Preferences.Set(ApiUrlKey, settings.ApiBaseUrl ?? string.Empty);
        Preferences.Set(MasterKeyKey, settings.MasterKey ?? string.Empty);
        return Task.CompletedTask;
    }

    public Task ClearAsync(CancellationToken cancellationToken = default)
    {
        Preferences.Remove(ApiUrlKey);
        Preferences.Remove(MasterKeyKey);
        return Task.CompletedTask;
    }
}
