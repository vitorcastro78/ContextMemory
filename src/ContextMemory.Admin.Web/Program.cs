using ContextMemory.Admin.Web;
using ContextMemory.Admin.UI.Services;

var builder = WebApplication.CreateBuilder(args);
builder.AddServiceDefaults();

builder.Services.AddRazorComponents()
    .AddInteractiveWebAssemblyComponents();

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

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.UseWebAssemblyDebugging();
}
else
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
    app.UseHsts();
}

app.UseHttpsRedirection();
app.UseAntiforgery();

app.MapStaticAssets();
app.MapRazorComponents<App>()
    .AddInteractiveWebAssemblyRenderMode()
    .AddAdditionalAssemblies(typeof(ContextMemory.Admin.Web.Client.AssemblyMarker).Assembly)
    .AddAdditionalAssemblies(typeof(ContextMemory.Admin.UI.Pages.Dashboard).Assembly);
app.MapDefaultEndpoints();

app.Run();
