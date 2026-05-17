using ContextMemory.Api.Middleware;
using ContextMemory.Core.Contracts;

namespace ContextMemory.Api.Endpoints;

public static class AppsWikiEndpoint
{
    public static void MapAppsWikiEndpoint(this WebApplication app)
    {
        app.MapPost("/apps/{appId}/wiki", UploadWikiAsync)
            .DisableAntiforgery();
    }

    private static async Task<IResult> UploadWikiAsync(
        string appId,
        HttpContext httpContext,
        IFormFileCollection files,
        IAppRegistry appRegistry,
        IWikiIndexService wikiIndex,
        CancellationToken cancellationToken)
    {
        var headerAppId = httpContext.Items[AuthMiddleware.AppIdItemKey] as string;
        if (!string.Equals(headerAppId, appId, StringComparison.Ordinal))
            return Results.Forbid();

        if (!appRegistry.TryGetApp(appId, out var app) || app is null)
            return Results.NotFound(new { error = "App not found." });

        if (files.Count == 0)
            return Results.BadRequest(new { error = "No files uploaded." });

        var saved = new List<string>();

        foreach (var file in files)
        {
            if (!file.FileName.EndsWith(".md", StringComparison.OrdinalIgnoreCase))
                continue;

            var safeName = Path.GetFileName(file.FileName);
            var targetDir = app.WikiPath;
            Directory.CreateDirectory(targetDir);
            var targetPath = Path.Combine(targetDir, safeName);

            await using var stream = File.Create(targetPath);
            await file.CopyToAsync(stream, cancellationToken).ConfigureAwait(false);

            await wikiIndex
                .ReindexFileAsync(appId, app.WikiPath, safeName, cancellationToken)
                .ConfigureAwait(false);

            saved.Add(safeName);
        }

        if (saved.Count == 0)
            return Results.BadRequest(new { error = "No valid .md files uploaded." });

        return Results.Ok(new
        {
            appId,
            filesIndexed = saved,
            count = saved.Count
        });
    }
}
