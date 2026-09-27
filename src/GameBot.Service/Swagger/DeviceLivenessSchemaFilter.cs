using GameBot.Domain.Sessions;
using GameBot.Service.Contracts.Queues;
using Microsoft.OpenApi.Any;
using Microsoft.OpenApi.Models;
using Swashbuckle.AspNetCore.SwaggerGen;

namespace GameBot.Service.Swagger;

/// <summary>
/// Feature 106 (FR-020, issue #220): descriptions and enum values of the device liveness fields. The
/// service does not give XML comments to Swagger, so this filter publishes the meaning of each field.
/// </summary>
internal sealed class DeviceLivenessSchemaFilter : ISchemaFilter {
  internal const string StateDescription =
    "The device liveness. 'live': the device answers and its frames are current. 'not_live': the device does not "
    + "answer, or it does not apply the inputs (see reason). 'unknown': no device (stub mode), or not sufficient data.";

  internal const string ReasonDescription =
    "Why the device is not live. Null when the state is not 'not_live'. 'capture_stalled': no capture completed for "
    + "longer than CaptureStallLimitMs. 'input_timeout': an input did not complete in InputTimeoutMs. "
    + "'no_change_after_input': the frame did not change for StaleLimitMs after an input. "
    + "'transport_not_ready': adb get-state did not report 'device'.";

  internal const string FrameAgeDescription =
    "Milliseconds since the last completed capture. 0 after a direct capture. Null when no capture data exists.";

  internal const string UnchangedDescription =
    "Milliseconds since the frame bytes last changed. Null when no capture-loop data exists.";

  internal const string StaleDescription =
    "True when a capture loop runs, and the frame did not change for longer than StaleLimitMs, or no capture "
    + "completed for longer than CaptureStallLimitMs. False when no capture loop runs. A stale frame alone does not "
    + "make the state 'not_live': a static screen can be live.";

  private const string LastInputAtDescription =
    "The start of the last input command that the service sent (inputs endpoint or sequence input). Null when no "
    + "input was sent.";

  private const string LastInputOutcomeDescription =
    "The outcome of the last input command. 'pending': it did not end yet. 'completed': the device accepted it. "
    + "'timed_out': the device did not answer in InputTimeoutMs. 'failed': the device refused it. 'cancelled': the "
    + "caller stopped it before the input time limit. Null when no input was sent.";

  public void Apply(OpenApiSchema schema, SchemaFilterContext context) {
    ArgumentNullException.ThrowIfNull(schema);
    ArgumentNullException.ThrowIfNull(context);
    if (schema.Properties is null) return;

    if (context.Type == typeof(SessionLivenessSchema)) {
      schema.Description = "The device liveness of the session (feature 106). The service calculates it at each call.";
      Describe(schema, "state", StateDescription, DeviceLivenessStates.Live, DeviceLivenessStates.NotLive, DeviceLivenessStates.Unknown);
      Describe(schema, "reason", ReasonDescription, [.. DeviceLivenessReasons.All]);
      Describe(schema, "frameAgeMs", FrameAgeDescription);
      Describe(schema, "unchangedMs", UnchangedDescription);
      Describe(schema, "stale", StaleDescription);
      Describe(schema, "lastInputAt", LastInputAtDescription);
      Describe(schema, "lastInputOutcome", LastInputOutcomeDescription, [.. InputOutcomes.WireValues]);
    }
    else if (context.Type == typeof(SessionHealthSchema)) {
      Describe(schema, "liveness", "The device liveness (feature 106). See SessionLivenessSchema.");
    }
    else if (context.Type == typeof(QueueDeviceLivenessResponse)) {
      schema.Description = QueueDeviceLivenessDescription;
      Describe(schema, "state", StateDescription, DeviceLivenessStates.Live, DeviceLivenessStates.NotLive, DeviceLivenessStates.Unknown);
      Describe(schema, "reason", ReasonDescription, [.. DeviceLivenessReasons.All]);
      Describe(schema, "notLiveSince",
        "Service-local time at which the queue first observed the device as not live in this fault episode. Null when "
        + "the state is not 'not_live'.");
      Describe(schema, "stale", StaleDescription);
      Describe(schema, "frameAgeMs", FrameAgeDescription);
      Describe(schema, "unchangedMs", UnchangedDescription);
      Describe(schema, "gatedFirings",
        "The number of held firings in the current fault episode. 0 when no episode is open. A firing is held only for "
        + "the reasons capture_stalled, input_timeout and transport_not_ready.");
    }
  }

  internal const string QueueDeviceLivenessDescription =
    "The device liveness of the running queue (feature 106). Null before the run binds a session. The read calculates "
    + "it from the current data. When the state is 'not_live' with the reason capture_stalled, input_timeout or "
    + "transport_not_ready, the queue holds each due firing: the firing stays due and runs when the device is live "
    + "again. The first held firing of each sequence in a fault episode writes one failed execution-log entry "
    + "('device_not_live: <reason>'). When the device stays not live for longer than QueueGracePeriodMs, the queue "
    + "records one failed cycle for the episode. The queue never stops or recovers the device by itself.";

  private static void Describe(OpenApiSchema schema, string property, string description, params string[] values) {
    if (!schema.Properties.TryGetValue(property, out var propertySchema)) return;
    propertySchema.Description = description;
    if (values.Length == 0) return;
    propertySchema.Enum = values.Select(v => (IOpenApiAny)new OpenApiString(v)).ToList();
  }
}
