using ContextMemory.Core.CompanyBrain;
using ContextMemory.Core.Contracts;

namespace ContextMemory.Api.Endpoints;

public static class MetricsEndpoint
{
    public static void MapMetricsEndpoint(this WebApplication app)
    {
        app.MapGet("/metrics", (ITelemetryCollector telemetry, CompanyBrainTelemetry companyBrainTelemetry) =>
        {
            var body = telemetry.ExportPrometheus() + companyBrainTelemetry.ExportPrometheus();
            return Results.Text(body, "text/plain; version=0.0.4; charset=utf-8");
        });
    }
}
