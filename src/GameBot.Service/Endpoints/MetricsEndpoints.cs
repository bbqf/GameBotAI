using GameBot.Service.Hosted;
using GameBot.Domain.Actions;
using GameBot.Domain.Commands;
using GameBot.Domain.Triggers;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace GameBot.Service.Endpoints;

/// <summary>
/// Exposes operational metrics for trigger evaluation cycles.
/// </summary>
internal static class MetricsEndpoints {
  public static IEndpointRouteBuilder MapMetricsEndpoints(this IEndpointRouteBuilder app) {
    var group = app.MapGroup("/api/metrics");

    group.MapGet("/triggers", (ITriggerEvaluationMetrics metrics) => {
      // Snapshot current counters; simple anonymous object for JSON
      return Results.Ok(new {
        evaluations = metrics.Evaluations,
        skippedNoSessions = metrics.SkippedNoSessions,
        overlapSkipped = metrics.OverlapSkipped,
        lastCycleDurationMs = metrics.LastCycleDurationMs
      });
    })
    .WithName("GetTriggerEvaluationMetrics")
    ;

    group.MapGet("/domain", async (IActionRepository actions, ICommandRepository commands, ITriggerRepository triggers, CancellationToken ct) => {
      var actionList = await actions.ListAsync(null, ct).ConfigureAwait(false);
      var commandList = await commands.ListAsync(ct).ConfigureAwait(false);
      var triggerList = await triggers.ListAsync(ct).ConfigureAwait(false);
      return Results.Ok(new {
        actions = actionList.Count,
        commands = commandList.Count,
        triggers = triggerList.Count
      });
    })
    .WithName("GetDomainObjectCounts")
    ;

    group.MapGet("/process", (IConfiguration config) => {
      // Working set and managed memory snapshot in MB
      var wsBytes = System.Diagnostics.Process.GetCurrentProcess().WorkingSet64;
      var managedBytes = GC.GetTotalMemory(forceFullCollection: false);
      var wsMb = wsBytes / (1024.0 * 1024.0);
      var managedMb = managedBytes / (1024.0 * 1024.0);

      // Optional budget from config: Service:ResourceBudget:MaxWorkingSetMB (env Service__ResourceBudget__MaxWorkingSetMB)
      var budgetStr = config["Service:ResourceBudget:MaxWorkingSetMB"];
      double? budgetMb = null;
      if (double.TryParse(budgetStr, System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out var parsed)) {
        budgetMb = parsed;
      }

      return Results.Ok(new {
        workingSetMB = Math.Round(wsMb, 2),
        managedMemoryMB = Math.Round(managedMb, 2),
        budgetMB = budgetMb
      });
    })
    .WithName("GetProcessMetrics");

    return app;
  }
}
