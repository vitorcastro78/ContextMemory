using ContextMemory.Core.Models;

namespace ContextMemory.Core.Contracts;

public interface IAppRegistry
{
    bool TryGetApp(string appId, out AppProfile? profile);
    bool ValidateApiKey(string appId, string apiKey);
    IReadOnlyCollection<AppProfile> GetAllApps();
}
