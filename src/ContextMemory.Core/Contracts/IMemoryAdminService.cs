namespace ContextMemory.Core.Contracts;

public interface IMemoryAdminService
{
    Task DeleteUserMemoryAsync(string appId, string userId, CancellationToken cancellationToken = default);
    Task<UserAdminDetail> GetUserDetailAsync(string appId, string userId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<UserAdminSummary>> ListUsersAsync(string appId, CancellationToken cancellationToken = default);
}

public sealed class UserAdminSummary
{
    public required string UserId { get; init; }
    public int FactCount { get; init; }
    public bool HasSessionContext { get; init; }
}

public sealed class UserAdminDetail
{
    public required string UserId { get; init; }
    public required object Profile { get; init; }
    public int ConversationMessageCount { get; init; }
    public bool HasSemanticMemory { get; init; }
}
