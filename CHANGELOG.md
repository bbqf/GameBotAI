# Changelog

All notable changes to this project will be documented in this file.

## [Unreleased]

### Added
- Option `Cancel` for the `reschedule-self` step. It removes the pending Timer, OncePerRun and AtQueueStart bookings of its sequence in the current queue run, also a OncePerRun booking that the queue already copied for firing (123-reschedule-self-cancel, issue #264, FR-015)
  - The step gives outcome `cancelled` with `removed` true, or `noop` with `removed` false. It never fails the run. `Cancel` with `timerTimeOfDay`, `timerRelativeOffset` or `ocrOffset` returns 400. The Web UI lists "Cancel pending booking".
- Queue sequence notifications. A queue can send a short message when one of its entries ends (120-sequence-queue-notifications)
  - Each queue has a `notificationLevel`: `none` (default), `failure` or `successAndFailure`. Set it with `PUT /api/queues/{id}/notification-level` or on the new Notifications page of the Web UI. `POST` and `PUT /api/queues` accept an optional `notificationLevel`. Both queue responses show it. Every other queue change keeps the level. A duplicate gets `none`.
  - The message is plain text: `<queue> : <sequence> : <circle> <status>`. The circle is green for `success` and `recovered`, red for `failure` and yellow for `cancelled`. A manual run and a nested step send no message.
  - A failure streak sends one `failure` message, then no more, until the next success. That success sends one `recovered` message. A level change to `none` and a queue delete close the open streaks with no message. A stop of the service sends no message. A time-limit stop is a `failure`. A stop of the queue while the service runs is `cancelled`.
  - Telegram is the first target type. Targets are in `data/notifications/targets.json`. Routes: `GET` and `POST /api/notifications/targets`, `PUT` and `DELETE /api/notifications/targets/{id}`, `POST /api/notifications/targets/{id}/test` and `GET /api/notifications/types`. The bot token is never in a response, a log line or an error text. The API shows `hasSecret` and `secretHint`. A `PUT` with an empty token keeps the stored token.
  - The send never blocks a queue run. One worker reads one channel and sends to all targets in parallel with a 30 s limit. Above 256 jobs that wait, the new job is dropped and one log line is written. A hand edit of `targets.json` applies with no restart. The backup archive does not hold the file.
  - **Compatibility**: a queue file with no level reads as `none`. A new target type needs one `INotificationChannel` class and one DI registration.

### Fixed
- A `reschedule-self` payload with an unknown top-level field is now rejected on save with a 400 that names each unknown field (119-reject-unknown-reschedule-field, #228)
  - Known fields are `option`, `timerTimeOfDay`, `timerRelativeOffset` and `ocrOffset`. Field names match without regard to letter case. Before, a mistyped field (for example `nextDay`) was ignored and the step did the wrong thing without an error.
  - **Compatibility**: a stored sequence with an unknown field still loads and runs. The next PUT or PATCH of that sequence gets a 400 until the author removes the unknown field.
- `timerTimeOfDay` has one format in the queue-template endpoint and in the sequence validator (118-unify-timer-time-format, #226)
  - Both accept `HH:mm` and `HH:mm:ss` (24-hour) and reject all other strings. Before, the template endpoint accepted only `HH:mm`. The sequence validator accepted `HH:mm:ss` and also forms such as `11:00 PM` and `9:30`.
  - Both error messages name the accepted format: `HH:mm or HH:mm:ss (24-hour)`. The OpenAPI text and the doc comments state the same rule.
  - A template response writes `HH:mm` when the seconds are zero. It writes `HH:mm:ss` when they are not zero.
  - **Compatibility**: a stored `HH:mm` value keeps working. A sequence with a not-strict `timerTimeOfDay` (for example `9:30`) is now rejected on save. `timerRelativeOffset`, the other schedule fields and the run behavior do not change.
- A `commandOutcome` condition that names a Break in an If branch that did not run (or in a loop body that ran zero iterations) now evaluates as `no_break`. Before, it failed with `condition-evaluation-error` (117-break-outcome-untaken-branch, #250)
  - A Break that ran keeps the outcome that it recorded last. A reference to a step that is not a Break and that did not run, and an unknown reference, still fail as before. The save rules, the API shapes and the execution log do not change.
- A `{{name}}` placeholder in a step `parameterBindings` value now resolves against the sequence scope at run time. Before, the command received the placeholder as literal text (115-binding-placeholder-scope, #246)
  - The placeholder resolves against the scope outside the binding. This applies to a sequence step and to a nested `Command` step. A value that is one whole placeholder keeps the origin layer of its value, for example `entry`.
  - A value with text around placeholders (for example `nova-{{option}}`) gets the layer `command`. The `parameters` log item also shows each placeholder name with its value and origin layer.
  - When no scope supplies the name, a sequence step fails and the command does not run. The message names the field `parameterBindings.<bindingName>` and tells how to supply the value. A nested command step gets `skipped_parameter_unresolved`, and the calling command continues with its next step.
  - **Compatibility**: a literal binding value, a `null` binding value and a step with no bindings keep their behavior. `${name}` stays literal text. The stored JSON, the API shapes and the save rules do not change.
- `POST /api/sequences` rejects a malformed step, and a create dry run never stores a sequence (113-reject-malformed-sequence-step, #242)
  - A step without `stepType` and without `primitiveAction` (for example, a step with only `commandReference`) now gets 400. The error names the step: `steps[0] (stepId 'a'): each action step must include primitiveAction object.` Before, the service returned 201 and stored the sequence with zero steps and no parameters. It also dropped the valid steps of the same request.
  - When one or more items of `steps` is an object, the service reads each step as a step object. Before, the service looked only at the first step. `PUT` and `PATCH /api/sequences/{id}` use the same rule, so a malformed step gets the same 400 and the stored sequence does not change.
  - Each shape error of a step starts with `steps[<index>]`, and with `(stepId '<id>')` when the step has a string `stepId`.
  - `dryRun: true` on `POST /api/sequences` now applies to each body shape. A create dry run never stores a sequence.
  - The old create shape (`steps` as a list of command id strings) rejects an item that is not a string, and a `parameters` value that is not `null`, with 400.
  - **Compatibility**: a valid per-step body, an old body with string ids, and a `blocks` body keep their results. Only bodies that the service stored with data loss now get 400. The Swagger description of `POST /api/sequences` tells the rules.
- A "not executed" tap outcome is true, and a single step call writes an execution-log entry (112-truthful-tap-outcome-log, #222)
  - A `PrimitiveTap` step reports `skipped_detection_failed`, `skipped_invalid_config` or `cancelled` with `accepted: 0` only when the service sent no input to the device. Before, an error or a cancellation after the dispatch (for example, when the service read back the executed point) gave `accepted: 0` with a "not executed" status, although the tap went to the device.
  - After a completed dispatch, the outcome is now `executed` with the reason `executed_then_error` or `executed_then_cancelled`, and `accepted` is the count that the session returned. The outcome has the resolved point, and the executed point when the service can read it.
  - When the session fails or is cancelled during the dispatch, the new status is `dispatch_unknown` with the reason `dispatch_error` or `dispatch_cancelled`, `accepted: 0` and the resolved point. The device can have the input.
  - `POST /api/steps/execute` now writes one execution-log entry for each call that passes the session check: execution type `step`, object type `step`, object id = the session id. The entry has the step type, the status, the reason, the points, the `accepted` count, the start time and the duration. A timeout, a cancellation and an error also write an entry. Read the entries with `GET /api/execution-logs?objectType=step&objectId=<sessionId>` and the time filter.
  - **Compatibility**: the response shape of `POST /api/steps/execute` does not change. The timeout response does not change. The Swagger description of the route states the contract.

### Added
- A parameter can choose the reference image (114-parametrized-reference-image, #243)
  - A command step accepts two new `fieldTemplates` keys: `primitiveTap.detectionTarget.referenceImageId` and `waitForImage.detectionTarget.referenceImageId`. The value must be one whole placeholder, for example `{{novaOption}}`. At run time the resolved value replaces the inline `referenceImageId`, and the inline value is not resolved. The save gives the warning `static_check_skipped` for each image key.
  - A value of an image key that is not one whole placeholder (for example `nova-{{option}}` or `option-a`) gets `400 invalid_field_template_value`. To build an id from text and a parameter, use the inline form `"referenceImageId": "nova-{{option}}"`.
  - An `imageVisible.imageId` in a sequence can hold a placeholder in each condition position: `condition`, `if.condition`, `loop.condition` (while and repeat-until), `breakCondition`, and each child of `all`, `any` and `none`. The save skips the image existence check for it and gives `static_check_skipped`. The run resolves the id against the scope of the step before each evaluation. The log text of the condition shows the resolved id.
  - A name with no value fails the step, and no input goes to the device. A step guard gives the message `Step '<key>': parameter '<name>' used by field 'condition.imageId' could not be resolved from any scope.` A while, repeat-until or `If` condition keeps its current message prefix. A break condition gives "No break" with the error in the log (feature 066). An id with no image gives the current missing-image result of each step type.
  - `POST`, `PUT` and `PATCH /api/sequences` with a per-step body now return a `warnings` member when the parameter check gives warnings. The member is not present when there are no warnings. A dry run does not change.
  - `POST /api/queue-templates` checks each known value (entry value or declared default) that goes to an image field of the sequence or of a reachable command. When no image has that id, the save gets `400 unknown_image_reference`, for example `Entry 0: parameter 'novaOption' gives the image id 'no-such-image' to field 'condition.imageId', but no image has that id.`, and stores nothing. A non-null `parameterBindings` entry for the name on the call path covers it, so the save does not check it. A binding to `{{otherName}}` is not followed.
  - **Compatibility**: the stored JSON does not change. The numeric `fieldTemplates` keys and the inline placeholders keep their behavior. The message of `unknown_field_template_path` is now "Step N: '<path>' is not a parametrizable field." A template save that names a missing image through an image field now gets 400; before, it failed only at run time. The OpenAPI document and `specs/openapi.json` describe the new rules.
- Press and hold at a detected point (111-anchored-long-press, #235)
  - A `PrimitiveTap` command step has a new optional field, `primitiveTap.holdMs` (integer, 0 to 5000). The service keeps it on `POST /api/commands`, `PATCH /api/commands/{id}` and `POST /api/steps/execute`, and returns it on read. Before, the service dropped the field. A value outside 0 to 5000 gets a 400 with "primitiveTap.holdMs must be between 0 and 5000". `holdMs` does not take a parameter placeholder.
  - With `holdMs` more than 0, the step finds its image as before and then presses and holds at the detected point (plus the offsets) for `holdMs`: one ADB swipe with the same start and end point and a duration of `holdMs`. The tap-point jitter applies as for a tap.
  - The step outcome of the execute responses has `holdMs` for a press and hold. The execution-log `tap` detail tells "Press and hold at (x,y) for N ms." (or "Press and hold targeted (x,y), executed at (x2,y2) for N ms.") and has the attribute `holdMs`.
  - The OpenAPI document shows `holdMs` on `PrimitiveTapConfigDto` (minimum 0, maximum 5000) and on `StepExecutionOutcomeDto`. The tap editor of the web UI has a "Hold duration (ms)" input.
  - **Compatibility**: a step without `holdMs`, or with `holdMs: 0`, sends the same single tap as before, and its stored JSON, response and log do not change.
- The API shows a wedged emulator (106-wedged-device-liveness, #220)
  - `GET /api/sessions/{id}/health` has a new `liveness` block: `state` (`live`, `not_live`, `unknown`), `reason` (`capture_stalled`, `input_timeout`, `no_change_after_input`, `transport_not_ready`), `frameAgeMs`, `unchangedMs`, `stale`, `lastInputAt` and `lastInputOutcome`. The call has a time limit: `adb get-state` has `TransportCheckTimeoutMs`, and one direct capture, when necessary, has `CaptureTimeoutMs`. The `adb` block does not change.
  - `GET /api/emulator/screenshot` and `GET /api/sessions/{id}/snapshot` have three new headers: `X-Capture-Age-Ms`, `X-Capture-Unchanged-Ms` and `X-Capture-Stale`. The snapshot has them only when a capture loop runs, or ran, for the session. CORS exposes them.
  - `GET /api/queues/{id}` has a new `health.deviceLiveness` block: `state`, `reason`, `notLiveSince`, `stale`, `frameAgeMs`, `unchangedMs` and `gatedFirings`.
  - A queue holds each due firing when its device is `not_live` with the reason `capture_stalled`, `input_timeout` or `transport_not_ready`. A held firing does not run and does not fail: it stays due, keeps its daily retry attempt and its self-reschedule due time, and runs when the device is live again. The first held firing of each sequence in a fault episode writes one failed execution-log entry (`device_not_live: <reason>`) and one `failure` run in `sequenceStats`. When the device stays not live for longer than `QueueGracePeriodMs`, the queue writes one failed `queue` entry and records one failed cycle, so `consecutiveFailedCycles` and the failure policy act (also for a queue that does not cycle). The queue never stops or recovers the device by itself. Known limit: a time-of-day firing held past midnight is lost for that day.
  - A new configuration section, `Service:DeviceLiveness`: `StaleLimitMs` (300000), `CaptureStallLimitMs` (60000), `InputTimeoutMs` (10000), `CaptureTimeoutMs` (10000), `TransportCheckTimeoutMs` (5000), `QueueGracePeriodMs` (120000) and `QueueCheckIntervalMs` (30000). A value below its minimum is set to the minimum.
- Run statistics of each sequence in a queue, and the `lastRun` step condition (105-sequence-run-statistics, #224)
  - The queue records each sequence run that it starts: start time, end time and status (`success`, `failure` or `cancelled`). A run that a Break step ends is a success. A stop by hand, a failure-policy stop and the sequence time limit are `cancelled`. A run that a service stop interrupts is not recorded. The values stay after a queue restart and a service restart, in `<data>/queue-sequence-stats/<queueId>.json`.
  - `GET /api/queues/{id}` (and the other queue detail responses) has a new field, `sequenceStats`, keyed by sequence ID: `sequenceName`, `lastRunStartedAt`, `lastRunEndedAt`, `lastRunStatus`, `lastSuccessAt`, `successCount`, `failureCount` and `cancelledCount`. It is `{}` when the queue has no recorded run. `DELETE /api/queues/{id}` deletes the statistics; a duplicate does not copy them.
  - A new step condition, `{ "type": "lastRun", "sequence": "self", "status": "success", "since": "11:00" }`, is true when the named sequence has a completed run with that status in the current queue since the last 11:00 service-local time. Use `within` (`hh:mm:ss` or `d.hh:mm:ss`; `24:00:00` is 24 hours) in place of `since` for a window of a fixed length. It is permitted in every condition slot and in `all`/`any`/`none`. In an ad-hoc run or a dry-run it is false, and the run does not fail. A daily task can now skip its work when it already succeeded today, also after a queue restart.
  - A bad `lastRun` condition gets a 400 that names the field. The OpenAPI document describes the condition (`LastRunCondition`) and `sequenceStats`.
  - **Web UI limit**: the web sequence editor does not know the `lastRun` type. Write `lastRun` conditions through the API. If you save such a sequence in the web UI, the condition can be lost.
- Condition-model ceiling retested, gaps closed and outcome documented (103-condition-model-retest-docs, #193)
  - A `commandOutcome` condition's `stepRef` is now resolved and ordered wherever it sits, not only where it was written directly on a step guard. Reached through an `all`/`any`/`none` composite it was previously checked for non-emptiness alone, so a reference to a nonexistent or later step saved with 201 and then failed the run with "reference is not available"; it is now a 400 at save time, naming the offending child by its `$`-rooted path. The same rules now also apply to a reference written directly in an `If` condition, a slot that validated a condition's shape and never checked a reference at all.
  - A loop's exit reason is now recorded in the persisted execution log: the loop step's entry carries `brokeVia` and `exhaustedMaxIterations` beside the existing `iterations`. It was already on the `POST /api/sequences/{id}/execute` response, but a queue-driven firing has no caller to answer, so its log is the only record it leaves — and it dropped the exit reason entirely.
  - The `If` condition slot's rejection message named `success|failed|skipped` while accepting all five outcome states, telling an author that `break` and `no_break` were invalid in a slot that has accepted them since feature 081. It now names all five.
  - The web sequence editor no longer rejects what the service accepts: it resolves a `stepRef` against the whole step tree (it previously searched top-level steps only and reported "references unknown prior step" for a legal reference into a `Loop` body or `If` branch) and accepts `break`/`no_break`. It also now validates conditions on nested steps, which it skipped entirely.
  - The OpenAPI document now states the reference resolution scope, the authored-order rule, all five `expectedState` values, the loop `exitReason` shape, and the one documented exception: a bare leaf reference in a `while`/`repeatUntil` condition is deliberately not checked (feature 088 decision D-006), while a composite-wrapped one there is.
  - **Compatibility**: a stored sequence whose composite-nested or `If`-condition reference is dangling or forward-pointing will be rejected on its next save. Such a sequence already fails at run time when that condition is reached; this turns a run-time failure into a save-time error. No new condition variant or outcome state was added, no field renamed, and the run response's `exitReason` keeps its shape.
  - No change to how conditions are evaluated against the screen, and a bare leaf reference in a `while`/`repeatUntil` condition is still accepted as before.
- Primitive action types published in the OpenAPI document (102-primitive-action-types-docs, #201)
  - `PrimitiveAction.type` is now an enum of all eleven accepted values: `tap`, `swipe`, `key`, `command`, `connect-to-game`, `WaitForImage`, `ensure-game-running`, `go-to-home-screen`, `ensure-emulator-running`, `reschedule-self`, `notify`. It is described as case-insensitive, with a note that `POST /api/sessions/start` accepts only `connect-to-game`. The enum comes from the same domain list the step validator uses, so the two cannot drift. `reschedule-self` was previously only discoverable by probing validator errors.
  - `PrimitiveAction.payload` now describes each type's fields, including `reschedule-self`'s `option` values, the Timer rule (exactly one of `timerTimeOfDay` or `timerRelativeOffset`, or an `ocrOffset`), its ranges, and that it has effect only inside a queue run. The sequence create/update/get examples include a `reschedule-self` Timer step.
  - An unsupported or missing action type is still rejected with 400, and the message now ends with `(expected one of ...)`, listing the supported values; the leading text is unchanged.
  - No action type's acceptance, payload rules or runtime behaviour changed.
- Detection coordinate units documented in the OpenAPI document (101-detect-coordinate-units-docs, #188)
  - `POST /api/images/detect` match `x`/`y`/`width`/`height` and `bbox.*` are now described as fractions 0..1 of the capture frame's width/height (clamped, not pixels), and `bbox` as repeating the top-level box. `POST /api/images/detect-all` match `x`/`y`/`width`/`height` are described as pixels. Both operation descriptions state the difference and warn against comparing values from the two routes directly.
  - `templateId` (detect) and `imageId` (detect-all) are described as the same reference image identifier. The detect example now shows the top-level coordinates it really returns.
  - Documentation only: response values, field names and status codes are unchanged.
- Queue roster documented in the OpenAPI document (100-queue-roster-openapi-docs, #179)
  - `QueueDetailResponse.entries` now says it is the queue's current roster in run order, always an array and never null, and the queue's own entries rather than its linked template's. It also says that `GET /api/queues/{id}` is how the roster is read (there is no `GET /api/queues/{id}/entries`). It is no longer published as nullable.
  - The `QueueEntryResponse` fields are described: `entryId` for `DELETE /api/queues/{id}/entries/{entryId}`, and `sequenceName` null / `stale` true when the sequence no longer exists. The `POST`/`PUT /api/queues/{id}/entries` operations now point to the read path.
  - `GET /api/queues/{id}/monitor` no longer carries the single-queue summary, description and example it wrongly shared before.
  - Documentation only: routes, response bodies and status codes are unchanged.
- Sequence step nesting rules in the OpenAPI document (099-sequence-nesting-rules-docs, #178)
  - The `SequenceStep` schema and its `body`/`elseBody` properties now state the nesting rules the service enforces: a Loop body may hold Action, If and Break steps but not another Loop, an If branch may hold only Action steps plus Break when the If sits in a Loop body (an If inside an If branch is rejected), and a top-level Break is rejected. These were previously learned only from a 400 on save.
  - Documentation only: validation, its error messages and execution are unchanged.
- Resume queues after a service restart (098-resume-queues-on-restart, #203)
  - New per-queue option `resumeOnServiceStart` ("Resume after service restart" in the queue form), off by default. A queue that has it on and was Running when the service stopped is started again automatically once the service is back up. That covers a graceful restart, an upgrade, a host reboot and a crash.
  - Only queues that were really running come back. An operator stop, a run that completed or failed on its own, and a failure-policy stop all keep the queue Stopped, and queues without the option stay Stopped as before.
  - The resumed run is an ordinary fresh start from the linked template: At Queue Start and Timer entries behave as on any start, and the previous run's self-reschedules, live schedules and daily retries are not restored. One attempt per queue per service start, with each outcome in the service log (events 7300–7304).
  - The service keeps the ids of running queues in `<data>/queue-run-state.json`, written when a run starts and cleared when it ends outside a shutdown.
  - Perf note: one small file write per queue start and per run end, and one read at service start.
- Alternate reference images (097-multi-reference-image-target, #192)
  - A stored reference image can now carry up to 8 alternates: other stored images that also count as a match for it, such as night-lit crops of the same daylight art. Manage them with GET/PUT /api/images/{id}/alternates. PUT replaces the whole list and [] clears it. GET /api/images/{id}/metadata lists them too.
  - Every detection that names the image also scores its alternates, with the same threshold and without any change to the sequence. That covers sequence image conditions, wait-for-image steps, image-anchored taps, image-match triggers, the readiness gate and POST /api/images/detect. One image can therefore cover both day and night renderings, replacing hand-OR'd Break steps.
  - POST /api/images/detect matches gain an additive matchedReferenceId; 	emplateId keeps the requested id. With several references, matches are merged by score, with ties going to the image itself and then to alternates in order, and de-duplicated by overlap. Runtime detections log the alternate that matched.
  - Deleting an alternate never breaks detection: it is skipped with a warning and reported as exists: false. Deleting an image removes its own alternates list. Backups include alternate images and their lists, and restore re-applies them.
  - Unchanged: images without alternates (same scores, never routed through the new matcher), POST /api/images/detect-all, and thresholds.
  - Perf note: an image without alternates costs one extra sidecar-file existence check per detection. With N alternates, detection takes about N+1 single matches: measured 43.8 ms for one reference and 173.2 ms for four on a 1280×720 frame.
- "Before Each Run" queue schedule type (095-before-each-run-schedule, #202)
  - New queue-template schedule type `BeforeEachRun`, shown as "Before Each Run". Its entries run immediately before every timed, live-scheduled or self-rescheduled firing: time-of-day timers and their daily retries, relative timers, live schedules, and self-reschedule Timer / At Queue Start / Once Per Run firings. Each task that wakes a queue therefore starts from a known state, even if something changed during an idle pause.
  - Several firings due at one wake-up share a single pass, which runs before the first of them. Entries run in template order. Once-per-run steps, at-queue-start entries and after-every-step executions never trigger the pass.
  - Accounting matches After Every Step: these runs do not count as executed, a failure is counted and is non-fatal, and the task still runs. The entries appear in the execution log and are listed once in the queue monitor as "Before Each Run".
  - The template API accepts and returns `BeforeEachRun`, case-insensitively. The template editor has a new "Before each run" area.
  - Perf note: a template without Before Each Run entries adds only an empty-list check per firing. Otherwise the cost is the configured sequences' own run time, once per wake-up.
- Sequence time-limit cancellation reported distinctly (094-sequence-time-limit-reporting, #182)
  - A queue firing cut off by its time bound still reads `finalStatus: "failure"`, but its sequence's execution-log entry now also carries `cancellationReason: "sequence_time_limit"` and `timeLimitMs`. The fields appear in the list, detail and subtree responses. A timeout can now be told apart from a sequence that reached its own failure, and that includes a step that swallowed the cancellation. User stops, ordinary failures, successes, ad-hoc runs and older entries carry neither field.
  - `GET /api/sequences/{id}` adds read-only `effectiveWatchdogTimeoutMs`: the sequence's `watchdogTimeoutMs` when set, otherwise the 240000 ms default. It is ignored on writes.
  - The OpenAPI document now describes `watchdogTimeoutMs` (default 240000 ms, minimum 1, maximum 1800000 ms), the effective read-out and the new execution-log fields.
- Duplicate queues (083-duplicate-queues)
  - A new "Duplicate" action on the Queues page creates a copy of an existing queue: cycle-execution/idle-pause settings, linked template, linked game, and the queue's currently loaded entries are all copied over. The only required input is a new name, which must differ from the source queue's current name; the confirm button and the API both reject a resubmitted unchanged name.
  - The dialog also pre-fills the source queue's emulator (serial, instance name, instance index) but — unlike the name — lets it be changed or left as-is, so a duplicate can target a different emulator, which a normal edit does not allow.
  - The duplicate links to the *same* template as the source (no template copy) and is always created stopped with no execution history, regardless of whether the source was running at the time. The source queue itself is never modified.
  - API: `POST /api/queues/{id}/duplicate` with `{ "name": string, "emulatorSerial": string, "emulatorInstanceName"?: string, "emulatorInstanceIndex"?: number }`, returning `201 Created` with the new queue (same shape as `POST /api/queues`).
- Tap-point jitter (058-tap-point-jitter)
  - Every tap and swipe sent to the device now lands within a small random offset of its target instead of always hitting the exact same pixel, making repeated executions look more natural. Each axis offset is drawn independently from `[-radius, +radius]`; swipe start and end points are jittered independently; results are clamped so coordinates are never negative.
  - Applied automatically and centrally in the session input dispatch path, so it covers all origins: authored steps, image-detection-resolved primitive taps, recorder replays, and the raw `POST /sessions/{id}/inputs` API. No per-step opt-in/opt-out and no new authoring/execution UI controls.
  - New configuration parameter `GAMEBOT_TAP_JITTER_RADIUS_PX` (default `5`): `0` disables jitter entirely (pixel-exact input); negative/invalid values fall back to the default. Follows the standard precedence (default → saved config file → environment variable), appears in the generic UI Configuration variables list, and is documented in `ENVIRONMENT.md`.
  - Execution logs and step outcomes now report both the pre-jitter target and the post-jitter executed coordinates: tap details read "Tap targeted (X,Y), executed at (X',Y')" and step outcome payloads gain additive `executedPoint`, `targetSwipe`, and `executedSwipe` fields alongside the existing `resolvedPoint`.

### Changed
- Device errors of the session endpoints (106-wedged-device-liveness, #220)
  - `POST /api/sessions/{id}/inputs` can now return `504 device_timeout`: one action did not complete in `InputTimeoutMs`, and the service did not send the actions after it. The `results` before it keep their `dispatched` value.
  - `POST /api/sessions/{id}/inputs` can now return `503 device_not_live`, with `error.reason`: the service sent the inputs, but the device is not live, so each result is `dispatched: false`. A request with no dispatched action still gets `400 invalid_input_actions`.
  - `GET /api/emulator/screenshot` and `GET /api/sessions/{id}/snapshot` can now return `504 capture_timeout` when a direct capture does not complete in `CaptureTimeoutMs`.
  - The service now kills an `adb` process when its call is cancelled or times out. Before, a hung `adb shell input` or `screencap` process stayed alive. Each capture of the background capture loop now has the limit `CaptureTimeoutMs`, so one hung capture does not stop the loop.
- Break step success/failure execution statuses (066-break-step-status)
  - A break that **does not fire** (its condition evaluated false) now shows a distinct, neutral **"No break"** state in the execution logs instead of the old `Skipped` label — clearly signalling that the loop simply continued, and never the alarming red "Failed".
  - A break whose condition **cannot be evaluated** (a runtime error) is now treated exactly like a false condition — a non-influential "No break" — so execution continues and the run no longer fails. This reverses the previous behavior where a break-condition error aborted the run. The same guarantee applies to a loop-level `breakOn` condition on a while block, whose evaluation errors are now guarded.
  - A break that **fires** is reported as a success (fixing a latent miscolor where a fired break could fall through to the red "failure" styling). A non-firing break never marks the enclosing loop, sequence, or run as failed and is excluded from failure counts. No change to break authoring, break firing behavior, or the persisted log format — only the reported *outcome* of a break changes.

### Fixed
- A `condition` on a `Loop` step is a guard that the runtime obeys (110-loop-step-condition, #232)
  - `POST /api/sequences` (also with `dryRun: true`) accepted a `condition` on a top-level `Loop` step, but the service did not keep it, and the runner did not evaluate it. The loop ran when its condition was false, and the execution log showed no condition result for the loop.
  - Now the save endpoints keep the `condition` of a `Loop` step and return it on read. The runner evaluates it one time, before the first iteration, with the same rules as the guard of an `Action` step. When it is false, the loop body does not run, the loop entry has status `Skipped` with no iterations, and a later `commandOutcome` reads the loop as `skipped`. When the evaluation fails, the step fails and the sequence stops.
  - The loop entry of the run result and of the execution log has `conditionType` and `conditionResult` (`true`, `false` or `error`) when the loop has a guard.
  - Save-time validation checks the guard of a `Loop` step with the rules for the guard of an `Action` step. An incorrect guard gets a 400 that names the step. A `commandOutcome` reference to a body step of the same loop is not a prior step.
  - The OpenAPI description of the sequence step tells the guard rule.
  - **Web UI limit**: the web sequence editor does not show the guard of a `Loop` step. Write it through the API. If you save such a sequence in the web UI, the guard is removed.
- A `reschedule-self` time of day that has passed books the next day (109-reschedule-timeofday-nextday, #227)
  - A `reschedule-self` step with `option: Timer` and a `timerTimeOfDay` that had passed today booked the sequence for the current moment. The queue then ran the sequence again immediately, and again after each run, until the operator stopped the queue.
  - Now a time of day that is not later than the local time now books that time on the next local day, as the template `Timer` entry does. The offset is the offset of the local time zone for that day. A time of day that is still ahead today books today, as before.
  - `GET /api/queues/{id}/monitor` shows the `SelfReschedule` item at the next-day instant. `timerRelativeOffset`, `ocrOffset`, the other options and the template `Timer` entry do not change.
- `POST /api/images/detect` does not report a time-limited measurement as an absence (108-detect-timeout-alternates, #223)
  - When the time limit of a call (`Service:Detections:TimeoutMs`, 500 ms by default) expired, the call returned a `200` with an empty `matches` array and `limitsHit: true`. A caller read this as a real absence. An image with alternates met the limit in some calls only, because the image and all its alternates had one shared limit.
  - Now each reference (the image and each alternate that loaded) gets the full `TimeoutMs`. An image without alternates keeps the same limit.
  - When the limit expires, the call fails with `504` and `{ "code": "detection_timeout", "message": "..." }`. A `200` with an empty `matches` array is now always a real absence, and `limitsHit` is true only when `maxResults` cut the list of matches. Sequence conditions, image-anchored taps and `POST /api/images/detect-all` do not use this limit and do not change.
- The execution log names each command step by its own step ID (107-command-step-log-attribution, #221)
  - When two or more steps of a sequence ran the same command, each command node in the execution log had the `stepId` of the first step that uses that command. This occurred in `message`, `stepId`, `stepLabel` and `deepLink.stepId`. A step that did not run (for example, the body of an `If` whose condition was false) thus appeared to run.
  - Now each command node has the `stepId` of the step that ran it. This applies at the top level and in `If` bodies, `If` else branches and `Loop` bodies. `If` and `Loop` nodes do not change. The API shape and the message format do not change.
- A queue idle for over 30 minutes no longer fails its next scheduled run (104-queue-session-idle-eviction, #217)
  - A running queue holds one emulator session, and the service retired any session idle for longer than `Service:Sessions:IdleTimeoutSeconds` (1800 s by default), including that one. A queue waiting between self-booked firings touched nothing for the whole gap, so its own session was evicted, and at the next wake-up the run failed with `emulator connection lost mid-run ('<serial>')`. The queue stopped, lost its pending bookings, and its `BeforeEachRun` entries never ran. ADB still listed the device the whole time. `pauseWhenIdle` made no difference.
  - Sessions bound by a queue are now exempt from the idle sweep and end only when the queue's run ends. Ad-hoc sessions are still evicted exactly as before.
  - Defense in depth: if a queue finds its session missing before a firing, it binds a new session on the same serial once, moves background capture to it, logs a `SessionRebound` warning (event 1131), and carries on, so the `BeforeEachRun` entries and the due firing run. It fails with the unchanged `emulator connection lost` message only when the device cannot be bound again (not listed by ADB, no devices, or session capacity reached).
  - `GET /api/emulator/screenshot?serial=` for a serial with no bound session still returns 404 `session_not_found`, but the message now says to start a session (`POST /api/sessions/start`) or a queue on that device, so it no longer reads as "no such device".
  - Perf note: the idle sweep does one extra null check per session, and the pre-firing check is unchanged unless a session is actually missing.
- Queue health now reports an Idle Pause (096-idle-pause-health-paused, #199)
  - While a running queue was in an idle pause, `GET /api/queues/{id}` returned `health.paused: false` with a null `pausedAt` and `pauseReason`, even as `/monitor` reported `IdlePause` for the same queue. `health.paused` now covers both kinds of pause. During an idle pause, `pausedAt` is when the hold began, and `pauseReason` reads `idle pause: resumes at HH:mm`.
  - Added `health.pauseKind` (`idle` | `failurePolicy` | null), so a routine idle pause can be told apart from a failure-policy park that needs `POST /api/queues/{id}/resume`. If both were in force at once, the failure-policy pause is reported. Failure-policy pause values are unchanged, and `/resume` still releases only a failure-policy pause.
  - The OpenAPI document now describes `paused`, `pausedAt`, `pauseReason` and `pauseKind`.
  - Perf note: a health read takes at most two extra uncontended locks. The idle-pause poll loop is unchanged.

## [0.7.0] - 2026-06-02

### Added
- Queue execution runtime (051-queue-execution-runtime)
  - Starting a queue now runs it for real: the linked template is loaded server-side, a session is opened to the queue's bound emulator, and the template's sequences run in order — replacing the previous placeholder that only flipped status.
  - "Cycle execution" gains its runtime meaning: when enabled, the run repeats from the first sequence (reusing the template snapshot loaded at start) until stopped or a run-level failure; an empty template completes without busy-looping.
  - Stopping a queue aborts the run promptly, disconnects the emulator session, and records a "stopped manually" entry; the session is disconnected on every end path.
  - Every run writes exactly one terminating queue-run execution-log entry with its stop reason (completed full run / stopped manually / failure). Individual sequence failures are non-fatal — recorded per sequence while the run continues — whereas no resolvable linked template, an unreachable emulator at start, or a lost connection mid-run end the run as a failure.
  - Concurrency: one run per queue (a second start returns 409 `already_running`); queues bound to the same emulator may run concurrently (operator's responsibility).
  - Execution log: a queue run appears as a single top-level entry (`executionType: "queue"`) with its sequences and their steps nested beneath it, reusing the existing hierarchy/grid; the grid shows a "Queue" row type.
  - API: `POST /api/queues/{id}/start` launches the run (200 Running, 404, or 409 `already_running`); `POST /api/queues/{id}/stop` aborts and is a no-op when not running.
  - Internal: extracted the sequence-execution wiring into a reusable `ISequenceExecutionService` shared by the standalone execute endpoint and the queue engine.

### Changed
- Execution logs now reflect what was actually executed (049-execution-logs-hierarchy)
  - The execution-logs list shows only top-level executed entities — stand-alone commands and sequences. Commands invoked as part of a sequence run are no longer separate top-level rows; they are recorded as linked children (kept, not deleted) and nested under their sequence.
  - Top-level rows are now expandable: expanding a sequence reveals its ordered sub-elements (steps, invoked commands, primitive taps, conditions, loops, wait-for-image outcomes) with the same detail available before, and command sub-elements drill into their own primitive outcomes.
  - A running sequence appears as a single in-progress entry (new `running` status) created at start and finalized in place when it completes; the page polls (~2s) while any entry is running so sub-elements update live without a manual reload.
  - API: `GET /api/execution-logs` returns roots-only and each item gains `childCount`; `finalStatus` now includes `running`. New `GET /api/execution-logs/{id}/subtree` returns the nested execution tree.
  - Performance: roots-only listing and subtree fetch are in-memory filters over the existing log store (no extra I/O on the hot path); live updates use a bounded ~2s poll gated on the presence of an in-progress entry (no polling when idle).
  - Historical logs recorded before this change remain viewable as completed leaf rows (no migration required).

### Fixed
- Sequence step command names are now preserved across save/reopen cycles (`001-fix-sequence-step-names`)
  - Reopening a saved sequence restores each step's selected command and user-visible label without reverting to "Select command".
  - Execution-log detail entries now identify both the step label and the command name that ran, making logs operator-readable.
  - When a referenced command is deleted after a sequence was saved, the affected step shows the last-saved command name as unresolved instead of appearing blank; other steps in the same sequence are unaffected.

### Added
- Wait For Image primitive action (001-wait-for-image)
  - Added `WaitForImage` as an authorable step in both command and sequence flows.
  - Added optional `detectionTarget` and `timeoutMs` payload support with default timeout normalization.
  - Added command and sequence runtime behavior that treats `image_detected`, `timeout_elapsed`, and `image_unavailable` as terminal wait outcomes.
  - Extended execution-log detail payloads and UI rendering to show wait parameters and terminal wait exit conditions.
  - Added backend unit/integration/contract coverage and web-ui authoring/service/log rendering coverage for wait-step scenarios.

- Tap Wait-and-Retry Before Execution (036-tap-wait-retry)
  - Primitive tap steps now wait for the target image to appear before tapping, retrying up to a configurable number of times.
  - New environment variables for configuration:
    - `GAMEBOT_TAP_RETRY_COUNT` — maximum retry cycles (default 3; 0 = single check, no retries).
    - `GAMEBOT_TAP_RETRY_PROGRESSION` — multiplier applied to the wait time after each retry (default 1.0 = constant; >1 = exponential backoff).
    - `GAMEBOT_CAPTURE_INTERVAL_MS` — now also used as the base wait time between retry cycles (default 500ms, min 50ms).
  - Retry metadata encoded in `PrimitiveTapStepOutcome.Reason` field (e.g., `detected_after_2_retries`, `detection_failed_after_3_retries`, `cancelled_during_retry_N`).
  - Immediate cancellation support: cancelling during any wait period stops the retry loop within 1ms.
  - Source-generated `[LoggerMessage]` log entries for retry cycle tracing (EventIds 6009–6013).

- Background Screenshot Service (034-background-screenshot-service)
  - Per-session background capture loop runs on a dedicated thread, taking ADB screenshots at a configurable interval (`GAMEBOT_CAPTURE_INTERVAL_MS`, default 500ms, min 50ms).
  - Cached frames available instantly via `BackgroundScreenCaptureService.GetCachedFrame()` — dual-format cache (PNG bytes + Bitmap) so consumers choose their preferred format.
  - All `IScreenSource` consumers now read from the background cache instead of making direct ADB calls.
  - Screenshot endpoint (`GET /api/emulator/screenshot`) serves cached frames with fallback to direct ADB capture during startup.
  - Optional `sessionId` query parameter on screenshot endpoint for explicit session targeting.
  - Capture rate metric (FPS) displayed in the Execution tab for each running session.
  - Automatic lifecycle management: capture loop starts with session, stops when session ends or is evicted.
  - Rolling FPS calculation using a circular buffer of the last 10 capture durations.

### Changed
- Image match evaluation now uses OpenCV `Cv2.MatchTemplate` (CCoeffNormed) via `ITemplateMatcher` instead of brute-force pure-C# NCC. Reduces per-evaluation time from ~5 seconds to ~50 ms for full-screen regions.
- MVC controllers now serialize enums as strings (via `JsonStringEnumConverter`) for consistency with minimal API endpoints.

### Fixed
- Execution tab session status no longer shows "Unknown" due to integer enum serialization mismatch.
- Capture rate display moved to its own labeled line; only shown when data is available.
- Web-ui test warning noise reduced by stabilizing async Configuration test flows and Jest interop settings.

### Security
- Upgraded `jest-environment-jsdom` from 29.7.0 to 30.3.0 to resolve `@tootallnate/once` vulnerability (GHSA-vpq2-c234-7xj6).
- Upgraded `System.Drawing.Common` from 9.0.0 to 10.0.3 across all projects.

- Command Loop Steps (033-command-loops)
  - Count-based loops (`count`): execute body steps exactly N times.
  - While loops (`while`): re-evaluate condition before each iteration; skip body if false on entry.
  - Repeat-until loops (`repeatUntil`): execute body, then check exit condition; always runs at least once.
  - Break step (`Break`): unconditional or conditional early exit from a loop body.
  - `{{iteration}}` placeholder substitution in `CommandId` and action parameters inside loop bodies.
  - Configurable safety limit (`maxIterations`) per loop step and global `GAMEBOT_LOOP_MAX_ITERATIONS` (default 1000).
  - Per-iteration execution log entries (`LoopIterationOutcome`) with break detection.
  - Validation rules: no nested loops, no top-level breaks, no `{{iteration}}` outside loops, commandOutcome back-ref only.
  - Extended `/api/sequences/{id}/validate` endpoint with step-level loop validation.
  - UI visualization: `LoopBlockHeader`, `BreakStepRow`, `LoopBlock` components with "Add Loop" (count/while/repeatUntil) affordance in SequencesPage.

### Tests
- 15 loop validation unit tests, 15 runner loop tests, 9 template substitutor tests, 6 contract round-trip tests, 14 UI component tests.

## [2026-03-02]

### Added
- Execution Logs top-level navigation tab between `Execution` and `Configuration`.
- Execution Logs list page with sortable columns (`timestamp`, `objectName`, `status`) and per-column text filters.
- Readable execution detail view with summary, related objects, snapshot availability, and step outcomes (no raw JSON rendering).
- Responsive behavior for desktop split-pane and phone drill-down flow with preserved list state.
- Timestamp display mode toggle (exact local default, switchable relative) and latest-request-wins coordination under rapid query changes.
- Backend performance integration test for 1,000-log datasets with strict local and CI-relaxed p95 profiles.

### Changed
- Execution log repository query path now uses an in-memory index loaded once and incrementally updated, reducing repeated disk-deserialization overhead on list requests.

### Tests
- Added/updated coverage across unit, integration, contract, and web-ui tests for execution logs query/detail/responsive behavior.

## [2025-12-17]

### Added
- Command Sequences (Phase 1–5)
  - File-backed sequences persisted under `data/commands/sequences`.
  - Minimal API endpoints:
    - `POST /api/sequences` (create)
    - `GET /api/sequences/{id}` (get)
    - `POST /api/sequences/{id}/execute` (execute)
  - Delay handling: fixed `delayMs` and ranged `delayRangeMs { min, max }` with precedence rules (range overrides fixed when present).
  - Gating: per-step `gate { targetId, condition, confidence? }` evaluated with timeout; on timeout the sequence stops and returns `status = "Failed"`.
  - Telemetry: structured per-step logging via `LoggerMessage` delegates (sequence start/end, applied delays, gate decisions, command duration).
  - Tests: unit tests for delay ranges and gating; integration tests covering fixed delays and gating present/absent scenarios.

### Changed
- README updated with a new “Command Sequences” section including endpoints, auth header usage, and PowerShell examples for create/execute.

### Notes
- Authentication is required for all non-health endpoints; set `GAMEBOT_AUTH_TOKEN` or `Service:Auth:Token`.
- Storage root can be overridden via `GAMEBOT_DATA_DIR` or `Service:Storage:Root`.

## [2025-12-02]

### Added
- Image detections: Additive endpoint `POST /images/detect` returning normalized bounding boxes and confidences in [0,1]. Includes unit, integration, and contract tests.
- Operational metrics: `/metrics/process` endpoint exposing `workingSetMB`, `managedMemoryMB`, and `budgetMB`; integration test verifies working set remains under configured budget (T039).

### Release Notes
Image Match Detections feature shipped across stacked phases:
- Phase 1–2: Introduced OpenCvSharp4 dependency, template matcher, IoU/NMS utilities, domain exceptions, timing helper, DI wiring.
- Phase 3: Exposed `POST /images/detect` endpoint with validation, structured logging, and contract + integration tests.
- Phase 4: Backward compatibility validation—existing `/images` endpoints unchanged; immutability and OpenAPI presence tests.
- Phase 5: Normalization guarantees—coordinates and confidences now resolution & scale independent with dedicated unit/integration coverage.
- Phase 6: Performance & hardening—timeouts, metrics (duration/result count + process memory), stress tests, resource safeguards.

Configuration keys (override via env): `Service__Detections__Threshold`, `Service__Detections__MaxResults`, `Service__Detections__TimeoutMs`, `Service__Detections__Overlap`.
Operational guidance: Raise `Threshold` to reduce noise; monitor `/metrics/process` for memory trends and adjust budget or template sizes accordingly.

### Notes
- Backwards compatibility: Existing endpoints and trigger evaluators remain unchanged. Detection feature is additive and does not mutate stored images.

## [2025-11-26]

### Added
- OCR: Tesseract TSV integration
  - Pass `-c tessedit_create_tsv=1` to emit `output.tsv` alongside `output.txt`.
  - Parse TSV into word-level tokens (level=5) with floating-point confidences.
  - Reconstruct text from TSV tokens when `.txt` is not produced.
  - Force `en-US` numeric parsing to avoid locale-specific failures.
- Tests and fixtures
  - TSV fixtures under `tests/TestAssets/Ocr/tsv` and unit tests for header/rows/aggregation/malformed cases.
  - Updated `TesseractProcessOcr` tests to assert TSV args and behavior.
- Persistent reference image storage:
  - Disk-backed `ReferenceImageStore` under `data/images` with atomic PNG writes.
  - Endpoints: `POST /images`, `GET /images/{id}`, `DELETE /images/{id}`.
  - Integration test ensuring persistence across restart.

### Changed
- Confidence calculation now prefers TSV aggregate (scaled 0–1) and falls back to legacy text heuristic only when TSV is missing or invalid.
- Triggers: default `CooldownSeconds` is now 0 (was 60). Added a unit test to lock the default.
- Image trigger flow now supports persisted reference images across service restarts (no re-upload needed).

### Notes
- Backwards compatibility: existing triggers remain unchanged; the cooldown behavior only differs for newly created triggers relying on the default value.
- No persistence schema changes; ENV docs updated for OCR TSV usage.

## [Unreleased] - 2025-11-25

### Added
- Actions, Commands, and Triggers as first-class resources with file-backed repositories.
- Endpoints:
  - `/actions` CRUD and `/sessions/{id}/execute-action` execution.
  - `/commands` CRUD, `/commands/{id}/evaluate-and-execute`, and `/commands/{id}/force-execute`.
  - `/triggers` CRUD, `POST /triggers/{id}/test`, and `POST /triggers/evaluate`.
- Metrics: `/metrics/domain` exposes counts for actions, commands, and triggers.
- Migration script: `scripts/migrate-profiles-to-actions.ps1` to convert legacy profiles to actions/triggers.
- Structured Tesseract invocation logging (debug-level) capturing CLI, stdout/stderr with truncation guard, exit code, elapsed time, and correlation IDs.
- OCR coverage tooling: `tools/coverage/report.ps1` now emits Cobertura-derived summaries, writes `data/coverage/latest.json`, and enforces ≥70% coverage for `GameBot.Domain.Triggers.Evaluators.Tesseract*`.
- Coverage summary API surface: `GET /api/ocr/coverage` plus contract/integration tests so stakeholders can query latest coverage without parsing XML.
- 001-image-storage: Persistent reference image storage under `data/images` with atomic writes, structured LoggerMessage logging for `/images` endpoints, standardized error responses (`invalid_request`, `invalid_image`, `not_found`), and increased test coverage (evaluator edge cases and endpoint error paths). CI stability improvements: isolated storage via `Service__Storage__Root` + `GAMEBOT_DATA_DIR`, persistence test robustness, and evaluator GDI+ OOM fix using `Graphics.DrawImage`.

### Changed
- Configuration precedence clarified and enforced: Environment > Saved file > Defaults. See `ENVIRONMENT.md`.
- OCR: Improved preprocessing and dynamic environment-driven OCR stub; Tesseract integration remains optional but supported.
- README updated with new domain model and migration guidance.
- README + specs quickstart now document OCR logging toggles, coverage script usage, and `/api/ocr/coverage` flows (stale/missing behavior, bearer auth).
- `POST /commands/{id}/evaluate-and-execute` now surfaces `triggerStatus` + `message`, always persists trigger evaluation before dispatching actions, and emits structured telemetry (`TriggerExecuted`, `TriggerSkipped`, `TriggerBypassed`). Integration tests now assert both HTTP metadata and trigger repository state for satisfied, pending, cooldown, and disabled flows; quickstart instructions updated accordingly.

### Removed (Breaking)
- All `/profiles` endpoints and nested trigger routes.
- Background trigger worker; evaluation is now explicit via endpoints or command gating.
- Legacy session execute path `/sessions/{id}/execute`.

### Migration
1. Backup the `data/` directory.
2. Run the migration script:
   - Dry run: `pwsh ./scripts/migrate-profiles-to-actions.ps1 -DryRun`
   - Convert and delete originals: `pwsh ./scripts/migrate-profiles-to-actions.ps1 -DeleteOriginal`
3. Update clients to use `/actions`, `/triggers`, and `/commands` endpoints.

### Notes
- OpenAPI spec regenerated for the new endpoints.
- Full test suite green after refactor with additional tests (cycle detection, metrics).
