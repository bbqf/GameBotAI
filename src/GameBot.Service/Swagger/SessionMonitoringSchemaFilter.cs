using GameBot.Service.Endpoints.Dto;
using Microsoft.OpenApi.Any;
using Microsoft.OpenApi.Models;
using Swashbuckle.AspNetCore.SwaggerGen;

namespace GameBot.Service.Swagger;

/// <summary>
/// Feature 141 (FR-018): descriptions and enum values of the monitoring API. The service does not give XML
/// comments to Swagger, so this filter publishes the meaning of each field.
/// </summary>
internal sealed class SessionMonitoringSchemaFilter : ISchemaFilter {
  public void Apply(OpenApiSchema schema, SchemaFilterContext context) {
    ArgumentNullException.ThrowIfNull(schema);
    ArgumentNullException.ThrowIfNull(context);
    if (schema.Properties is null) return;

    if (context.Type == typeof(MonitoredSessionDto)) DescribeSession(schema);
    else if (context.Type == typeof(SessionMonitoringListResponse)) DescribeList(schema);
    else if (context.Type == typeof(SessionRestartRecordDto)) DescribeRestart(schema);
  }

  private static void DescribeList(OpenApiSchema schema) {
    schema.Description = "All sessions that the service holds, with a state. The list has no status filter. An empty list is a valid answer.";
    Describe(schema, "sessions", "One item for each session. The service does not probe the device to make this list.");
  }

  private static void DescribeSession(OpenApiSchema schema) {
    schema.Description = "One session in the monitoring list (feature 141).";
    Describe(schema, "id", "The session id.");
    Describe(schema, "name", "The game id and the device serial, for example 'pns (emulator-5558)'. The game id alone when the session has no serial.");
    Describe(schema, "gameId", "The game of the session.");
    Describe(schema, "deviceSerial", "The ADB serial of the emulator. Null when the session has no device.");
    Describe(schema, "state",
      "'live': the session works. 'no_answer': the device does not answer (see reason). 'restarting': an emulator restart runs. "
      + "'stopping': the session is on its way out. The first rule that matches wins, in the order restarting, no_answer, stopping, live.",
      MonitoredSessionStates.All);
    Describe(schema, "reason", "The device liveness reason when the state is 'no_answer'. Null in the other states.");
    Describe(schema, "ownerQueueId", "The id of the queue that uses this session. Null for an ad-hoc session.");
    Describe(schema, "ownerQueueName", "The name of the queue that uses this session. Null when no queue uses it.");
    Describe(schema, "currentSequenceName", "The name of the sequence that the owner queue runs now. Null when no queue uses the session or the queue runs no sequence.");
    Describe(schema, "restartAvailable", "True when the system can restart the emulator of this session.");
    Describe(schema, "restartUnavailableReason",
      "Why a restart is not available. Null when restartAvailable is true.", RestartUnavailableReasons.All);
    Describe(schema, "startedAtUtc", "The time at which the session started (UTC).");
  }

  private static void DescribeRestart(OpenApiSchema schema) {
    schema.Description = "The last emulator restart of a session (feature 141). The service keeps it in memory.";
    Describe(schema, "sessionId", "The session that was restarted.");
    Describe(schema, "state", "'restarting': the restart runs. 'success': the emulator answers and the session is connected. 'failure': the restart failed (see reason).",
      RestartStates.All);
    Describe(schema, "step", "The step that runs now, or the last step that ran.", RestartSteps.All);
    Describe(schema, "startedAtUtc", "The time at which the restart started (UTC).");
    Describe(schema, "endedAtUtc", "The time at which the restart ended (UTC). Null while the restart runs.");
    Describe(schema, "reason", "Why the restart failed. Null when the state is not 'failure'.", RestartReasons.All);
  }

  private static void Describe(OpenApiSchema schema, string property, string description, params string[] values) {
    if (!schema.Properties.TryGetValue(property, out var propertySchema)) return;
    propertySchema.Description = description;
    if (values.Length == 0) return;
    propertySchema.Enum = values.Select(v => (IOpenApiAny)new OpenApiString(v)).ToList();
  }
}
