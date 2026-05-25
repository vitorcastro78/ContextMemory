using System.Text.Json;
using ContextMemory.Core.Configuration;
using Microsoft.Extensions.Options;

namespace ContextMemory.Core.CompanyBrain;

public sealed record SharePointOAuthPendingState
{
    public required string State { get; init; }
    public required string CompanyId { get; init; }
    public required string SourceId { get; init; }
    public DateTimeOffset CreatedAt { get; init; }
}

public sealed class SharePointOAuthStateStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false
    };

    private readonly string _pendingRoot;

    public SharePointOAuthStateStore(IOptions<ContextMemoryOptions> options)
    {
        var config = options.Value;
        _pendingRoot = Path.Combine(
            Path.GetFullPath(config.DataPath, config.ContentRootPath),
            "companies",
            "_oauth",
            "sharepoint-pending");
        Directory.CreateDirectory(_pendingRoot);
    }

    public void Save(SharePointOAuthPendingState state)
    {
        var path = Path.Combine(_pendingRoot, $"{state.State}.json");
        File.WriteAllText(path, JsonSerializer.Serialize(state, JsonOptions));
    }

    public bool TryTake(string state, out SharePointOAuthPendingState? pending)
    {
        pending = null;
        if (string.IsNullOrWhiteSpace(state))
            return false;

        var path = Path.Combine(_pendingRoot, $"{state}.json");
        if (!File.Exists(path))
            return false;

        try
        {
            pending = JsonSerializer.Deserialize<SharePointOAuthPendingState>(File.ReadAllText(path), JsonOptions);
            File.Delete(path);
            if (pending is null)
                return false;

            if (pending.CreatedAt < DateTimeOffset.UtcNow.AddHours(-1))
            {
                pending = null;
                return false;
            }

            return true;
        }
        catch
        {
            try { File.Delete(path); } catch { /* ignore */ }
            return false;
        }
    }
}
