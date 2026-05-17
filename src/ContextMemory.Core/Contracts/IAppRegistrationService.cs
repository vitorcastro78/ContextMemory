using ContextMemory.Core.Models;

namespace ContextMemory.Core.Contracts;

public interface IAppRegistrationService
{
    Task<RegisterAppResponse> RegisterAsync(
        RegisterAppRequest request,
        CancellationToken cancellationToken = default);
}
