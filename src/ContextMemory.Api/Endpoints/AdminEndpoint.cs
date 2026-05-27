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
        app.MapGet("/admin/apps/{appId}/credentials", GetCredentials);
        app.MapPost("/admin/apps/{appId}/rotate-api-key", RotateApiKey).DisableAntiforgery();
    }

    private static IResult ListApps(IAppRegistry registry, ITelemetryCollector telemetry) =>
        Results.Json(registry.GetAllApps().Select(a => new
        {
            a.AppId,
            source = registry.GetAppSource(a.AppId),
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

    private static IResult GetCredentials(string appId, IAppRegistry registry)
    {
        if (!registry.TryGetCredentials(appId, out var credentials) || credentials is null)
            return Results.NotFound(new { error = "App not found." });

        return Results.Json(credentials);
    }

    private static IResult RotateApiKey(string appId, IAppRegistry registry)
    {
        if (!registry.TryRotateApiKey(appId, out var credentials) || credentials is null)
            return Results.NotFound(new { error = "App not found." });

        return Results.Json(credentials);
    }

    private static string GetDashboardHtml() => """
        <!DOCTYPE html>
        <html lang="pt">
        <head>
          <meta charset="utf-8" />
          <meta name="viewport" content="width=device-width, initial-scale=1" />
          <title>ContextMemory Admin</title>
          <script defer src="https://cdn.jsdelivr.net/npm/alpinejs@3.14.1/dist/cdn.min.js"></script>
          <style>
            :root { color-scheme: dark; }
            body { font-family: system-ui, sans-serif; margin: 0; background: #0f172a; color: #e2e8f0; }
            header { padding: 1.5rem 2rem; border-bottom: 1px solid #334155; display: flex; justify-content: space-between; align-items: center; }
            h1 { margin: 0; color: #38bdf8; font-size: 1.5rem; }
            main { padding: 2rem; max-width: 1100px; margin: 0 auto; }
            .card { background: #1e293b; border-radius: 8px; padding: 1.25rem; margin-bottom: 1rem; border: 1px solid #334155; }
            .metrics { display: grid; grid-template-columns: repeat(auto-fit, minmax(140px, 1fr)); gap: 0.75rem; margin: 0.75rem 0; }
            .metric { background: #0f172a; padding: 0.75rem; border-radius: 6px; }
            .metric span { display: block; font-size: 0.75rem; color: #94a3b8; }
            .metric strong { font-size: 1.25rem; }
            button { background: #2563eb; color: white; border: none; padding: 0.45rem 0.9rem; border-radius: 4px; cursor: pointer; margin-right: 0.5rem; font-size: 0.875rem; }
            button.secondary { background: #475569; }
            button.danger { background: #dc2626; }
            pre { background: #020617; padding: 1rem; overflow: auto; border-radius: 4px; font-size: 0.8rem; }
            .badge { display: inline-block; padding: 0.15rem 0.5rem; border-radius: 4px; font-size: 0.75rem; background: #334155; }
          </style>
        </head>
        <body x-data="adminApp()" x-init="init()">
          <header>
            <h1>ContextMemory Admin</h1>
            <button class="secondary" @click="load()">Atualizar</button>
          </header>
          <main>
            <p>Autenticação: <code>Authorization: Bearer MASTER_KEY</code></p>
            <template x-if="error"><p style="color:#f87171" x-text="error"></p></template>
            <template x-for="app in apps" :key="app.appId">
              <div class="card">
                <div style="display:flex;justify-content:space-between;align-items:center">
                  <div>
                    <strong x-text="app.appId"></strong>
                    <span class="badge" x-text="app.source"></span>
                  </div>
                </div>
                <div class="metrics">
                  <div class="metric"><span>Pedidos</span><strong x-text="app.stats.RequestsTotal ?? app.stats.requestsTotal ?? 0"></strong></div>
                  <div class="metric"><span>Utilizadores activos</span><strong x-text="app.stats.ActiveUsers ?? app.stats.activeUsers ?? 0"></strong></div>
                  <div class="metric"><span>Tokens prompt</span><strong x-text="app.stats.TokensPrompt ?? app.stats.tokensPrompt ?? 0"></strong></div>
                  <div class="metric"><span>Tokens resposta</span><strong x-text="app.stats.TokensCompletion ?? app.stats.tokensCompletion ?? 0"></strong></div>
                  <div class="metric"><span>RAG hits</span><strong x-text="app.stats.RagHits ?? app.stats.ragHits ?? 0"></strong></div>
                  <div class="metric"><span>Latência média (ms)</span><strong x-text="Math.round(app.stats.AvgLatencyMs ?? app.stats.avgLatencyMs ?? 0)"></strong></div>
                </div>
                <div>
                  <button class="secondary" @click="viewUsers(app.appId)">Utilizadores</button>
                  <button class="secondary" @click="viewAudit(app.appId)">Audit log</button>
                  <button class="danger" @click="deleteUser(app.appId)">Apagar memória (GDPR)</button>
                </div>
                <pre x-show="app.detail" x-text="JSON.stringify(app.detail, null, 2)"></pre>
              </div>
            </template>
          </main>
          <script>
            function adminApp() {
              return {
                apps: [],
                masterKey: '',
                error: '',
                async init() {
                  this.masterKey = sessionStorage.getItem('cm_master_key') || prompt('Master Key:', 'cm_master_dev_key_change_me') || '';
                  if (this.masterKey) sessionStorage.setItem('cm_master_key', this.masterKey);
                  await this.load();
                },
                headers() { return { Authorization: 'Bearer ' + this.masterKey }; },
                async load() {
                  this.error = '';
                  try {
                    const r = await fetch('/admin/apps', { headers: this.headers() });
                    if (!r.ok) { this.error = await r.text(); return; }
                    const data = await r.json();
                    this.apps = data.map(a => ({ ...a, detail: null }));
                  } catch (e) { this.error = e.message; }
                },
                async viewUsers(appId) {
                  const r = await fetch(`/admin/apps/${appId}/users`, { headers: this.headers() });
                  const app = this.apps.find(a => a.appId === appId);
                  if (app) app.detail = await r.json();
                },
                async viewAudit(appId) {
                  const r = await fetch(`/admin/apps/${appId}/audit`, { headers: this.headers() });
                  const app = this.apps.find(a => a.appId === appId);
                  if (app) app.detail = await r.json();
                },
                async deleteUser(appId) {
                  const userId = prompt('userId a apagar:');
                  if (!userId) return;
                  await fetch(`/admin/apps/${appId}/users/${userId}/memory`, { method: 'DELETE', headers: this.headers() });
                  alert('Memória apagada');
                }
              }
            }
          </script>
        </body>
        </html>
        """;
}
