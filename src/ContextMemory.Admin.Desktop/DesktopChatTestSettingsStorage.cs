using ContextMemory.Admin.UI.Models;
using ContextMemory.Admin.UI.Services;

namespace ContextMemory.Admin.Desktop;

public sealed class DesktopChatTestSettingsStorage : IChatTestSettingsStorage
{
    private const string AppIdKey = "contextmemory.chat.appId";
    private const string UserIdKey = "contextmemory.chat.userId";
    private const string ApiKeyKey = "contextmemory.chat.apiKey";
    private const string ModelKey = "contextmemory.chat.model";

    public Task<ChatTestSettings?> LoadAsync(CancellationToken cancellationToken = default)
    {
        var settings = new ChatTestSettings
        {
            AppId = Preferences.Get(AppIdKey, "kyc-dev"),
            UserId = Preferences.Get(UserIdKey, "admin-chat-test"),
            ApiKey = Preferences.Get(ApiKeyKey, string.Empty),
            Model = Preferences.Get(ModelKey, "qwen3.5:4b")
        };
        return Task.FromResult<ChatTestSettings?>(settings);
    }

    public Task SaveAsync(ChatTestSettings settings, CancellationToken cancellationToken = default)
    {
        Preferences.Set(AppIdKey, settings.AppId ?? string.Empty);
        Preferences.Set(UserIdKey, settings.UserId ?? string.Empty);
        Preferences.Set(ApiKeyKey, settings.ApiKey ?? string.Empty);
        Preferences.Set(ModelKey, settings.Model ?? string.Empty);
        return Task.CompletedTask;
    }
}
