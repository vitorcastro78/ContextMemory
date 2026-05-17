using ContextMemory.Core.Models;

namespace ContextMemory.Core.Contracts;

public interface IAppConfigStore
{
    string ProfilesRoot { get; }
    AppRuntimeConfig GetConfig(string appId);
    Task<AppRuntimeConfig> ReloadAsync(string appId, CancellationToken cancellationToken = default);
    Task<AppRuntimeConfig> UpdateAsync(
        string appId,
        AppConfigPatchRequest patch,
        CancellationToken cancellationToken = default);
    void EnsureProfileExists(string appId, AppRuntimeConfig? seed = null);
}
