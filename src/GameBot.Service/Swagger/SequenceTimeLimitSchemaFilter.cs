using GameBot.Domain.Commands;
using GameBot.Domain.Logging;
using GameBot.Service.Models;
using Microsoft.OpenApi.Models;
using Swashbuckle.AspNetCore.SwaggerGen;

namespace GameBot.Service.Swagger;

/// <summary>
/// Feature 094 (FR-009): describes the per-sequence time bound and the execution-log fields that report
/// its expiry. The service does not feed XML comments to Swagger, so without this the bound's default and
/// meaning would again be something an author learns only by watching runs get cut off.
/// </summary>
internal sealed class SequenceTimeLimitSchemaFilter : ISchemaFilter {
  private static readonly string WatchdogDescription =
    $"Time bound, in milliseconds, for one queue firing of this sequence. Absent/null means the platform default of "
    + $"{SequenceTimeLimits.DefaultWatchdogTimeoutMs} ms (4 minutes); maximum {SequenceTimeLimits.MaxWatchdogTimeoutMs} ms "
    + $"(30 minutes). A firing that exceeds it is cancelled, the queue run continues, and the sequence's execution-log "
    + $"entry records cancellationReason '{ExecutionCancellationReasons.SequenceTimeLimit}'. Read the value that "
    + "applies from effectiveWatchdogTimeoutMs on GET /api/sequences/{sequenceId}.";

  /// <summary>Shared with the subtree operation description, whose node schema cannot be published.</summary>
  internal static readonly string CancellationReasonDescription =
    $"Why the platform ended this execution early. '{ExecutionCancellationReasons.SequenceTimeLimit}': a queue firing's "
    + "sequence ran past its time bound (watchdogTimeoutMs, default "
    + $"{SequenceTimeLimits.DefaultWatchdogTimeoutMs} ms). Null otherwise — including ordinary failures, successes, "
    + "user stops and ad-hoc runs. finalStatus stays 'failure' for a time-limit cancellation.";

  private const string TimeLimitDescription =
    "The time bound in milliseconds that applied to the firing; set exactly when cancellationReason is set.";

  public void Apply(OpenApiSchema schema, SchemaFilterContext context) {
    if (context.Type == typeof(SequenceUpsertContract) || context.Type == typeof(SequencePatchContract)) {
      Describe(schema, "watchdogTimeoutMs", WatchdogDescription);
    }
    else if (context.Type == typeof(ExecutionLogEntryDto)) {
      Describe(schema, "cancellationReason", CancellationReasonDescription);
      Describe(schema, "timeLimitMs", TimeLimitDescription);
    }
  }

  private static void Describe(OpenApiSchema schema, string property, string description) {
    if (schema.Properties is not null && schema.Properties.TryGetValue(property, out var propertySchema)) {
      propertySchema.Description = description;
    }
  }
}
