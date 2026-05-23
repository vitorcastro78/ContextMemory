using ContextMemory.Core.Contracts;
using ContextMemory.Core.Models;

namespace ContextMemory.Api.Endpoints;

public static class AdminEndpoint
{
    public static void MapAdminEndpoints(this WebApplication app)
    {
        app.MapGet("/admin", () => Results.Content(GetDashboardHtml(), "text/html"));
        app.MapGet("/admin/apps", ListApps);
        app.MapGet("/admin/apps/{appId}/stats", GetStats);
        app.MapGet("/admin/apps/{appId}/users", ListUsers);
        app.MapGet("/admin/apps/{appId}/users/{userId}", GetUser);
        app.MapDelete("/admin/apps/{appId}/users/{userId}/memory", DeleteMemory);
        app.MapPatch("/admin/apps/{appId}/config", PatchConfig);
        app.MapGet("/admin/apps/{appId}/audit", GetAudit);
    }

    private static IResult ListApps(IAppRegistry registry, ITelemetryCollector telemetry) =>
        Results.Json(registry.GetAllApps().Select(a => new
        {
            a.AppId,
            stats = telemetry.GetAppSnapshot(a.AppId)
        }));

    private static async Task<IResult> GetStats(string appId, ITelemetryCollector telemetry, IFeedbackStore feedback)
    {
        var avg = await feedback.GetAverageScoreAsync(appId).ConfigureAwait(false);
        var snapshot = telemetry.GetAppSnapshot(appId);
        return Results.Json(new
        {
            appId,
            activeUsers = snapshot.ActiveUsers,
            telemetry = snapshot,
            feedbackAverage = avg
        });
    }

    private static async Task<IResult> ListUsers(string appId, IMemoryAdminService admin) =>
        Results.Json(await admin.ListUsersAsync(appId).ConfigureAwait(false));

    private static async Task<IResult> GetUser(string appId, string userId, IMemoryAdminService admin) =>
        Results.Json(await admin.GetUserDetailAsync(appId, userId).ConfigureAwait(false));

    private static async Task<IResult> DeleteMemory(
        string appId,
        string userId,
        IMemoryAdminService admin,
        CancellationToken cancellationToken)
    {
        await admin.DeleteUserMemoryAsync(appId, userId, cancellationToken).ConfigureAwait(false);
        return Results.Ok(new { status = "deleted", appId, userId });
    }

    private static async Task<IResult> PatchConfig(
        string appId,
        AppConfigPatchRequest patch,
        IAppConfigStore configStore,
        CancellationToken cancellationToken)
    {
        var updated = await configStore.UpdateAsync(appId, patch, cancellationToken).ConfigureAwait(false);
        return Results.Json(updated);
    }

    private static async Task<IResult> GetAudit(
        string appId,
        IAuditLog auditLog,
        CancellationToken cancellationToken)
    {
        var entries = await auditLog.GetByAppAsync(appId, 200, cancellationToken).ConfigureAwait(false);
        return Results.Json(entries);
    }

    private static string GetDashboardHtml() => """
        <!DOCTYPE html>
        <html lang="pt">
        <head>
          <meta charset="utf-8" />
          <title>ContextMemory Admin</title>
          <script defer src="https://cdn.jsdelivr.net/npm/alpinejs@3.x.x/dist/cdn.min.js"></script>
          <style>
            body { font-family: system-ui, sans-serif; margin: 2rem; background: #0f172a; color: #e2e8f0; }
            h1 { color: #38bdf8; }
            .card { background: #1e293b; border-radius: 8px; padding: 1rem; margin: 1rem 0; }
            button { background: #2563eb; color: white; border: none; padding: 0.5rem 1rem; border-radius: 4px; cursor: pointer; }
            button.danger { background: #dc2626; }
            pre { background: #020617; padding: 1rem; overflow: auto; border-radius: 4px; }
          </style>
        </head>
        <body x-data="adminApp()" x-init="load()">
          <h1>ContextMemory Admin</h1>
          <p>Dashboard de gestão — requer <code>Authorization: Bearer MASTER_KEY</code> nas chamadas API.</p>
          <div class="card">
            <h2>Aplicações</h2>
            <template x-for="app in apps" :key="app.appId">
              <div style="margin-bottom:1rem">
                <strong x-text="app.appId"></strong>
                <pre x-text="JSON.stringify(app.stats, null, 2)"></pre>
                <button @click="deleteUser(app.appId)">Apagar memória user demo</button>
              </div>
            </template>
          </div>
          <script>
            function adminApp() {
              return {
                apps: [],
                masterKey: prompt('Master Key:', 'cm_master_dev_key_change_me') || '',
                async load() {
                  const r = await fetch('/admin/apps', { headers: { Authorization: 'Bearer ' + this.masterKey } });
                  this.apps = await r.json();
                },
                async deleteUser(appId) {
                  const userId = prompt('userId a apagar:');
                  if (!userId) return;
                  await fetch(`/admin/apps/${appId}/users/${userId}/memory`, {
                    method: 'DELETE',
                    headers: { Authorization: 'Bearer ' + this.masterKey }
                  });
                  alert('Memória apagada');
                }
              }
            }
          </script>
        </body>
        </html>
        """;
}
