using GameBot.Service.Hosted;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace GameBot.Service.Endpoints;

/// <summary>
/// Exposes operational metrics for trigger evaluation cycles.
/// </summary>
internal static class MetricsEndpoints
{
    public static IEndpointRouteBuilder MapMetricsEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/metrics");

        group.MapGet("/triggers", (ITriggerEvaluationMetrics metrics) =>
        {
            // Snapshot current counters; simple anonymous object for JSON
            return Results.Ok(new
            {
                evaluations = metrics.Evaluations,
                skippedNoSessions = metrics.SkippedNoSessions,
                overlapSkipped = metrics.OverlapSkipped,
                lastCycleDurationMs = metrics.LastCycleDurationMs
            });
        })
        .WithName("GetTriggerEvaluationMetrics")
        .WithOpenApi();

        return app;
    }
}