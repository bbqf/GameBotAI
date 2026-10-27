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
        "keyCode (integer Android key code) or key (string key text), one required; keyCode wins when both are given. "
        + "The key text is: " + KeyInputSchemaFilter.RuleSummary,
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
        + "offsetY (integers, default 0), selectionStrategy (HighestConfidence, the default, or FirstMatch), "
        + "region (object, optional: x, y, width, height in capture pixels, all four required, x and y 0 or more, "
        + "width and height more than 0; the search runs only inside the region; a bad region gives 400). "
        + "A primitiveTap payload detectionTarget takes the same region.",
      [ActionTypes.EnsureGameRunning] =
        "forceRestart, boolean, optional (default false). Without it, or with false, the step checks that the run's "
        + "session game is in the foreground and attempts a launch when it is not; the step fails unless the game ends "
        + "up running. With true, the step stops the game on the device of the session, starts it again, and waits for "
        + "the foreground; the step then has the outcome restarted. A value that is not true or false is rejected with 400.",
      [ActionTypes.GoToHomeScreen] =
        "no payload fields. Presses Android HOME on the run's session; the game keeps running in the background.",
      [ActionTypes.EnsureEmulatorRunning] =
        "adbSerial, string, required; instanceName (string) or instanceIndex (integer >= 0), one required, and "
        + "instanceName wins when both are given. Starts or restarts that LDPlayer instance when it is not running "
        + "and responsive.",
      [ActionTypes.RescheduleSelf] =
        "option, string, required: one of AtQueueStart, OncePerRun, Timer, EveryStep, Cancel (case-insensitive). With Timer, "
        + "exactly one of timerTimeOfDay (HH:mm or HH:mm:ss, 24-hour, service-local time of day, no other form) or timerRelativeOffset (HH:mm:ss, "
        + "between 00:00:00 and 24:00:00) is required, unless ocrOffset is given. A timerTimeOfDay that is not later "
        + "than the current time books that time on the next day. ocrOffset, object, Timer only: "
        + "region {x, y, width, height} (required, positive width and height) is OCR-read for a countdown that "
        + "becomes the offset (the text can have a day part \"<N>d\" before the time, for example \"1d 23:29:10\"; "
        + "then max must allow one day or more, for example \"2.00:00:00\"); fallback (HH:mm:ss, required, between 00:00:00 and 24:00:00) is used when the read "
        + "fails; min and max (HH:mm:ss, optional, defaults 00:00:01 and 24:00:00, min must be less than max) bound "
        + "the accepted reading. timerTimeOfDay, timerRelativeOffset and ocrOffset are rejected with any other option. "
        + "Schedules one more firing of this sequence into the queue run that started it; a no-op success when the "
        + "sequence was not started from a queue. keep, string, optional, Timer only: the one value is earliest "
        + "(case-insensitive). With keep earliest, a booking does not replace a pending booking of this sequence when that "
        + "pending booking was made in the same run and fires at the same time or earlier. The booking is dropped, and the step "
        + "still succeeds with the result scheduled. A pending booking from an earlier run or from the engine is always "
        + "replaced. keep is only valid when option is Timer. Without keep, the last booking wins. "
        + "The service rejects an unknown top-level field. "
        + "Cancel removes the pending Timer, OncePerRun and AtQueueStart bookings of this sequence in the current queue run, "
        + "also a OncePerRun booking that the queue already copied for firing but did not start. It keeps EveryStep "
        + "registrations, live schedules, queue template entries and the bookings of other sequences. A booking made after "
        + "Cancel stays (the last booking wins). Cancel never fails the run: the outcome is cancelled (removed true) when it "
        + "removed a booking, or noop (removed false) when nothing was pending, there was no queue, or the queue run was not "
        + "active. The removed field is in the execution log of the step.",
      [ActionTypes.Notify] =
        "message, string, required, at most 1000 characters; url, optional absolute http or https URL. Without url, "
        + "the message goes to the enabled notification targets (see GET /api/notifications/targets). With url, the "
        + "message goes only to that URL. The default URL (Service:Notifications:DefaultUrl) is used only when no "
        + "notification target is enabled. Raises an outbound alert and always succeeds. "
        + "dedupeKey, string, optional, at most 100 characters, needs since or within: the service sends the alert at most "
        + "once for each queue and key in the window. since, string HH:mm, optional: the window starts at the latest "
        + "local time of day (as in the lastRun condition). within, string HH:mm:ss, optional, from 00:00:01 to 24:00:00: "
        + "the window is the last such duration. Use since or within, not both. The step outcome is sent when the alert "
        + "was delivered and suppressed when the window already has an alert; a delivery that fails does not start a window. "
        + "A step without dedupeKey sends every time.",
      [ActionTypes.QuizLookup] =
        "game, category, store, strings, required, not blank. question, region {x, y, width, height} (integers in capture "
        + "pixels, width and height more than 0), required: where the question text is. answers, list of 2 or more regions, "
        + "required: where the answer texts are, in order. similarity, number from 0 to 1, optional (default 0.85): the "
        + "lowest text similarity for a match. The step reads one frame, looks the question up in the store of that game "
        + "and category (game exact, category and store without case) and never taps. The outcome is pick_N (N from 1: "
        + "the position of the answer, as a stored answer or as a random guess when the question is not known), "
        + "unreadable (a region has no readable text or the OCR engine failed; no pick is made), or service_unavailable "
        + "(the store cannot be used). A later commandOutcome condition can read the outcome, for example expectedState "
        + "pick_2. The step never fails the sequence. Example step: " + QuizLookupExample,
      [ActionTypes.QuizLearn] =
        "lookupStepId, string, required: the id of a quiz-lookup step that is before this step. Exactly one of "
        + "correctPosition (integer from 1: the position of the correct answer) or correctRegion (region {x, y, width, "
        + "height}: where the screen shows the correct answer text). The correct text must reach the similarity limit "
        + "of the lookup for exactly one answer. The outcome is stored, skipped_no_match (no lookup result, or no "
        + "matching answer), skipped_ambiguous (more than one matching answer), or service_unavailable (the store "
        + "cannot be used). A step with a wrong shape is rejected with 400. Example step: " + QuizLearnExample
    };

  /// <summary>
  /// The example <c>quiz-lookup</c> action of <c>contracts/quiz-steps.md</c>. The request names the fields <c>payload</c>.
  /// The stored step names them <c>parameters</c>.
  /// </summary>
  internal const string QuizLookupExample =
    "{\"type\":\"quiz-lookup\",\"payload\":{\"game\":\"pns\",\"category\":\"radio\",\"store\":\"pns-quiz\","
    + "\"question\":{\"x\":40,\"y\":200,\"width\":400,\"height\":90},"
    + "\"answers\":[{\"x\":40,\"y\":320,\"width\":400,\"height\":60},{\"x\":40,\"y\":400,\"width\":400,\"height\":60},"
    + "{\"x\":40,\"y\":480,\"width\":400,\"height\":60}],\"similarity\":0.85}}";

  /// <summary>The example <c>quiz-learn</c> action of <c>contracts/quiz-steps.md</c>.</summary>
  internal const string QuizLearnExample =
    "{\"type\":\"quiz-learn\",\"payload\":{\"lookupStepId\":\"lookup-1\",\"correctPosition\":2}}";

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
