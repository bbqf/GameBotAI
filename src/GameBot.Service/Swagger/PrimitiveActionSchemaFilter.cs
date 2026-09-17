using System.Linq;
using GameBot.Domain.Actions;
using GameBot.Service.Models;
using Microsoft.OpenApi.Any;
using Microsoft.OpenApi.Models;
using Swashbuckle.AspNetCore.SwaggerGen;

namespace GameBot.Service.Swagger;

/// <summary>
/// Feature 102 (issue #201): publishes the action types a step's <c>primitiveAction</c> accepts, taken from
/// <see cref="SequenceActionTypes.All"/> — the list the step validator uses — and what each type reads from
/// <c>payload</c>. Without it <c>type</c> was a free string, and <c>reschedule-self</c> could only be found by probing
/// validator errors. The service does not feed XML comments to Swagger.
/// </summary>
internal sealed class PrimitiveActionSchemaFilter : ISchemaFilter {
  internal const string SchemaDescription =
    "A step's action: type picks the action and payload carries that type's fields.";

  internal const string TypeDescription =
    "The action to run, one of the listed values, matched case-insensitively (WaitForImage is the one PascalCase "
    + "value). POST /api/sessions/start shares this schema but accepts only connect-to-game. A sequence step with "
    + "any other value is rejected with 400, and the error lists the supported values.";

  /// <summary>
  /// What each action type reads from <c>payload</c>, keyed by the <see cref="SequenceActionTypes.All"/> values. The
  /// field names are the keys the dispatcher and validators read (research R-004). A type missing here is simply left
  /// out of the published text, which the contract tests report.
  /// </summary>
  internal static readonly IReadOnlyDictionary<string, string> PayloadDescriptions =
    new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) {
      [ActionTypes.Tap] = "x and y, integers, required: the point to tap in device pixels.",
      [ActionTypes.Swipe] =
        "x1, y1 (start) and x2, y2 (end), integers, required, in device pixels; durationMs, integer, optional.",
      [ActionTypes.Key] =
        "keyCode (integer Android key code) or key (string key name), one required; keyCode wins when both are given.",
      [ActionTypes.Command] =
        "commandId, string, required: the id of an existing command to run. The step-level commandReference does not "
        + "replace it.",
      [ActionTypes.ConnectToGame] =
        "gameId and adbSerial, strings, required: starts a session for that game on that device and brings the game "
        + "to the foreground. instanceName (string) or instanceIndex (integer >= 0), optional: ensure that LDPlayer "
        + "instance is running first.",
      [ActionTypes.WaitForImage] =
        "timeoutMs, integer >= 0, optional (default 1000). detectionTarget, object, optional: referenceImageId "
        + "(string, must not be empty when detectionTarget is given), confidence (number, default 0.8), offsetX and "
        + "offsetY (integers, default 0), selectionStrategy (HighestConfidence, the default, or FirstMatch).",
      [ActionTypes.EnsureGameRunning] =
        "no payload fields. Checks that the run's session game is in the foreground and attempts a launch when it is "
        + "not; the step fails unless the game ends up running.",
      [ActionTypes.GoToHomeScreen] =
        "no payload fields. Presses Android HOME on the run's session; the game keeps running in the background.",
      [ActionTypes.EnsureEmulatorRunning] =
        "adbSerial, string, required; instanceName (string) or instanceIndex (integer >= 0), one required, and "
        + "instanceName wins when both are given. Starts or restarts that LDPlayer instance when it is not running "
        + "and responsive.",
      [ActionTypes.RescheduleSelf] =
        "option, string, required: one of AtQueueStart, OncePerRun, Timer, EveryStep (case-insensitive). With Timer, "
        + "exactly one of timerTimeOfDay (HH:mm:ss, service-local time of day) or timerRelativeOffset (HH:mm:ss, "
        + "between 00:00:00 and 24:00:00) is required, unless ocrOffset is given. ocrOffset, object, Timer only: "
        + "region {x, y, width, height} (required, positive width and height) is OCR-read for a countdown that "
        + "becomes the offset; fallback (HH:mm:ss, required, between 00:00:00 and 24:00:00) is used when the read "
        + "fails; min and max (HH:mm:ss, optional, defaults 00:00:01 and 24:00:00, min must be less than max) bound "
        + "the accepted reading. timerTimeOfDay, timerRelativeOffset and ocrOffset are rejected with any other option. "
        + "Schedules one more firing of this sequence into the queue run that started it; a no-op success when the "
        + "sequence was not started from a queue.",
      [ActionTypes.Notify] =
        "message, string, required, at most 1000 characters; url, optional absolute http or https URL that overrides "
        + "the service's default destination. Raises an outbound alert and always succeeds."
    };

  /// <summary>The published <c>payload</c> description: an intro, then one <c>- type: fields</c> line per type.</summary>
  internal static readonly string PayloadDescription = BuildPayloadDescription();

  private static string BuildPayloadDescription() {
    var lines = SequenceActionTypes.All
      .Select(type => PayloadDescriptions.TryGetValue(type, out var text) ? $"- {type}: {text}" : null)
      .Where(line => line is not null);
    return "Type-specific fields; which ones apply depends on type. Integer fields also accept numeric strings.\n\n"
      + string.Join("\n", lines);
  }

  public void Apply(OpenApiSchema schema, SchemaFilterContext context) {
    if (context.Type != typeof(PrimitiveActionRequest) || schema.Properties is null) {
      return;
    }

    schema.Description = SchemaDescription;
    if (schema.Properties.TryGetValue("type", out var type)) {
      type.Enum = SequenceActionTypes.All.Select(value => (IOpenApiAny)new OpenApiString(value)).ToList();
      type.Description = TypeDescription;
    }
    if (schema.Properties.TryGetValue("payload", out var payload)) {
      payload.Description = PayloadDescription;
    }
  }
}
