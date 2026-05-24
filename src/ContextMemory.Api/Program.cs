using ContextMemory.Api.Endpoints;
using ContextMemory.Api.Extensions;
using ContextMemory.Api.Middleware;
using ContextMemory.Core.Configuration;
using ContextMemory.Core.Persistence;
using ContextMemory.Core.Persistence.Postgres;
using Microsoft.EntityFrameworkCore;
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
builder.Services.AddContextMemorySwagger();

var app = builder.Build();

if (PersistenceProviders.IsPostgres(builder.Configuration.GetSection(ContextMemoryOptions.SectionName)["PersistenceProvider"]))
{
    await using var scope = app.Services.CreateAsyncScope();
    var dbFactory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<ContextMemoryDbContext>>();
    await using var db = await dbFactory.CreateDbContextAsync();
    await db.Database.MigrateAsync();
}

app.UseContextMemorySwagger();

app.UseMiddleware<AuthMiddleware>();
app.UseMiddleware<RateLimitMiddleware>();
app.UseMiddleware<TelemetryMiddleware>();

app.MapHealthEndpoint();
app.MapMetricsEndpoint();
app.MapChatEndpoint();
app.MapGenerateEndpoint();
app.MapFeedbackEndpoint();
app.MapAppsEndpoint();
app.MapAdminEndpoints();
app.MapAppsConfigEndpoints();
app.MapAppsRegisterEndpoint();
app.MapAppsWikiEndpoint();

app.Run();

public partial class Program;
