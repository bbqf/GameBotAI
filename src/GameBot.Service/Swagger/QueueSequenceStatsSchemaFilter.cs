using System.Collections.Generic;
using GameBot.Service.Contracts.Queues;
using Microsoft.OpenApi.Any;
using Microsoft.OpenApi.Models;
using Swashbuckle.AspNetCore.SwaggerGen;

namespace GameBot.Service.Swagger;

/// <summary>
/// Feature 105 (FR-013): describes <c>sequenceStats</c> on the queue read and each field of
/// <see cref="QueueSequenceStatsResponse"/>. The service does not give XML comments to Swagger, so the
/// descriptions come from this filter.
/// </summary>
internal sealed class QueueSequenceStatsSchemaFilter : ISchemaFilter {
  internal const string SequenceStatsDescription =
    "The run statistics of each sequence that the queue ran, keyed by sequence ID (keys in ordinal order). "
    + "Always present: {} when the queue has no recorded run. The queue records each sequence run that it "
    + "starts and that completes, for all schedule types, guard sequences too. Two template entries of the "
    + "same sequence share one entry. Not recorded: a run in progress, a run that a service stop interrupts, "
    + "an ad-hoc run, a dry-run and a nested run. The values stay after a queue restart and a service restart. "
    + "A sequence that is no longer in the queue keeps its entry. DELETE /api/queues/{id} deletes the "
    + "statistics; POST /api/queues/{id}/duplicate does not copy them. A damaged statistics file gives {} "
    + "and a warning in the service log.";

  private static readonly Dictionary<string, string> FieldDescriptions = new() {
    ["sequenceName"] = "The name from the sequence store. Null when the sequence no longer exists.",
    ["lastRunStartedAt"] = "The start of the last completed run (service-local time, with offset).",
    ["lastRunEndedAt"] = "The end of the last completed run (service-local time, with offset).",
    ["lastRunStatus"] = "The status of the last completed run. success: the run completed and did not fail "
      + "(a run that a Break step ends is a success). failure: the run ended with an error or a failed step. "
      + "cancelled: the queue stopped the run (a stop by hand, a failure-policy stop or the sequence time limit).",
    ["lastSuccessAt"] = "The end of the last successful run. Null when no run succeeded.",
    ["successCount"] = "The total number of successful runs since the first record.",
    ["failureCount"] = "The total number of failed runs since the first record.",
    ["cancelledCount"] = "The total number of cancelled runs since the first record."
  };

  public void Apply(OpenApiSchema schema, SchemaFilterContext context) {
    if (schema.Properties is null) return;

    if (context.Type == typeof(QueueSequenceStatsResponse)) {
      schema.Description = "The run statistics of one sequence in one queue.";
      foreach (var (name, description) in FieldDescriptions) {
        if (schema.Properties.TryGetValue(name, out var property)) property.Description = description;
      }

      if (schema.Properties.TryGetValue("lastRunStatus", out var status)) {
        status.Enum = new List<IOpenApiAny> {
          new OpenApiString("success"),
          new OpenApiString("failure"),
          new OpenApiString("cancelled")
        };
      }

      return;
    }

    if (context.Type == typeof(QueueDetailResponse) && schema.Properties.TryGetValue("sequenceStats", out var stats)) {
      stats.Description = SequenceStatsDescription;
      stats.Type = "object";
      stats.AdditionalPropertiesAllowed = true;
      stats.AdditionalProperties ??= context.SchemaGenerator.GenerateSchema(typeof(QueueSequenceStatsResponse), context.SchemaRepository);
    }
  }
}
