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
    + $"{SequenceTimeLimits.DefaultWatchdogTimeoutMs} ms). With the status 'failure' it means the limit ended a run "
    + "of a sequence with onTimeLimit 'fail'. With the status 'success' it means the limit ended a run of a sequence "
    + "with onTimeLimit 'end' (a planned stop). Null otherwise — including ordinary failures, successes, "
    + "user stops and ad-hoc runs.";

  private const string TimeLimitDescription =
    "The time bound in milliseconds that applied to the firing; set exactly when cancellationReason is set.";

  // Feature 122: the description of the sequence option that hides success notifications.
  internal const string ExcludeFromSuccessNotificationsDescription =
    "If true, queues send no success message for this sequence. Failure, cancelled and recovered messages "
    + "are still sent. If absent on PUT or PATCH, the stored value does not change.";

  // Feature 170: the description of the sequence option that ends a run at its time limit.
  internal const string OnTimeLimitDescription =
    "What a queue run does when it reaches its time limit. Allowed values: 'fail' "
    + "(default) gives the log status failure. "
    + "'end' gives the log status success; the entry keeps cancellationReason "
    + "'sequence_time_limit' and timeLimitMs. A response always holds the value. "
    + "If absent on POST, the value is 'fail'. If absent on PUT, the value resets to 'fail'. If absent on PATCH, "
    + "the stored value does not change. An unknown value gives 400.";

  public void Apply(OpenApiSchema schema, SchemaFilterContext context) {
    if (context.Type == typeof(SequenceUpsertContract) || context.Type == typeof(SequencePatchContract)) {
      Describe(schema, "watchdogTimeoutMs", WatchdogDescription);
      Describe(schema, "excludeFromSuccessNotifications", ExcludeFromSuccessNotificationsDescription);
      Describe(schema, "onTimeLimit", OnTimeLimitDescription);
    }
    else if (context.Type == typeof(SequenceRequestSchema)
      || context.Type == typeof(SequenceResponseSchema)
      || context.Type == typeof(SequenceListItemResponse)) {
      Describe(schema, "excludeFromSuccessNotifications", ExcludeFromSuccessNotificationsDescription);
      Describe(schema, "onTimeLimit", OnTimeLimitDescription);
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
