using ContextMemory.Api.Endpoints;
using ContextMemory.Api.Extensions;
using ContextMemory.Api.Middleware;
using ContextMemory.Core.Configuration;
using System.Text.Json.Serialization;

var builder = WebApplication.CreateBuilder(args);

builder.Services.ConfigureHttpJsonOptions(options =>
{
    options.SerializerOptions.PropertyNamingPolicy = null;
    options.SerializerOptions.DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull;
});

builder.Services.Configure<ContextMemoryOptions>(options =>
{
    builder.Configuration.GetSection(ContextMemoryOptions.SectionName).Bind(options);
    options.ContentRootPath = builder.Environment.ContentRootPath;
});

builder.Services.Configure<ContextMemory.Embeddings.Configuration.EmbeddingsOptions>(options =>
{
    builder.Configuration.GetSection(ContextMemory.Embeddings.Configuration.EmbeddingsOptions.SectionName).Bind(options);
    options.ContentRootPath = builder.Environment.ContentRootPath;
});

builder.Services.AddContextMemory(builder.Configuration);

var app = builder.Build();

app.UseMiddleware<AuthMiddleware>();
app.UseMiddleware<RateLimitMiddleware>();
app.UseMiddleware<TelemetryMiddleware>();

app.MapHealthEndpoint();
app.MapMetricsEndpoint();
app.MapChatEndpoint();
app.MapFeedbackEndpoint();
app.MapAdminEndpoints();
app.MapAppsConfigEndpoints();
app.MapAppsRegisterEndpoint();
app.MapAppsWikiEndpoint();

app.Run();

public partial class Program;
