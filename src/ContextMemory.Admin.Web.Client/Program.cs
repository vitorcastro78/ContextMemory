using ContextMemory.Admin.UI.Services;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.AspNetCore.Components.WebAssembly.Hosting;

var builder = WebAssemblyHostBuilder.CreateDefault(args);

builder.Services.AddScoped(_ =>
{
    var client = new HttpClient { Timeout = TimeSpan.FromMinutes(10) };
    return client;
});
builder.Services.AddScoped<IAdminSettingsStorage, BrowserAdminSettingsStorage>();
builder.Services.AddScoped<IChatTestSettingsStorage, BrowserChatTestSettingsStorage>();
builder.Services.AddScoped<AdminSession>();
builder.Services.AddScoped<AdminApiClient>();
builder.Services.AddScoped<ChatClient>();

await builder.Build().RunAsync();
