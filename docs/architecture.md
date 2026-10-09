# GameBot Architecture & Capability Map

**This document describes the system as it is *now*. It is living documentation and MUST be kept
current** (see the project constitution, *Living Documentation* principle). When a feature changes
the domain model, the capability set, the API surface, or the persistence layout, update this file
in the same PR.

For the *history* of how the system got here — one folder per feature, point-in-time — see
[`specs/`](../specs/) and its roll-up [`specs/STATUS.md`](../specs/STATUS.md). Specs are immutable
history; this file is the current-state source of truth. When the two disagree, this file wins and
the relevant spec should be marked superseded.

_Last reviewed: 2026-10-09 (feature 131: auto-update from the Web UI, `/api/update/*`, the `GameBot.Updater` program;
feature 130: optional pixel `region` on `imageVisible` conditions and detection targets;
feature 129: `forceRestart` on ensure-game-running and the `restarted` outcome;
feature 128: `POST /api/ocr/read`;
feature 127: step-through of a saved sequence;
feature 126: failure message dedup by last sent message;
feature 125: reschedule-self `keep: earliest`;
feature 123: reschedule-self option `Cancel`;
feature 122: sequence option to exclude success notifications;
feature 121: device not-live alert, optional recovery, capture gate;
feature 120: queue sequence notifications; feature 118: the queue-template
endpoint and the sequence validator accept `timerTimeOfDay` in the same two forms, `HH:mm` and
`HH:mm:ss`, #226)._

## What GameBot is

GameBot automates Android games running in emulators. It connects to an emulator over ADB,
continuously captures the screen, detects on-screen images/text, and drives input (taps, swipes,
keys) according to user-authored **commands** and **sequences**, organized into per-emulator
execution **queues** with scheduling. Authoring and execution are driven from a browser Web UI
backed by a REST API.

## Solution layout

| Project | Role |
|---------|------|
| `src/GameBot.Domain` | Core domain model and logic — commands, sequences, queues, templates, primitive actions, trigger evaluation, vision/OCR, execution logging, versioning. No web/ADB dependencies. |
| `src/GameBot.Emulator` | ADB client and session management; the background screen-capture service. |
| `src/GameBot.Service` | ASP.NET Core host: REST API (minimal-API `Endpoints/` + `SessionsController`), execution orchestration (`Services/QueueExecution`, `Services/SequenceExecution`), hosted background services, security, swagger. Serves the built Web UI. |
| `src/GameBot.Updater` | Small console program for the auto-update (feature 131). It waits for the bot to exit, runs `msiexec` silent, starts the bot again, and writes `update-result.json`. Framework-dependent. The installer puts it in `updater\` next to the service. |
| `src/web-ui` | React + TypeScript + Vite SPA. Authoring, Execution, Execution Logs, Queues, Notifications, Configuration, Update. |

Persistence is **file-based** under the `data/` directory (JSON documents + stored image files;
an image's alternates list lives beside the images as `.alternates\{id}.json`, feature 097);
there is no database. Queue *runtime* state (loaded entries, running status) is in-memory and does
not survive a service restart; queue *configuration* and templates are persisted.

The auto-update (feature 131) keeps its files in `<data root>\updates\`. The folder has these items:

- `GameBot-<version>.msi`: the download. The bot deletes it after a successful update.
- `updater\`: a copy of the updater files.
- `update-result.json`: the result. The new bot renames it to `update-result.reported.json` after it reads it.
- `msiexec-<attemptId>.log`: the Windows Installer log. The bot keeps it for 30 days.

## Domain model (current)

- **Parameter** (feature 078) — a named, typed value a **Command** or **Sequence** accepts, so one
  entity can serve N emulator instances that differ only by a value. Lives in
  `GameBot.Domain/Parameters/`.
  - A **declaration** (`ParameterDeclaration`: name, type `text`/`number`, optional default, required
    flag, description) is what makes a parameter discoverable — the authoring UI renders it as a
    binding form on every call site and offers it in the insert-parameter picker.
  - A **binding** (`ParameterBinding`) supplies a value at one call site: a queue-template entry binds
    a sequence's parameters, and a sequence's command step binds a command's. `Value == null` means
    *inherit*; the empty string is a deliberate real value.
  - A **`ParameterScope`** is the immutable, layered set of names visible to a step when it is
    dispatched: queue built-ins → template entry → sequence → command → loop iteration. Resolution
    walks innermost-first and takes the first match, so an explicit binding beats an inherited value,
    which beats a declared default; nothing supplying the name fails the step rather than
    substituting anything. Not persisted — it exists only for the duration of a firing.
  - **Queue built-ins** are read-only names derived from the executing queue's own configuration and
    exposed under the reserved `queue.` namespace: `queue.emulatorSerial`, `queue.instanceName`,
    `queue.instanceIndex`, `queue.gameId`. A field the queue has not set is *absent* from scope, not
    empty. Because a queue already stores its serial, N queues driving one shared sequence need no
    parameter configuration at all.
  - A queue-template entry may also supply **ad-hoc** names the referenced sequence does not declare;
    they reach any command beneath the entry at any depth, so an intermediate sequence never
    re-declares a pass-through value. A supplied name nothing consumes is a warning, never an error.
  - **Where placeholders may appear**: any string leaf field carries `{{name}}` inline (and may embed
    it in surrounding text); numeric fields are supplied through `CommandStep.FieldTemplates`, a
    dotted-path overlay (e.g. `swipe.startX`), because a placeholder cannot live in an `int`, and must
    be a whole-field reference so the result parses. Command and sequence **references** are
    deliberately NOT substitutable, which keeps the dangling-reference validation exact.
  - **Parametrized reference image** (feature 114): `FieldTemplates` also accepts two image keys,
    `primitiveTap.detectionTarget.referenceImageId` and `waitForImage.detectionTarget.referenceImageId`.
    The value must be one whole placeholder (else `400 invalid_field_template_value`). At dispatch the
    resolved value replaces the inline image id, and the inline value is not resolved. An
    `imageVisible.imageId` can hold an inline placeholder in each condition position (`condition`,
    `if.condition`, `loop.condition`, `breakCondition`, and each composite child).
    `SequenceStepConditionResolver` resolves it against the scope of the call site before each
    evaluation: the step scope for a step guard and an `If` condition, the next iteration for a
    while condition, the iteration that just ran for a repeat-until condition, and the current
    iteration for a break condition. An unresolved name fails the step (a break condition gives
    "No break"). A parametrized image field skips the save-time existence check and gives the
    warning `static_check_skipped`. A queue template save checks each known value (entry value or
    default) that goes to an image field, and gives `400 unknown_image_reference` when no image has
    that id. A non-null step binding of the name covers it, so the save does not check it.
  - **Placeholder in a binding value** (feature 115): a `{{name}}` placeholder in a step
    `parameterBindings` value resolves against the scope outside the binding
    (`ParameterScope.TryBindChild`), never against the new binding layer. This applies to a sequence
    step and to a nested `Command` step. A value that is one whole placeholder keeps the origin layer
    of the resolved value. A value with text around placeholders gets the layer `command`, and the
    log adds one source item for each placeholder name after the used name. An unresolved name gives
    a `ParameterResolutionError` with the field path `parameterBindings.<bindingName>`. Its message
    tells how to supply the value. A sequence step then fails and the command does not run. A nested command step
    gives `skipped_parameter_unresolved`, and the calling command continues with its next step. Only
    `{{name}}` is a placeholder: `${name}` is literal text, and a `null` value inherits.
  - **Validation** splits three ways (`ParameterValidationService`): declaration well-formedness and
    statically-unresolvable references block a save (400); unsatisfied required parameters and unused
    ad-hoc values are reported as warnings on a template save; starting a queue is refused with
    `409 missing_required_parameters` before any device work. Resolution failures at dispatch fail the
    step and dispatch nothing.
  - Everything is additive and omitted from JSON when empty, so commands, sequences and templates
    stored before the feature load and re-serialize byte-identically. **No automatic migration
    exists** — conversion is manual, documented in `specs/078-sequence-parameters/quickstart.md`.
- **Game** — a target app (package) the bot can connect to.
- **Image (reference image)** — a stored bitmap used as a template for on-screen detection;
  disk-backed under `data/`. May carry a **transparency mask**: pixels at least half opaque
  (alpha >= 128) are compared, the rest are excluded from the score entirely, so a non-rectangular
  target is matched on its own pixels rather than on the scenery behind it (feature 089). Masking is
  a property of the image, not of the caller — every detection path honours it, and no sequence
  needs editing. An image with no alpha, or with an all-opaque alpha, is not masked and scores
  exactly as it did before feature 089. A masked comparison reports no match where the retained
  region — on either side — is too featureless to correlate (the no-information rule, below).
- **Command** — an ordered list of **steps**. Steps are **primitive actions** plus control
  structures (loops, per-step conditions). A command may carry a vestigial `TriggerId`.
- **Primitive Action** — the unit of input/effect. Current variants: **Tap**, **Swipe**,
  **Key**, **Wait for Image**, **Connect to Game**, **Ensure Game Running**, **Go to Home Screen**,
  **Ensure Emulator Running**.
  (These replaced the old first-class "Action" object — see *Legacy/removed* below.) Taps/swipes
  resolve coordinates from image detection + offset, with wait-and-retry and tap-point jitter
  applied automatically. A `PrimitiveTap` command step has an optional `holdMs` (0 to 5000, feature
  111). More than 0 gives a press and hold at the detected point for that duration: one ADB swipe
  with the same start and end point and a duration of `holdMs`. Absent or 0 gives the single tap (the
  same swipe with 200 ms). The step outcome and the execution-log `tap` detail show `holdMs` for a
  press and hold. The service rejects a value outside 0 to 5000 with 400. A "not executed" `PrimitiveTap` outcome (`skipped_*` or
  `cancelled`, `accepted: 0`) means that no input went to the device (feature 112). When an error or a
  cancellation occurs after the session accepted the tap, the outcome is `executed` with the reason
  `executed_then_error` or `executed_then_cancelled` and the accepted count of the session. When the
  session fails or is cancelled during the dispatch, the outcome is `dispatch_unknown` (`dispatch_error`
  or `dispatch_cancelled`), because the device can have the input. `POST /api/steps/execute` writes one
  execution-log entry of the type `step` (object id = session id) for each call that passes the session
  check, also for the 10-second timeout. The command executor owns that limit. **Go to Home Screen** (`go-to-home-screen`, feature 069) is a parameterless
  action that presses Android HOME (keycode 3) so the device returns to its home/main screen,
  leaving the game running in the background — the leave-game counterpart to Connect to Game. It is
  usable both as a sequence action (dispatched through the session input pipeline) and a command
  step, and degrades to a stub success on non-Windows/non-ADB sessions.
  **Ensure Emulator Running** (`ensure-emulator-running`, feature 070) verifies a target **LDPlayer**
  instance is running AND responsive (not hanging) and starts a stopped instance or restarts a hung
  one, waiting for boot-complete before succeeding. It is parameterized (an instance name or index
  plus the adbSerial used for the probe) and is the emulator-lifecycle sibling of the app-lifecycle
  Ensure Game Running. Emulator control uses LDPlayer's `ldconsole` CLI (`GameBot.Emulator/Adb/LdConsoleClient`
  + `LdConsoleResolver`, mirroring `AdbClient`/`AdbResolver`), fronted by fakeable `IEmulatorControl`/
  `IEmulatorDeviceProbe` seams and orchestrated by `EnsureEmulatorRunningActionHandler`. Health = `isrunning`
  + device state `device` + `sys.boot_completed=1`. Timeouts are configurable via `GAMEBOT_EMULATOR_PROBE_TIMEOUT_MS`
  (10s), `GAMEBOT_EMULATOR_BOOT_WAIT_MS` (120s), `GAMEBOT_EMULATOR_POLL_INTERVAL_MS` (3s). It degrades to a
  neutral no-op on non-Windows hosts or when ldconsole/ADB is unavailable, and fails the step for a
  nonexistent instance or a recovery timeout.
  **Force restart** (feature 129): `ensure-game-running` has the optional boolean `forceRestart`
  (a sequence action payload key, and `EnsureGameRunningConfig.ForceRestart` on a command step). When true,
  `EnsureGameRunningActionHandler.RestartAsync` runs `adb shell am force-stop <package>` on the device of the
  session, waits 1 s, starts the game with the monkey launch, and polls the foreground for up to 30 s.
  Each ADB call has its own time limit (`EnsureGameRunningRestartOptions`), so the worst case is
  10 s + 1 s + 10 s + 30 s. The failure reasons are `restart_no_device`, `restart_stop_failed`,
  `restart_start_failed`, and `restart_foreground_timeout`. A sequence action step records the state
  `restarted`; the command step shows the reason `restarted` in its own outcome list. A bad value is
  rejected with 400 (`EnsureGameRunningPayload` is the one reader). A step-through previews a restart step
  and does not run it. Without the option, the step is unchanged.
  **Connect to Game** (`connect-to-game`, feature 021) starts/attaches a session for a game on a device
  (`gameId` + `adbSerial`) and then runs Ensure Game Running to foreground/launch the app. Feature 071
  added an OPTIONAL emulator pre-heal: when the connect action also carries an LDPlayer instance
  identifier (`instanceName`/`instanceIndex`), `DispatchConnectToGameAsync` first runs the feature-070
  `EnsureEmulatorRunningActionHandler` against that instance + the same `adbSerial` before attaching —
  a genuine emulator failure (recovery timeout / instance-not-found) fails the connect before any
  session start, while success or a neutral unsupported outcome proceeds; with no instance identifier
  the connect behaves exactly as before. (The separate interactive `/api/sessions/start` endpoint is
  unchanged.)
  The `ensure-game-running` **command step** has an OPTIONAL readiness gate: with no config it reports
  success as soon as the game package is foreground (legacy behavior), but when its
  `EnsureGameRunningConfig.ReadinessImage` is set the step, after best-effort launching the game, polls
  the live screen for that image (via `IGameReadinessProbe` → `ImageDetectionHelper`, the same
  template-match cycle as `waitForImage`) for up to `ReadinessTimeoutMs` (default 90s) before reporting
  `game_ready`; on timeout it fails the step with `readiness_timeout`. This prevents a cold-launched
  game that is still on its splash/loading screen from letting the queue's startup sweep run daily
  sequences prematurely. If the game/session cannot be resolved the step short-circuits to the handler
  failure without polling; the probe is Windows-only (the vision stack), so off Windows the step keeps
  its foreground-only behavior.
- **Sequence** — an ordered list of steps that run **commands**, with random inter-step delays,
  conditional steps, loop/flow blocks (`SequenceFlowGraph`, `Blocks/`), and **if blocks**
  (`SequenceStepType.If`, feature 067): a condition (same model as while-loop conditions —
  `imageVisible`/`commandOutcome`/composite with negation, `IfConfig`), a then branch (reuses
  `SequenceStep.Body`), and an optional else branch (`SequenceStep.ElseBody`; `null` = absent).
  If blocks may sit at the sequence top level or inside loop bodies; branches are flat (no loops,
  no nested ifs; breaks only when the if is inside a loop, where a branch break exits the
  enclosing loop). The condition is evaluated once per encounter; a condition error fails the
  step and sequence exactly like a while-loop condition error. A `condition` on a top-level
  `Loop` step is a guard with the same rules as the guard of an `Action` step (feature 110, #232):
  it is evaluated one time, before the first iteration. When it is false, the loop does not run,
  and its entry has status `Skipped`, no iterations and `conditionResult` `false`; a later
  `commandOutcome` reads the loop as `skipped`. The loop entry of the run result and of the
  execution log has `conditionType` and `conditionResult` when the loop has a guard.
- **Queue** — bound to exactly one emulator; holds ordered **entries** (sequences), a
  cycle-execution flag, and an optional **failure policy**. Runs entries against the emulator; can
  cycle. Each sequence firing runs under a **time bound** (the watchdog): the sequence's own
  `watchdogTimeoutMs` when set (1..1,800,000 ms), otherwise the default 240,000 ms
  (`SequenceTimeLimits`). A firing that exceeds it is cancelled and the run continues with its next
  entry. `GET /api/sequences/{id}` reports the stored override (`watchdogTimeoutMs`) and the bound
  that applies (`effectiveWatchdogTimeoutMs`, read-only) (feature 094).
- **Queue failure policy** (`QueueFailurePolicy`, feature 087) — optional persisted configuration on
  a queue: a consecutive-failed-cycle threshold, a `QueueFailureAction`
  (`Notify`/`Stop`/`Pause`/`NotifyAndStop`), and an optional destination URL. Null means no policy
  and no evaluation. Deliberately carries **no credential** — the auth header for a destination is
  service configuration, because a queue is persisted to a JSON file the backup/restore endpoints
  copy around. Evaluated at cycle granularity against the feature-086 ledger, never per sequence: a
  single flaky firing must not trip a policy.
- **Queue Template** — a named, persisted snapshot of a queue's ordered entries and their
  **schedules**. A queue links to 0..1 templates (auto-loaded when the queue opens); a template can
  be shared across queues.
- **Queue roster** — a queue's own ordered entries, read from `GET /api/queues/{id}` `entries`
  (always an array). It comes from the runtime store, so it can differ from the linked template's
  entries (a running queue keeps the entries it started with). There is no
  `GET /api/queues/{id}/entries`; the entries path only has POST/PUT/DELETE writes. The OpenAPI
  document describes `entries` and each entry's fields via `QueueRosterSchemaFilter`, and the write
  operations point at the read path (feature 100, issue #179).
- **Queue duplication** (feature 083, amended 2026-09-12) — `POST /api/queues/{id}/duplicate`
  creates a near-1:1 copy of an existing queue: every configuration field (cycle-execution,
  idle-pause, linked template reference, linked game reference) plus the source's *currently
  loaded* runtime entries are copied into a brand-new queue. The one exception is the emulator
  target (serial, instance name, instance index): the client resubmits these — typically pre-filled
  from the source, editable in the web-ui's Duplicate dialog — and the endpoint applies whatever
  the request contains, so a duplicate can be pointed at a different emulator; unlike the name, an
  unchanged emulator is accepted. The duplicate links to the **same** template record — no template
  copy is made. The new name is required and MUST differ from the source's current name (ordinal
  comparison); queue names are otherwise not required to be unique. The duplicate is always created
  `Stopped` with no execution history, since a brand-new queue ID has never been started — the
  source queue's own running/stopped state is untouched either way.
- **Sequence schedule** (within a template) — how/when an entry runs in a queue cycle:
  *Once per run*, *At queue start*, *After every step*, *Before each run*, and *Scheduled* (absolute or
  relative time). *Before each run* (wire value `BeforeEachRun`, feature 095) is the mirror of *After
  every step*: its entries run, in template order, immediately before the first timed, live-scheduled or
  self-rescheduled firing of each scheduling-loop iteration (time-of-day timers and their daily retries,
  relative timers, live schedules, self-reschedule Timer / At Queue Start / Once Per Run firings). The
  pass runs at most once per iteration, and never before once-per-run, at-start or every-step
  executions. It does not count toward executed, and its failures are non-fatal. It does not by itself
  keep a run alive or make a cycle count as work, and the monitor lists each entry once as "Before Each Run".
- **Entry enabled/disabled** (within a template, spec 077) — each `QueueTemplateEntry` carries an
  `Enabled` flag (bool, default `true`; absent in legacy JSON ⇒ enabled). A disabled entry stays in
  the template (position/schedule/reference intact) but is **excluded when a run is built**: the run
  reads `template.Entries.Where(e => e.Enabled)` in `QueueExecutionService.RunAsync`, so a disabled
  entry never fires and is absent from all schedule partitions and the monitor projection. The
  runtime store / `GET /queues/{id}` retains **all** entries (the template editor renders from them
  and merges each entry's schedule+enabled from the template detail by position), so disabled entries
  stay visible and re-enableable. Toggling takes effect on the next run start. Exposed as an on/off
  switch per card in the template editor; persisted via the normal template save.
- **Self-reschedule Cancel** (feature 123) — the `reschedule-self` option `Cancel` removes the pending
  Timer, Once Per Run and At Queue Start bookings of the owner sequence in the current queue run. It
  also stops a Once Per Run booking that the run loop already copied for firing but did not start (the
  handle keeps an in-flight list and a cancelled-id set). It keeps After Every Step registrations, live
  schedules, queue template entries and the bookings of other sequences. A booking made after Cancel
  stays (last booking wins). Cancel never fails the run: outcome `cancelled` with `removed` true, or
  `noop` with `removed` false (nothing pending, no queue, queue run not active). The execution log of
  the step has the `removed` field. Cancel with `timerTimeOfDay`, `timerRelativeOffset` or `ocrOffset`
  is a 400 on create, update and PATCH.
- **Self-reschedule keep earliest** (feature 125) — a Timer `reschedule-self` step can have
  `keep: earliest`. The register keeps one Timer booking for each sequence. Without `keep`, the last
  booking wins. With `keep: earliest`, a booking does not replace a pending booking of the same run when
  that booking fires at the same time or earlier. The step then succeeds with the result `scheduled`,
  the booking is dropped, and one Information log message gives both fire times. Every Timer booking
  stores a run id. The run id is the id of the sequence execution (the id of its execution log entry).
  A booking of another run, a booking with no run id, and a re-armed booking are always replaced.
  `RearmTimerFiring` stores the held booking with no run id. The check and the write are one step
  under the timer lock of `QueueRunHandle.AddTimerFiring`. `keep` is valid only with option `Timer`
  (400 on create, update and PATCH).
- **Self-reschedule action** (within a sequence) — an authorable sequence action (`reschedule-self`,
  placeable under IF/conditional flow) that, when reached during a queue-driven run, schedules **one
  additional firing of the same sequence into the current run** using any of the schedule options
  above (At Queue Start / Once Per Run / Timer / After Every Step). It is **ephemeral** (current run
  only, never persisted) and a **success no-op** when the sequence was not started from a queue. The
  run's active-run state lives in a singleton `IQueueRunRegistry`; an `ISelfRescheduleCoordinator`
  injects the ephemeral firing, which the queue run loop drains at the matching boundary. The **Timer**
  option is **most-recent-wins per sequence** (feature 075): a new Timer firing replaces any pending
  Timer firing already queued for the same sequence in that run, so a self-rescheduling sequence never
  stacks duplicate future firings. `timerTimeOfDay` has one format in the queue-template endpoint and in
  the sequence validator: `HH:mm` or `HH:mm:ss` (24-hour). Both use `TimerTimeOfDayFormat` and reject
  all other forms (feature 118, #226). A Timer `timerTimeOfDay` that is not later than the local time
  now books that time on the next local day, with the offset of the local time zone for that day
  (feature 109, #227). The other options are unchanged — *Once Per Run* / *At Queue Start*
  accumulate, and *After Every Step* is idempotent per sequence. A pending booking (or pending live
  schedule) keeps the run's scheduling loop going **whatever schedule types the template uses** —
  including a template of only At Queue Start entries, whose start pass would otherwise end the run
  before its bookings fire (feature 092). A booked run keeps the parameter scope of the run that
  booked it: the scope that the queue run loop gave that run (queue layer and entry layer), for all
  four booking options. Thus a required parameter that the template entry supplies resolves in the
  booked run too, and a chain of bookings keeps the scope for each generation (feature 116, #249).
  A booking with no scope uses the queue scope.
- **Notify action** (within a sequence, feature 087) — an authorable sequence action (`notify`) that
  raises an outbound alert carrying an author-written `message` (required, ≤1000 chars) and an
  optional per-step `url`. It touches no device, which is the point: a guard that has detected an
  unusable screen must still be able to say so. It **always succeeds** — a delivery failure is
  recorded on the run's health and in the application log but never fails the step or the enclosing
  sequence, so adding an alert to a guard cannot make that guard less reliable. Payload is validated
  at save time. Queue id/name/serial are populated when the sequence runs inside a queue and null for
  an ad-hoc run. Complements the queue-level failure policy (feature 087 below): the policy is service
  configuration, this keeps escalation inside a committed, reviewable sequence artifact.
- **Queue monitor** — a read-only "live plan" view of a *running* queue. `GET /api/queues/{id}/monitor`
  returns a pure projection (`IQueueMonitorService`) that folds the active `QueueRunHandle` (the
  sequence-level "now" indicator plus pending live schedules and self-reschedule firings) and the
  linked template with the current local clock into the sequence running **now** and the ordered
  **up-next** list — each with a schedule reason and a best-effort expected time (exact for
  live/self-reschedule/relative; next-eligible for time-of-day timers). Nothing is persisted; the
  snapshot is computed per request. When the queue is not running the endpoint returns
  `running:false` with the best-effort last outcome from the execution log. The web UI opens this
  monitor (polling ~2.5s) in place of the entry editor while a queue is Running (feature 072).
- **Queue cycle observability** (feature 086) — evidence that a *cycling* run is doing work.
  `QueueExecutionStatus` is only `Stopped | Running`: a start/stop flag that cannot distinguish a queue
  cycling healthily from one failing every cycle, which once let two production queues report `Running`
  while doing nothing for 44 hours. Each active `QueueRunHandle` now carries a `QueueCycleLedger` — an
  in-memory, run-scoped, lock-guarded record of completed cycles, bounded to the newest **50** (oldest
  discarded first, so a week-long run costs the same as a short one). The run loop drives it at the
  three points it already had: it opens a cycle at the top of each loop iteration (idempotently),
  records each sequence firing's outcome, and seals the cycle at the loop's existing cycle counter — so
  a published cycle is exactly a cycle the engine counted. A cycle is `failure` iff any entry in it
  failed; one interrupted by a stop is never published. A cycling run's loop iteration that runs no
  sequence at all (e.g. a template of only at-queue-start and timer entries between firings) is **not**
  a cycle: it is neither counted nor published, its open cycle is discarded, and the run waits for the
  next due firing — the idle-pause hold when enabled and the gap exceeds the threshold, otherwise one
  250 ms poll — instead of looping at once (feature 093, #200; previously ~450k empty cycles/s). The ledger is a **pure observer**: no
  scheduling decision reads it, and the execution log is untouched (still one root record per run plus
  one terminating record). Two read paths project it, both readable **while the run is in progress**:
  `GET /api/queues/{id}` gains a `health` block (`runStartedAt`, `cyclesCompleted`, `lastCycleStartedAt`,
  `lastCycleCompletedAt`, `lastCycleStatus`, `consecutiveFailedCycles`, `currentEntryIndex`,
  `currentSequenceId`), and `GET /api/queues/{id}/cycles?limit=n` returns recent cycles newest-first
  with their per-entry outcomes (`limit` defaults to 20, clamped to 1-50 rather than rejected; 404 for
  an unknown queue, `running:false` with an empty list for a known but stopped one, as with `/monitor`).
  Both gate on the **conjunction** of `GetStatus(id) == Running` *and* a registered run handle: those
  two stores are not updated together (the handle is added before `SetStatus(Running)` and removed
  after `SetStatus(Stopped)`), so keying off the handle alone would emit a populated `health` on a
  response whose `status` reads `Stopped`. `health` is `null` when not running, never a zeroed block.
  All values describe the **current run** and do not survive a service restart; the queue *list*
  response is unchanged. Exposing `consecutiveFailedCycles` was deliberate — feature 086 shipped it
  inert, and feature 087 is what acts on it.
- **Queue failure policy and outbound notification** (feature 087, issue #181) — the escalation path
  feature 086 left out. Observability made a failing queue *visible to anyone who asks*; this makes it
  *tell someone*. During the 2026-09-14 outage the per-entry guards on two rosters worked exactly as
  designed — they detected a bad screen and refused to act on it, every cycle, for 44 hours — and it
  changed nothing, because a guard can protect the account but cannot escalate.
  `ExecutionQueue.FailurePolicy` is optional persisted configuration: a consecutive-failed-cycle
  threshold plus a selectable action — `notify` | `stop` | `pause` | `notifyAndStop` — and an optional
  `notifyUrl` overriding `Service:Notifications:DefaultUrl`. **`notify` is the primary action, not
  `stop`**: a cycling roster can self-heal (during that outage it relaunched the game itself once the
  network returned), so an automatic halt is a liability where an alert is a guardrail.
  `QueueFailurePolicyEvaluator` is called from one line in the run loop, immediately after the ledger
  seals a cycle. It reads the ledger's existing `SnapshotHealth()` — **the ledger itself gains no
  policy knowledge and stays a pure observer**; keeping the decision in a separate type outside the
  observer's lock is what makes acting on a failing run safe. The policy trips at most **once per
  failure episode** (a tripped flag, cleared by any successful cycle, which also re-arms it), so a
  44-hour outage produces one alert rather than one per cycle. `IFailureNotifier` POSTs a versioned
  JSON payload (`schemaVersion: 1`) carrying queue id/name/serial, the failing entry and sequence, the
  failed-entry count, the consecutive count and the action; delivery runs **off the run loop's thread**
  with a 5s per-attempt timeout and 2 attempts, swallows every fault, and uses a token that is *not*
  the run's so a `notifyAndStop` alert survives the stop it announces. An optional static auth header
  (`Service:Notifications:AuthHeaderName`/`AuthHeaderValue`) is a secret — never returned by any
  endpoint or written to a log. **Pause is run state on `QueueRunHandle`, not a third
  `QueueExecutionStatus`** (still `Stopped | Running`): the gate is awaited at the top of the loop
  iteration *before* any due-ness evaluation, so on resume a firing that came due while parked is
  simply still due — no skip list, no catch-up bookkeeping. `POST /api/queues/{id}/resume` releases it
  (200 with `resumed: true|false` for every known queue, 404 for an unknown one) and clears the failure
  count; `POST {id}/stop` still works on a parked run. A policy stop records the new
  `QueueStopReason.StoppedByFailurePolicy`, distinguished from an operator stop by a marker on the
  handle — both cancel the same token, and reporting one as the other would say a person halted
  production when nobody did. `health` gains `failurePolicyConfigured`, `failurePolicyTripped`,
  `paused`, `pausedAt`, `pauseReason`, `lastNotificationAt`, `lastNotificationSucceeded` and
  `lastNotificationError`. Policy *state* is per-run and in-memory; only the configuration persists.
- **Idle-pause** (feature 073) — an opt-in per-queue behavior (`ExecutionQueue.PauseWhenIdle` +
  `IdleThresholdSeconds`, default 30s; exposed via the REST API and web-ui, not MCP). When a
  non-cyclic run has no sequence due and the gap to the next scheduled firing exceeds the threshold,
  the run loop backs the game out to the device home screen (Android HOME) and foregrounds it again
  (`IEnsureGameRunningActionHandler`) when the firing becomes due — re-checking the next-due instant
  each poll tick so an earlier-arriving live/self-reschedule firing shortens the pause. The pause runs
  **inline** in the wait tail, not as a scheduled sequence, so it is exempt from the 4-minute
  per-sequence watchdog and writes **no** execution-log entries; backgrounding/foregrounding are
  best-effort (a failure is non-fatal) and a stop takes effect within one poll interval. While paused,
  the `QueueRunHandle` carries an idle-pause register that the monitor projects as a synthetic current
  item (`ScheduleKind.IdlePause`, `SequenceName` "Idle Pause", with the resume time), so an idle queue
  never reads as hung. The same register, which also records when the hold began, feeds the queue
  detail's `health` block (feature 096): `paused: true`, `pausedAt`,
  `pauseReason: "idle pause: resumes at HH:mm"`, `pauseKind: "idle"`. Both pause kinds go through one
  `QueueRunHandle.SnapshotPause()`, which reports the failure-policy pause when both are in force.
  Disabled queues are byte-for-byte unchanged.
- **Resume after a service restart** (feature 098, #203) — an opt-in per-queue flag,
  `ExecutionQueue.ResumeOnServiceStart` (default `false`; absent in older JSON ⇒ off; on queue
  create/update/response, copied by duplicate, a checkbox in the web-ui queue form). Queue run state
  is otherwise in-memory, so before this a restart, upgrade or host reboot left every queue `Stopped`.
  `IQueueRunStateStore` (`FileQueueRunStateStore`, `<data>/queue-run-state.json`,
  `{ "runningQueueIds": [...] }`) is the one durable piece of run state: `QueueExecutionService.StartAsync`
  records the id after the device claim and before the run launches (so a crash still leaves it
  recorded), and the run's `finally` removes it **unless the host's `ApplicationStopping` token is
  cancelled** — an operator stop, completion, run-level failure and failure-policy stop all forget it,
  a service shutdown does not. `QueueResumeOnStartupService` (hosted) waits for `ApplicationStarted`,
  reads the record once, and for each id starts the queue through the normal `StartAsync` when it
  exists and opts in, otherwise drops the record; one attempt per queue per service start, no retry,
  each outcome logged (event ids 7300–7304). The resumed run is an ordinary fresh start from the linked
  template — self-reschedules, live schedules and daily-retry state of the previous run are not
  restored. Store writes are serialized and atomic (temp file + replace); a corrupt record makes the
  resume pass log and resume nothing, while the next start/stop overwrites it. A store failure never
  fails a start or a run teardown (logged, event ids 1128/1129).
- **Sequence run statistics** (feature 105, #224) — the queue records each sequence run that it
  starts (all schedule types, guard sequences too) in `ISequenceRunStatisticsStore`
  (`FileSequenceRunStatisticsStore`, `<data>/queue-sequence-stats/<queueId>.json`, `schemaVersion: 1`).
  For each (queue, sequence) pair the store keeps `SequenceRunStatistics`: the last-run start, end and
  status (`success` | `failure` | `cancelled`), the last success time, three total counters, and the
  100 most recent `SequenceRunRecord`s. `QueueExecutionService.RunOneSequenceAsync` takes the start
  time just before `ExecuteAsync` and records after it through `RecordRunAsync`. A Break end is
  `success`; a stop by hand, a failure-policy stop and the sequence time limit are `cancelled`; a run
  that a host shutdown interrupts, and a fault before the sequence starts, are not recorded. A store
  failure is logged (event 1132) and never changes the run. The store keeps an in-memory copy, writes
  atomically (temp file + replace), and reads a damaged file as empty with one warning. `DELETE
  /api/queues/{id}` deletes the file, and a record for a deleted queue is ignored. A queue stop waits
  for the run that is in progress, including its record.
- **Pre-session emulator cold-start** (feature 074) — an opt-in per-queue behavior
  (`ExecutionQueue.EmulatorInstanceName` / `EmulatorInstanceIndex`, both optional/null by default;
  exposed via the REST API and web-ui). When set, the queue run brings the target **LDPlayer**
  instance up **before** it binds its device session — reusing the feature-070
  `ensure-emulator-running` capability (`IEnsureEmulatorRunningActionHandler`) with the queue's
  `EmulatorSerial` as the responsiveness probe — so a queue can self-start from a backend-only cold
  state (a closed emulator would otherwise fail `CreateSession`). An already-healthy / started /
  restarted or neutral unsupported outcome proceeds to create the session as before; a genuine failure
  (recovery timeout / instance-not-found) fails the run with an actionable reason and creates **no**
  session. No new emulator-tuning configuration is introduced (feature-070 timeouts apply). Queues
  with the fields unset perform no emulator management (byte-for-byte unchanged).
- **Queue session lifetime** (feature 104, #217) — the session a queue run binds carries
  `EmulatorSession.OwnerQueueId` and is **exempt from the idle-timeout sweep**
  (`Service:Sessions:IdleTimeoutSeconds`, default 1800 s); it ends only when the run's teardown stops
  it. Ad-hoc API sessions are still idle-evicted. Before every firing the run checks its session; if
  it is missing it makes one attempt to bind a new owned session on the same serial (moving
  background capture to it, logged as event 1131 `SessionRebound`) and continues. Only a failed
  re-bind — serial not listed by ADB, no devices, or session capacity reached — fails the run with
  `emulator connection lost mid-run ('<serial>')`. Before this, a queue idle for over 30 minutes lost
  its own session to the sweep and failed its next scheduled firing.
- **Trigger** — an evaluation construct (image-visible / text-match / time / delay / schedule),
  used internally to decide whether a step executes. Still present in the domain and on the API,
  but **no longer authored as a standalone object in the UI**.
- **Execution Log** — persisted, hierarchical record of what actually ran (queue → sequence →
  command → primitive action) with outcomes, timings, detections, and condition traces. Entries are
  kept for **7 days by default** (configurable; an explicitly saved retention value is never
  overwritten by the default). A queue run open longer than **24 hours** is split into successive
  **run segments**: at the next firing boundary — never mid-sequence — the current queue-root entry
  is closed out and a fresh one opened, linked by `RotatedToExecutionId` /
  `RotatedFromExecutionId` so the chain can be walked in either direction. Later firings attach to
  the newest segment, and each segment expires independently under retention. Status is only
  `running`/`success`/`failure`. When a queue firing's time bound cuts a sequence off, that sequence's
  entry stays `failure` and also carries `cancellationReason: "sequence_time_limit"` and
  `timeLimitMs`. This also applies when a step swallowed the cancellation and the run ended as an
  ordinary failure after the bound fired. User stops, ordinary failures, successes, ad-hoc runs and
  child command entries never carry it. The queue pushes an ambient `SequenceTimeLimitScope` with a
  timer-only token, so the finalize can tell the bound from a stop (feature 094).

## Capability map (what the product does today)

- **Authoring UI** (nav: Commands, Games, Sequences, Images): unified CRUD pages; image selector
  dropdowns with thumbnails; drag-and-drop step reordering in commands and sequences; visual
  command recorder with step simulation; emulator-screenshot cropping to create reference images;
  backup & restore as a downloadable zip.
- **Vision / OCR**: OpenCV-based template matching (bundled, no external binary) returning
  multiple detections with confidence; Tesseract OCR with TSV-based confidence.
- **Execution**: per-emulator queues with start/stop, cycle execution, scheduling areas
  (start / once-per-run / after-every-step / scheduled), live relative scheduling against a running
  queue, sequences that can **self-reschedule** into their originating queue run (ephemeral, any
  schedule option, IF-gated), a **live monitor** that shows a running queue's now/up-next plan
  (read-only, auto-refreshing) in place of the editor, a background screen-capture service reporting
  FPS, and "ensure game running" handling.
- **Concurrent queue runs** (feature 079): several queues run at the same time, **one per emulator**.
  Each run holds an exclusive, in-memory claim on its ADB serial (`IDeviceClaimRegistry`); starting a
  second queue on a claimed device is refused with `409 device_in_use` naming the device and the
  holding queue, distinct from `already_running`. The claim is released when the run ends for any
  reason and never survives a restart. See "Device-scoped observation" below for how each run's screen
  reads are kept to its own device.
- **Execution Logs** (separate tab): filterable/sortable grid, expandable hierarchy reflecting
  what actually executed, deep links into authoring, snapshots and step outcomes; non-technical
  presentation (no raw JSON).
- **Configuration**: dynamic backend-driven config editor (drag-and-drop reorder, filtering),
  runtime per-component logging level control, jitter/retry/delay parameters.
- **Packaging**: standalone Windows installer (EXE/MSI) with semantic-version upgrade flow
  (build auto-versioning, downgrade prohibition).

### Device-scoped observation (feature 079)

Every screen read is resolved against **one** device, so concurrent runs cannot observe each other:

- `IScreenSourceFactory.ForSession(sessionId)` returns a `SessionScopedScreenSource` bound to one
  session for its lifetime. Used wherever the caller already knows its session — `CommandExecutor`'s
  detect-and-tap and wait-for-image paths, and `GameReadinessProbe`.
- `IDeviceContextAccessor` (an `AsyncLocal<DeviceContext>`) carries "which device is this execution
  flow acting on". `SequenceExecutionService.ExecuteAsync` pushes it for the whole sequence when given
  a session, so nested sequences, commands, loops and trigger-based image/text conditions inherit it
  without `ITriggerEvaluator` needing a session parameter.
- The singleton `IScreenSource` (`BackgroundCaptureScreenSource`) resolves: **ambient context → the
  single running session → nothing**. It previously returned the frame of the *first* running session,
  which silently gave one queue run another run's screen.
- Device resolution for steps follows one rule (`SessionResolver`): an explicit session always wins;
  with none supplied, exactly one running session is used; several running sessions fail the step with
  `"N device sessions are active; specify a sessionId for '<step>'"` rather than guessing.
- `GET /api/emulator/screenshot` takes `sessionId` or `serial`; with several sessions and no selector
  it returns `409 ambiguous_session` instead of an arbitrary device. A `serial` with no bound session
  returns `404 session_not_found`, whose message says to start a session or a queue on that device.
- `POST /api/ocr/read` (feature 128) reads the text in a region of the live screen (`serial`) or of a stored
  capture (`captureId`). Set exactly one source. It returns the raw text and the confidence value. An optional
  `parser` (`hh:mm:ss`) adds a parsed duration, or a reason when the text does not parse (still status 200).
  The endpoint sends no input to the emulator and keeps no cache. It shares `OcrRegionReader` with the
  `ocrOffset` step, so both read the same text for the same frame and region. Errors use `{ code, message }`.
  The first fault wins in this order: request faults (400), lookup (404), capture (502 or 503), engine (503),
  region inside the frame (400).
- `POST /api/images/detect` takes `captureId` or `sessionId` (mutually exclusive; blank counts as
  absent) and follows the same rule, reusing the same codes: `409 ambiguous_session` when several
  sessions are running and none is named, `404 capture_not_found` / `404 session_not_found` for an
  unresolvable named target, `503 emulator_unavailable` when no screen is obtainable at all. Until
  feature 085 it answered every one of these with `200 {"matches":[]}` — a fabricated "absent"
  indistinguishable from a real one, which silently disarmed absence probes as soon as a second
  emulator was started. **A 200 from this route now means a measurement was actually taken.** The
  unresolved case is detected from the screen source returning null, never from counting sessions
  first: stub hosts serve a fixed bitmap with zero sessions, so a pre-emptive count would break them.
- `POST /api/images/detect` has a time limit of `Service:Detections:TimeoutMs` (500 ms by default)
  for each reference that it scores: the image and each alternate that loaded. When the limit
  expires, the call fails with `504 detection_timeout`. Until feature 108 the call returned
  `200 {"matches":[],"limitsHit":true}`, and all references had one shared limit, so an image with
  alternates looked absent in some calls only (#223). In a `200`, `limitsHit` is true only when
  `maxResults` cut the list of matches. Sequence conditions, image-anchored taps and
  `POST /api/images/detect-all` do not use this limit.
- `POST /api/images/detect` additionally reports `masked` and `retainedPixelCount` (feature 089) —
  whether the reference image's transparency mask was used, and how many pixels the comparison kept.
  Both fields are additive; `retainedPixelCount` counts pixels **kept**, not pixels masked out.
  `POST /api/images/detect-all` honours masks too but keeps its response shape: a library-wide sweep
  has no single mask state.
- **The two detection routes use different coordinate units under the same field names.**
  `POST /api/images/detect` reports match `x`/`y`/`width`/`height` (repeated under `bbox`) as
  fractions 0..1 of the capture frame's width/height, clamped; `POST /api/images/detect-all` reports
  the same box in whole pixels. The reference image is `templateId` on detect and `imageId` on
  detect-all. This is stated in the OpenAPI document (`ImageDetectCoordinatesSchemaFilter` plus both
  operation descriptions) rather than changed, so existing callers that convert keep working
  (feature 101, issue #188).
- Matching itself is normalised cross-correlation (`TM_CCOEFF_NORMED`) on grayscale. A masked
  template is scored by the same measure restricted to its retained pixels, computed as three
  `TM_CCORR` correlations, so a masked score stays on the same 0..1 scale and an existing threshold
  keeps its meaning. **Unmasked templates still run through the untouched OpenCV call**, which is
  what guarantees no score drift for the images and thresholds already in production.
- Uploading a reference image whose mask retains fewer than 16 pixels is rejected with
  `400 invalid_image`: such a template correlates with almost any patch of screen, and refusing it at
  authoring time is the only point where the operator can act on it.
- **The no-information rule** (feature 090). A normalised correlation is undefined where the thing
  being correlated has no variation, and a masked comparison meets that case routinely: the dimmed
  backdrop a game draws behind a modal is flat. A retained region — of the screen at a candidate
  position, or of the reference image itself — carrying less than **1.0** shade level of standard
  deviation is treated as carrying no information: that position scores `0`, and a reference image
  below the cutoff reports no match at all. The cutoff is fixed and not configurable, is stated on
  the standard deviation rather than on the accumulated variance so it does not change with the
  retained-pixel count, and is the same number `ImageMatchEvaluator` uses to call a template
  constant. A masked score is also clamped into `[-1, 1]` where it is produced.
  This replaced a division that returned `±Infinity` on a flat region, which the boundary clamp
  then reported as a perfect `1.0` — a false positive that armed a tap on modal screens (issue #196).
  A detection suppressed by the rule is reported through the detect log (event `11006`), not in the
  response body, which is unchanged.
- **Alternate reference images** (feature 097, issue #192). A stored image may carry an ordered list
  of up to **8** alternates — other stored images that also count as a match for it, typically
  night-lit crops of daylight art. GET/PUT /api/images/{id}/alternates manage the list (PUT
  replaces it atomically, [] clears it; 400 invalid_alternates with offending ids for a
  self-reference, duplicate, unknown/invalid id or more than 8); GET /api/images/{id}/metadata lists
  them. Every detection that names the image — POST /api/images/detect, wait-for-image steps,
  image-anchored taps, image-match triggers, sequence image conditions (imageVisible, inline
  operands, break conditions) and the readiness gate — scores the image **and** each alternate with
  the same threshold. Matching fans out through ReferenceSetTemplateMatcher (an ITemplateMatcher
  decorator): the union of every reference's matches, ordered by score (ties: primary, then
  alternates in order), de-duplicated by the existing overlap rule and capped at maxResults.
  ImageMatchEvaluator takes the maximum similarity across references. Detect matches carry an
  additive matchedReferenceId; runtime detections log the winning alternate. Alternates are never
  expanded transitively; a deleted alternate is skipped with a warning and reads exists: false.
  **An image without alternates never goes through the decorator**, so its scores are unchanged.
  POST /api/images/detect-all is unaffected. Lighting normalisation was rejected: TM_CCOEFF_NORMED
  already normalises global brightness/contrast, and any change would move every calibrated score.
- **Region-restricted image detection** (feature 130, issue #272). An `imageVisible` condition and a
  `detectionTarget` have the optional field `region`: `{ "x", "y", "width", "height" }`, integers in
  capture pixels. The search runs only inside the region, and the whole image must be inside it.
  Without a region, the code path is the old one. The field is on `imageVisible` in each condition
  position (step, `if`, loop, break, and the children of `all`, `any`, `none`), and on
  `primitiveTap.detectionTarget`, `waitForImage.detectionTarget`, `ensureGameRunning.readinessImage`,
  the command-level `detection`, and the `detectionTarget` of a sequence `waitForImage` payload.
  `PixelRegion` (Domain) holds the one validation rule (`Validate`: x and y 0 or more, width and height
  more than 0, all four present) and the one clip rule (`ClipTo`). Every save path calls `Validate` and
  gives 400 with every invalid field named. A tap target crops in `DetectionCoordinateResolver`
  (both `ResolveCenter` overloads) before the match, then adds the region origin to each box, so a
  stronger match outside the region cannot take a result slot and the tap point is in full-capture
  pixels. A condition crops in `ImageMatchEvaluator.ComputeSimilarity`; `ImageMatchParams.PixelRegion`
  wins over the fraction `Region`. A region past the capture is clipped; an empty or too small clip
  gives "not found" and no error. `Describe` and `DescribeBreakCondition` add `, region=x,y,width,height`
  only when a region is set. OpenAPI lists `region` and the `PixelRegion` schema.
- `Service:Sessions:MaxConcurrentSessions` defaults to **8** (was 3); exceeding it fails a run with a
  message naming the limit and the setting.

### Break & loop execution and the execution-log status vocabulary

Loops (count / while / repeat-until step-loops, and while/repeat-until blocks) may end early via a
**break** — either a discrete break step in the loop body (`SequenceStepType.Break`) or a loop-level
`breakOn` condition on a while block. A break's *own* outcome (feature 066) is reported with a
canonical two-token vocabulary (`GameBot.Domain.Services.BreakOutcomes`), carried in the existing
`StepResult.ActionOutcome`:

- `break` — the break **fired** (unconditional, or its condition/`breakOn` evaluated true). A
  **success**; the loop ends at that point.
- `no_break` — the break **did not fire** (condition false, or the condition/`breakOn` could not be
  evaluated). A distinct, neutral **"No break"** — never `Skipped`, never the red `Failed`. Execution
  continues unchanged and the run's health is not affected: a break-condition (or `breakOn`)
  evaluation error is guarded and treated as `no_break`, so it never fails the run.

A `commandOutcome` condition may also name the state `restarted` (feature 129). A sequence action step
`ensure-game-running` with `forceRestart` true records `restarted`. A condition on `success` also matches
it (`StepOutcomeStates.Matches`), and a condition on `restarted` matches only a restart step.

`ExecutionLogService.MapStepStatus` maps these to node statuses (`break → success`,
`no_break → no_break`, `restarted → success`); the web-ui renders `no_break` as a neutral "No break" badge distinct from
`failure` and `skipped`.

**If steps** (feature 067) record their branch decision *before* the branch steps run:
`StepResult.ActionOutcome` is `then` / `else` (branch taken → node status `success`) or `none`
(no-op → `skipped`), with `ConditionResult` `true`/`false`/`error`. The step-outcome map records
the if step as `success` (branch completed), `skipped` (no branch steps ran), or `failed`
(condition error or branch failure). Detail items carry `stepType: "if"`, mapped to the execution
tree node kind `if` (web-ui grid label "If"). Branch steps log themselves like loop-body steps.

**Sequence step API schema** (`POST/PATCH /api/sequences`): if steps use
`stepType: "If"` with `if: { condition: {...} }`, `body: [...]` (then branch), and optional
`elseBody: [...]` (null/absent = no else; `[]` = present but empty). See
`specs/067-sequence-if-conditions/contracts/sequences-api.md`. The nesting rules the step validator
enforces — a Loop body may hold Action, If and Break steps but not another Loop; an If branch may hold
only Action steps, plus Break when the If sits in a Loop body (no If-inside-If, no Loop); a top-level
Break is rejected — are published on the OpenAPI `SequenceStep` schema and its `body`/`elseBody`
properties by `SequenceNestingRulesSchemaFilter` (feature 099, issue #178). The accepted
`primitiveAction.type` values — `SequenceActionTypes.All`: tap, swipe, key, command, connect-to-game,
WaitForImage, ensure-game-running, go-to-home-screen, ensure-emulator-running, reschedule-self, notify,
matched case-insensitively — are the single list behind the step validator, `ActionPayloadValidationService`
and the OpenAPI `PrimitiveAction.type` enum. `PrimitiveActionSchemaFilter` describes each type's payload fields
on `PrimitiveAction.payload`, and an unsupported type is rejected with 400 whose message appends
`(expected one of ...)` (feature 102, issue #201).

**Loop exit reason** (feature 081): a `Loop` step's `StepResult` carries a structured
`ExitReason { BrokeVia: string?, ExhaustedMaxIterations: bool }` alongside the existing
`LoopIterations`, populated identically across all three loop kinds (count/while/repeat-until) and
correct even when the firing `Break` is nested inside an `If` body within the loop. `BrokeVia` is
the firing `Break` step's own `StepId` (never an enclosing `If`'s), or `null` if none fired.
`ExhaustedMaxIterations` reports whether the loop ran its full configured `MaxIterations` without
any `Break` firing, independent of `ExitOnMaxIterations` (so it stays `true` even when
`ExitOnMaxIterations: false` also fails the loop for that reason). Both are `false`/`null` when the
loop finished its body/condition normally. Purely additive on the existing `/api/sequences/{id}/execute`
response — no separate contract to update: that endpoint returns the domain `SequenceExecutionResult`
directly, so `exitReason: { brokeVia, exhaustedMaxIterations }` is serialized with no DTO in between.
The two causes are mutually exclusive, and a `Break` firing on the same iteration that reaches the
ceiling reports the break (`ExhaustedMaxIterations: false`).

Feature 103 added a second place to read the same value: the loop step's entry in the **persisted
execution log** carries `brokeVia` and `exhaustedMaxIterations` attributes beside the existing
`iterations`. This matters because a queue-driven firing has no caller to answer — its log is the only
record it leaves — so before feature 103 the exit reason was unreadable after the fact for precisely
the runs that matter, inferable only from the prose in `message`. A loop that fails early
(`AddLoopStep` on the early-stop path) still carries no exit reason at all.

**Composite step conditions** (feature 088): `SequenceStepCondition` has a third form alongside
`imageVisible` and `commandOutcome` — a **composite** that owns an ordered list of child conditions
and a combining rule. The rule is the JSON `type` discriminator, so there is one sealed type per
rule: `AllStepCondition` (`all` — true when every child is true), `AnyStepCondition` (`any` — true
when at least one is), and `NoneStepCondition` (`none` — true when none is), all deriving from the
abstract `CompositeStepCondition`. A child may be any condition form, including another composite.
The inherited `negate` applies to the composite's combined result.

Why it exists: a single reference image cannot always identify a screen. Two unrelated dialogs can
draw an identical button at identical coordinates, so one template matches both at score 1.0 and no
threshold separates them; a guard built on that button alone acts on whichever dialog is up. A
composite lets a guard require a second, dialog-unique signal — "this button AND that title", or
"this button AND NOT that other dialog's title".

- **Evaluation** — `SequenceStepConditionEvaluator` (Domain/Services) owns the recursion and is
  shared by both of `SequenceRunner`'s condition sites (per-step guards and loop/break/while/
  repeat-until conditions), so the semantics cannot drift between them. Children are evaluated in
  author order and **short-circuit**: `all` stops at the first false child, `any` and `none` at the
  first true one. The evaluator performs no screen capture of its own, so a composite's children
  resolve against one screen observation from the screen source's capture cache and a two-image
  guard costs no more capture than a one-image guard. A child that cannot be evaluated (missing
  image evaluator, unresolvable `commandOutcome` ref) raises `ConditionEvaluationException` rather
  than answering `false`, preserving the existing "condition error fails the step" behaviour.
- **Limits** — at most 16 children per composite and at most 4 levels of condition nesting
  (`CompositeStepCondition.MaxChildren` / `MaxDepth`); an empty `children` list is rejected. A
  single-child composite is valid. These are enforced by `CompositeConditionValidator` on the save
  path, so violations are **400s** with `$`-rooted paths (`$.children[2].children[0]`) matching the
  convention `ConditionExpression.Validate()` already uses. `FileSequenceRepository` walks composites
  when applying its per-leaf checks, but as a backstop only.
- **Positions** — accepted in all five places a condition appears: step guard, break condition,
  `if` condition, `while` condition and `repeatUntil` condition. The last two were previously never
  validated at all (only a count loop's `Count` was); composites there are now validated, while
  *leaf* conditions in those positions deliberately remain unvalidated so no stored sequence changes
  status.
- **Reporting** — a skip caused by a composite reuses the existing per-step record: `ConditionType`
  carries the rule (`all`/`any`/`none`) and the message names the child that settled it. A composite
  break condition renders as `all(imageVisible(imageId=…), NOT imageVisible(imageId=…))`.
- **API and compatibility** — purely additive. The published schema gains `AllCondition`,
  `AnyCondition`, `NoneCondition` and the shared `CompositeCondition` base with its recursive
  `children` array. The two leaf forms are untouched in C# and in JSON, so stored sequences load,
  evaluate and re-save byte-identically; there is no migration and no version negotiation.
- **Not merged with `ConditionExpression`** — the block-style flow `Condition` step keeps its own
  separate `and`/`or`/`not` tree (`ConditionExpressionDto`, `nodeType` discriminator, ≥2 children per
  node). The two models remain distinct; converging them would change validation behaviour for
  existing flow steps.

**Widened `commandOutcome` `stepRef` scope** (feature 081): `SequenceStepValidationService`
resolves a `commandOutcome` condition's `stepRef` against every step reachable from the sequence
root — root steps plus every nested `Loop.Body` and `If.Body`/`ElseBody`, recursively — using the
sequence's authored (document) order to keep enforcing "must reference a prior step," rather than
only the condition's immediate sibling list as before. `expectedState` additionally accepts
`break`/`no_break` (alongside `success`/`failed`/`skipped`), and `SequenceRunner` now records a
`Break` step's fired/not-fired outcome into its runtime outcome map so such a reference actually
resolves at execution time (previously unset, so even an already-legal same-body reference to a
`Break` step's outcome always failed with "reference unavailable"). See
`specs/081-loop-exit-reason-and-nested-steprefs/contracts/loop-exit-reason-and-stepref-scope.md`.

Run rule for a step that did not execute (feature 117, issue #250, changes the feature 081 rule):
at the start of each run, `SequenceRunner.ExecuteAsync` calls `BreakStepIndex.SeedNoBreakOutcomes`.
This puts the default `no_break` into the run outcome map for each `Break` step of the definition
(root steps, each `Loop.Body`, each `If.Body`/`ElseBody`). Thus a `commandOutcome` reference to a
`Break` that did not run (an `If` branch that did not run, or a loop body that ran zero iterations)
evaluates as `no_break`. A `Break` that ran writes its own outcome over the default, so it keeps
the outcome that it recorded last. A reference to a step that is not a `Break` and that did not
run still fails the referencing step and the run with the "unavailable" error. An unknown
`stepRef` also still fails. An id that names a `Break` and also a step of a different type gets no
default. See `specs/117-break-outcome-untaken-branch/contracts/command-outcome-break-not-run.md`.

**Where the reference rules apply, and the 2026-09-17 retest that established it** (feature 103,
issue #193): feature 081's widening was reported as delivered but never re-measured. Issue #193 asked
for either a confirmation that the whole ceiling was gone — nested `stepRef` in every condition
variant, plus a readable loop exit reason — or a precise statement of what remained. The retest found
both halves delivered in the API and domain and **neither gone end-to-end**, and closed the five gaps
it measured. Current state:

| Where a `commandOutcome` sits | Resolved + ordered? |
| --- | --- |
| Directly on a step guard | Yes (feature 081) |
| Directly on an `If` condition | Yes — **feature 103**; this slot validated a condition's shape and never checked a reference at all, so feature 081's own acceptance scenario for it passed vacuously |
| Directly on a `Break` condition | Yes (feature 081) |
| Directly on a `while`/`repeatUntil` condition | **No** — deliberate, see below |
| Inside an `all`/`any`/`none` composite, any slot the composite is validated in | Yes — **feature 103**; previously checked for non-emptiness only, so a dangling or forward reference saved with 201 and failed the run instead |

`CompositeConditionValidator.Validate` takes the authored-order index and the referencing step's
position as **optional** parameters; a caller that cannot supply them (notably
`FileSequenceRepository`, which validates a condition with no sequence around it, and where a
rejection would surface as a 500) keeps shape-only validation. A nested rejection reuses that
validator's `$`-rooted path convention, so one message format covers every rule and slot.

The `while`/`repeatUntil` exception is inherited from feature 088 research decision **D-006**:
validating *leaf* conditions in those two slots would newly reject sequences that save today. The
resulting asymmetry — a composite-wrapped reference there is checked while a bare one is not — is
deliberate, asserted by test, and published in the OpenAPI `stepRef` description rather than left for
a reader to discover.

Feature 103 also corrected the `If` slot's `expectedState` message, which named
`success|failed|skipped` while validating against all five values — issue #193's half-2 complaint
surviving verbatim as a message years after the behaviour was fixed.

**What remains**, stated so the row can be closed on evidence rather than assumption: the **web
authoring UI offers no way to author a composite condition at all** — its `PerStepConditionType` is
`imageVisible | commandOutcome`, with no `all`/`any`/`none` form. That is absent capability rather
than a client rule contradicting the service, so feature 103 recorded it instead of building it. The
UI's two *stale rules* were fixed: `validatePerStepConditions` now resolves a reference against the
flattened step tree (it previously searched the top-level array only and reported the original
"references unknown prior step" message for a legal nested reference) and accepts all five outcome
states.

The evidence is executable, not prose. `tests/unit/Sequences/ConditionReferenceScopeValidationTests.cs`
covers every variant and slot, including the D-006 boundary; `CompositeConditionContractTests` and
`SequenceLoopExitReasonContractTests` cover the HTTP boundary;
`ExecutionLogsLoopExitReasonContractTests` covers the log record;
`SequenceRunnerLoopTests`/`SequenceRunnerIfTests` cover the exit reason's three states, the
simultaneous break-and-ceiling iteration, and `brokeVia` attribution; and
`src/web-ui/src/lib/__tests__/perStepConditionValidation.spec.ts` covers the editor. Re-run them
rather than trusting this paragraph.

### The `lastRun` step condition (feature 105, #224)

`LastRunStepCondition` (discriminator `lastRun`; fields `sequence` = `self` or a sequence ID,
`status` = `success|failure|cancelled`, exactly one of `since` = `HH:mm` or `within` =
`[d.]hh:mm:ss`, and `negate`) is true when the named sequence has a kept run record in the
**current queue** with that status, and the end time of that run is in the window. The window ends
now. `since` starts it at the most recent real occurrence of that service-local time of day
(`LastRunWindow.SinceStart`, daylight-saving safe); `within` starts it at now minus the duration
(`24:00:00` is 24 hours). It is permitted in every condition slot and as a child of `all`/`any`/`none`.

The queue context reaches the runner through the ambient `SequenceRunContext` (`AsyncLocal`, the same
pattern as the device context and `SequenceTimeLimitScope`). `SequenceExecutionService` pushes it only
for a queue run (`OriginatingQueueId` set, not a dry-run); its delegate calls
`LastRunConditionEvaluator` (store + `TimeProvider`). `SequenceStepConditionEvaluator` answers a
`lastRun` leaf with that delegate, and with `false` when there is no context (ad-hoc run, dry-run).
`LastRunConditionRules` holds the field rules and the strict parsers for both save-time validation
(`CompositeConditionValidator` walks a `lastRun` leaf at the root of all six slots, so each bad field
is a 400 with a `Step '<id>' condition at <path>:` message) and evaluation. The web UI does not know
this type: authors write `lastRun` conditions through the API.

### Queue sequence notifications (feature 120)

A queue can tell the operator when one of its entries ends. This is separate from the failure policy
of feature 087, which acts on failed cycles.

- **Level.** `ExecutionQueue.NotificationLevel` is `none` (default), `failure` or `successAndFailure`.
  It is a normal queue field, so every other queue change keeps it. Only the level route, and a create
  or update request that names a level, write it. The queue file stores the field with the other
  queue fields. A file with no field reads as `none`.
- **Hook.** `QueueExecutionService.RecordRunAsync` is the one place where a queue entry run ends with
  a known status. It hands one `QueueNotificationJob` to `INotificationDispatcher.Enqueue`. A nested
  step and a manual run never reach it. The run of the host stop returns before the hook, so a stop
  of the service sends no message. A queue stop while the service runs gives `cancelled`. The time limit
  (watchdog) gives `failure` for the message, and the statistics keep `Cancelled`.
- **Dispatcher and worker.** `QueueNotificationDispatcher` writes to one unbounded channel and returns
  at once. `QueueNotificationWorker` is the one reader and the only owner of the failure streak state
  (`NotificationStreakState`, in memory, keys are queue ID and sequence ID). It reads the level of the
  queue when it handles a job and applies the state table. It starts one send task for each message
  and does not wait. The send task sends to all enabled targets in parallel with one 30 s limit. Above
  256 run jobs that wait, `Enqueue` drops the new job. Above 64 send tasks that run, the worker drops the
  message. Each drop writes one log line. `ResetStreaks(queueId)` writes a control message to the same
  channel. It is never dropped. The level route calls it after a save of `none`. The delete route calls
  it after a delete.
- **Streaks.** A failure at an allowed level opens a streak. A success after an open streak sends
  `recovered` and closes it, at both levels. Without an open streak, a success sends `success` at
  `successAndFailure` only. `cancelled` sends a message and changes no streak. Streaks are lost when
  the service restarts.
- **Failure dedup (feature 126).** The worker keeps the last message that it started to send to each
  target (`NotificationLastSentState`, in memory). It drops a failure message for a target only when
  that last message is the same (same queue, sequence, status, and text). Any other message that
  reaches the target, such as a success, a `cancelled` message, or an alert, makes the next failure new.
  A message that the level, the exclude option, or the send cap drops does not change the record. A
  failed send still counts as the last message. The records are lost when the service restarts.
- **Excluded sequences (feature 122).** `CommandSequence.ExcludeFromSuccessNotifications` (JSON
  `excludeFromSuccessNotifications`, default false, omitted from the file when false) hides the plain
  `success` message for one sequence, for example a helper that runs often. The worker reads the
  sequence one time for each job, before it applies the state table, so a change applies to the next
  job with no restart. The option hides only `success` that has no open streak. `failure`, `cancelled`
  and `recovered` are still sent. If the sequence read fails, the option is treated as off and the
  worker writes log event 12033. A stored streak is not changed by the option.
- **Targets.** `NotificationTarget` (type, name, enabled, `Settings` map) is stored in
  `data/notifications/targets.json` by `FileNotificationTargetStore`. The store reloads the file when
  the write time or the length changes. A corrupt or empty file keeps the last good list. A write goes
  to a temp file first. `INotificationChannel` is one target type (`Type`, `Fields`, `Validate`,
  `SendAsync`). `TelegramChannel` posts `chat_id` and `text` to the Bot API with no `parse_mode`. It
  makes 2 attempts of 10 s with a 1 s pause, and a 4xx answer is final. The token is never in a reason,
  a log line or a response. The response of a target shows `hasSecret` and `secretHint`. The backup
  archive does not hold the target file.
- **UI.** The Notifications area has a Telegram setup guide, the target list with add, edit, delete and
  "Save and test", and a table with the level of each queue.

### Device liveness (feature 106, #220)

A wedged emulator can answer `adb` but not apply the inputs, or stop its captures. Before feature 106,
the API reported such a device as healthy. Now the API shows the fault.

**Data.** `DeviceLivenessTracker` (`GameBot.Domain/Sessions`, a singleton, always registered) keeps
one record for each session ID behind one lock: the capture-loop state, the time of the last capture
and of the last frame change, the first input after the last frame change, and the last input with
its outcome (`pending`, `completed`, `timed_out`, `failed`, `cancelled`). The capture loop writes the
capture data. It compares the PNG bytes of each capture with the current frame (`SequenceEqual`), and
the first frame of a loop is a change. `SessionManager` writes the input data for each ADB input, in
both input paths. Stub mode records nothing. `StopSession` and the idle eviction remove the record.

**Rules.** `DeviceLivenessEvaluator` is pure. The first rule that applies gives the state:

1. No device serial: `unknown`.
2. The transport check failed: `not_live`, `transport_not_ready`.
3. The last input timed out, or it is pending for longer than `InputTimeoutMs`, and no frame change
   came after its start: `not_live`, `input_timeout`.
4. A capture loop runs, and no capture completed for longer than `CaptureStallLimitMs`:
   `not_live`, `capture_stalled`.
5. A capture loop runs, the frame did not change for longer than `StaleLimitMs`, and the first input
   after the last change is older than `StaleLimitMs`: `not_live`, `no_change_after_input`.
6. A capture loop runs and has a capture: `live`.
7. All other cases: `unknown`. The health call then does one direct capture.

The `stale` flag is separate: a capture loop runs, and the frame did not change for longer than
`StaleLimitMs` or no capture completed for longer than `CaptureStallLimitMs`. A static screen is stale
but live.

**Configuration.** Section `Service:DeviceLiveness` (`DeviceLivenessOptions`, all values in ms; a
value below its minimum is set to the minimum): `StaleLimitMs` 300000, `CaptureStallLimitMs` 60000,
`InputTimeoutMs` 10000, `CaptureTimeoutMs` 10000, `TransportCheckTimeoutMs` 5000,
`QueueGracePeriodMs` 120000, `QueueCheckIntervalMs` 30000. Feature 121 adds `AlertAfterMs`,
`RecoveryStaggerMs` and `RebootReadyTimeoutMs` (see below).

**Session API.** `GET /api/sessions/{id}/health` adds a `liveness` block. `SessionLivenessService`
runs `adb get-state` with `TransportCheckTimeoutMs`, evaluates the data, and, for rule 7, does one
direct capture with `CaptureTimeoutMs`. The worst time is the sum of the two limits. The screenshot and
snapshot responses add `X-Capture-Age-Ms`, `X-Capture-Unchanged-Ms` and `X-Capture-Stale`
(`CaptureHeaders`; CORS exposes them). A direct capture that does not complete in `CaptureTimeoutMs`
gives `504 capture_timeout`. `POST /api/sessions/{id}/inputs` gives each action the limit
`InputTimeoutMs`: a hung action gives `504 device_timeout`, and the service does not send the actions
after it. When the data-only report after the dispatch is `not_live`, the service gives
`503 device_not_live` and reports each result as not dispatched. Sequence inputs get no new time limit.

**ADB processes.** `AdbClient.ExecAsync` and `GetScreenshotPngAsync` register a kill of the `adb`
process tree on the token before the first read. A cancel or a time-out thus stops the process, also
when the read of the synchronous screenshot pipe is blocked. Each capture of the capture loop has the
limit `CaptureTimeoutMs`, so one hung `screencap` does not stop the loop.

**Queue gate.** `QueueExecutionService` evaluates the data-only report one time for each firing
group (the main firing, its before-each-run pass and its every-step pass; a standalone every-step pass
is a group too). It holds the group only for `not_live` with a hard reason (`capture_stalled`,
`input_timeout`, `transport_not_ready`). `no_change_after_input` does not hold, because the inputs of
the firing can clear it. A held firing does not run and does not fail. It stays due:

- A time-of-day or relative timer is not marked fired. A daily retry keeps its attempt number.
- A self-reschedule Timer firing goes back with its original `FireAt` (`RearmTimerFiring`).
- A live schedule goes back with `TryAdd`, so a newer schedule from the API wins.
- Next-cycle-start and once-per-run self-reschedule entries go back into their registers. Their order
  can change.
- A held at-queue-start entry and the at-start entries after it move to the next-cycle-start register
  (`at-queue-start:<index>`), so the run enters the loop and does not end.
- A held once-per-run pass continues later with the first entry that did not run.

After the first hold of a loop iteration, the other due firings of the iteration are held with the
same report (hold-only mode). A held iteration completes no cycle, does no idle pause and does not end
a non-cycling run. It waits `QueueCheckIntervalMs`. Only the first held firing of each sequence in a
fault episode writes one failed sequence entry (`device_not_live: <reason>`, depth 1 under the current
root segment) and one `failure` run in the statistics. Known limit: a time-of-day firing held past
midnight is lost for that day.

**Queue watch.** `QueueLivenessWatch` runs with each queue run (started after the session binds,
stopped before the session stops). Every `QueueCheckIntervalMs` it evaluates the device and updates
`QueueLivenessEpisode`. When the device stays `not_live` (each reason) for longer than
`QueueGracePeriodMs`, it writes one failed `queue` entry, seals one failed cycle
(`QueueCycleLedger.RecordFaultCycle`) and calls the failure policy evaluator, one time for each
episode. `QueueRunHandle.TryMarkPolicyTripped` makes the policy act one time also when the watch and
the run loop call it at the same time. The liveness code never stops the run; a failure policy that
the operator configured can. Since feature 121, the device is repaired only when the queue has
`deviceRecovery` (see below). `QueueDeviceWatchdogService` does not change.
Detection limit: during an idle pause the queue sends no input, so a wedged device whose captures
still complete is found only when the captures stop or after the next firing.

**Alert, recovery and capture gate (feature 121, #261).** The detection rules above do not change.
Three parts are added:

- **Alert.** `QueueLivenessWatch.CheckOnceAsync` sends one "device not live" message when the episode is
  older than `AlertAfterMs` (default 300000). It sends one "device live again" message when the device
  is live after an alert. `QueueLivenessEpisode` keeps the claims (`TryClaimAlert`, `TryClaimLiveAgain`,
  `TryClaimRecoveryFailed`), so each message is sent one time for each episode. The message goes through
  `INotificationDispatcher.SendAlert` to every enabled notification target. It does not need a failure
  policy and it ignores `notificationLevel`. The worker never drops it. The result sets
  `health.lastNotificationAt`, `lastNotificationSucceeded` and `lastNotificationError`.
- **Recovery.** `ExecutionQueue.DeviceRecovery` (`action` `none` or `reboot-instance`, `afterMs`,
  `maxAttempts`, `cooldownMs`; `QueueDeviceRecoveryValidator`) is optional and persisted. When the
  action is `reboot-instance`, the watch starts `QueueDeviceRecoveryRunner` in the background after
  `afterMs`. One attempt has four steps: `IDeviceRecoveryCoordinator.RebootInstanceAsync`, a wait for the
  device (`IEmulatorDeviceProbe`), `QueueExecutionService.RebindSessionAsync` (new session, the run loop
  follows `handle.SessionId`), and a wait for the state `live`. `DeviceRecoveryCoordinator` is the one
  recovery slot of the service. It starts one reboot at a time, at least `RecoveryStaggerMs` apart (default
  180000), and queues of one instance share one reboot. After `maxAttempts` failures, the runner sends
  one "recovery failed" alert and the queue stays Running. `IEmulatorControl.RebootAsync` returns
  `true` only for exit code 0 of `ldconsole.exe reboot`. `health.deviceLiveness` has three more read-only
  members: `alertSent`, `recoveryAttempts` and `recoveryState` (`idle`, `running`, `exhausted`).
- **Capture gate.** `DeviceCaptureGate` (`GameBot.Emulator`) allows at most one unfinished capture for each
  device serial. A capture that times out puts the device in state `Suspect`: the `screencap` process of
  the device can still run, and the old code started a new one after each time-out (960 processes in the
  incident). While the device is `Suspect`, the capture loop waits. Every `CaptureStallLimitMs` it asks
  `adb shell pidof screencap` (`AdbClient.HasRunningScreencapAsync`). No process clears the state. A new
  capture loop for the serial (a session rebind) also clears it. `AdbSessionDirectCapture` returns false at
  once for a `Suspect` device, so a health call adds no process.

Service settings `AlertAfterMs` (300000, minimum 1000), `RecoveryStaggerMs` (180000, minimum 0) and
`RebootReadyTimeoutMs` (180000, minimum 1000) are in the same section `Service:DeviceLiveness`.

### Dry-run / validate-only sequence mode (feature 082)

`dryRun: true` on the per-step `POST /api/sequences` create request and on
`POST /api/sequences/{id}/execute` lets a sequence author check structural
validity or exercise runtime branching without ever touching a real emulator.

- **Create**: runs the same enrichment (`commandId` → `commandName`) and
  full structural/reference validation a real create runs, but never calls
  `ISequenceRepository.CreateAsync`. Success returns `200 OK` with
  `{ valid: true, dryRun: true, errors: [] }` (mirroring the sibling
  `POST /api/sequences/{id}/validate` response shape); failure returns the
  identical `400` error a non-dry-run create would give for the same body.
  Since feature 113 (issue #242), the service reads `dryRun` from the raw body
  for each body shape (per-step, old string-id list and `blocks`), so a create
  dry run never stores a sequence.
- **Body shape and malformed steps** (feature 113, issue #242): when one or
  more items of `steps` is an object, `POST`, `PUT` and `PATCH` read the body
  as the per-step shape (for a body with `blocks`, only when a step object has
  `stepType` or `primitiveAction`). The reader rejects a top-level Action step
  without a `primitiveAction` object with `400`, and each shape error starts
  with `steps[<index>] (stepId '<id>')`. A step with only `commandReference`
  is not a command step: `commandReference` is only a name label, and the
  command id comes from `primitiveAction.payload.commandId`. The old
  string-id shape applies only when each item of `steps` is a string. It
  rejects an item that is not a string and a `parameters` value that is not
  `null`, so the service never stores fewer steps or parameters than the
  request declares. Before, a first step without `stepType` and
  `primitiveAction` selected the old shape, and the service stored the
  sequence with zero steps and no parameters.
- **Update / patch** (feature 091): `dryRun: true` on `PUT` or `PATCH
  /api/sequences/{id}` runs every check a real update runs and returns before
  the version bump and `ISequenceRepository.UpdateAsync`, so the stored
  sequence is untouched. Same envelope on success; the same `400`/`404`/`409`
  a real update would give on failure. Read from the raw request root, so it
  applies to every body shape, legacy ones included. (It used to be silently
  ignored, applying the update for real — issue #177.)
- **Command references must exist** (feature 091): create, update and patch —
  with or without `dryRun` — reject a `command` step at any depth whose payload
  `commandId` names no existing command, with one `400` error per missing id:
  `Command reference '<id>' does not exist (used by: <stepIds>).` On update and
  patch, an id the stored sequence already references is tolerated even if its
  command has since been deleted, so such a sequence (shown as
  `isResolved: false`) can still be re-saved. Backup restore writes through the
  repository and is not affected.
- **Execute**: walks the sequence's real step tree — `Loop`/`If`/`Break`
  control-flow (iteration counting, `exitReason`, branch selection) executes
  exactly as a real run — but every step that would dispatch input to the
  emulator, start/use a session, or read live capture state (primitive tap/
  swipe/key, connect-to-game, ensure-game-running, ensure-emulator-running,
  go-to-home-screen, wait-for-image, reschedule-self, notify, and a command-referencing
  step's inner dispatch) is skipped and reported with a new
  `actionOutcome: "skipped_dry_run"` (`SequenceRunner.DryRunOutcomes`),
  `Status: "Succeeded"` — never tripping a `requireDispatch: true` step's
  miss-check. No session is ever required. An `imageVisible`- or text-sourced
  condition (per-step gate, `If` condition, `Loop` `breakOn`) resolves to
  `false` without reading live capture state, wherever the injected
  `conditionEvaluator` is invoked; this applies even when the referenced image
  no longer exists — unlike a stale `commandId` below, that case is not
  specially detected under `dryRun`.
- **`commandId` existence is still checked**: a command-referencing step's
  `commandId` is looked up against the command repository even under
  `dryRun` — a resolvable one is skipped like any other step, but one that
  does not resolve still fails the step and the run with the same
  "references a missing command" error a real execution reports. This check
  never reaches `CommandExecutor`/the real dispatch machinery either way.
- **Queue execution is unaffected**: `dryRun` is a parameter on
  `ISequenceExecutionService.ExecuteAsync`/`SequenceRunner.ExecuteAsync`
  defaulting to `false`; `QueueExecutionService` never passes it, so every
  scheduled queue run is byte-for-byte unchanged.
- See `specs/082-dry-run-sequences/contracts/dry-run-sequences.md` for the
  full request/response contract and invariants.

### Step-through of a saved sequence (feature 127)

An author runs a saved sequence one step at a time on a game session and watches the emulator. The
author selects any step as the next step. The view shows the history of the step runs. A step-through
changes no queue schedule and no daily record.

- **Stepper** (`GameBot.Domain/Services/StepThrough`): `SequenceStepper` keeps a cursor over the step
  tree and runs one step that is not a container for each call. `StepPath` gives each step a path
  (`2`, `1/body/0`, `3/else/1`), and flattens the tree into rows for the view. `StepperState` holds the
  cursor, the frame stack (open loops and open if branches), the author values, the step outcomes, and
  the history (at most 1,000 entries). The stepper runs a leaf step with
  `SequenceRunner.ExecuteLeafAsync`. A real run uses the same code for the guard, the delay, the
  gate, the wait-for-image, and the action and command dispatch. The helpers for conditions, loop
  checks, and break checks are shared and `internal`. A parity test runs 18 fixtures through the real
  runner and through the stepper. It compares the order of the commands and the order of the condition
  checks.
- **When it evaluates**: the stepper evaluates a loop or an if step when the author runs the next step,
  not before. The cursor can therefore be on a header row. A manual selection into a loop body or an if
  branch opens the frames for all parents at iteration 1. After a step, the cursor goes to the next step
  by the normal order. A failed step does not stop the step-through. A break with no loop does not stop
  it. The history entry of a step that a real run would end the sequence at has the note
  `sequence would end here`.
- **Differences from a real run**: a `lastRun` condition is always false, and the entry says
  `lastRun is not evaluated`. The sequence time limit does not apply. Each step keeps its own timeout.
  The time between steps is not limited. The inter-step delay of the sequence is not applied.
- **No outside effects**: `reschedule-self` and `notify` do not run. The history entry shows the intended
  effect (`would reschedule at 14:30`) and the outcome `previewed`. `ExecutionOptions.PreviewEffects` is an
  optional argument of `ICommandExecutor.ForceExecuteDetailedAsync`. Today no command step type has an
  outside effect, so `PreviewEffectRules` lists each `CommandStepType` as `runs`. A unit test fails for a
  new command step type that is not listed. A contract test fails for a sequence action type that is not
  listed as `runs` or `previews`.
- **Service** (`GameBot.Service/Services/StepThrough`): `StepThroughService` owns the sessions in memory.
  A session has one game session, one version hash (SHA-256 of the stored sequence JSON), a lock, and a
  lease of 90 seconds. Each call renews the lease. A step runs in a background task on a copy of the
  state, and the service adopts the copy when the step ends. The call that starts a step returns `202`.
  `StepThroughLeaseSweeper` checks the leases every 10 seconds. An expired lease cancels the running
  step, resumes a queue that the step-through paused, and removes the session. The wiring is the set of
  command, action, gate, and condition callbacks. It comes from
  `SequenceExecutionService.CreateStepWiring`. A step therefore reaches the device in the same way as
  a step of a real run.
- **Queue safety**: `StepThroughSessionGuard` finds the queue that owns the device with
  `IDeviceClaimRegistry` and `IQueueRunRegistry`. `run-next` answers `409 queue_running` while that queue
  runs and is not paused. The author can pause the queue (`POST .../pause-queue`). The guard uses the
  policy pause of the queue run with the reason `step-through`. It does not pause a queue while a firing
  runs (`409 queue_run_active`). It resumes only a pause with its own reason, so a queue that was paused
  before stays paused. A restart keeps the pause. The end of the step-through resumes the queue, after
  the cancelled step stops.
- **Saved sequences only**: the Sequences page enables **Step through** only for a saved sequence with no
  unsaved edits, and shows the reason otherwise. The service compares the version hash on `run-next` and
  `select` and answers `409 sequence_changed`. A restart takes the new stored version. A flow-graph
  sequence and a sequence with blocks give `400 unsupported_sequence_kind`.
- **Execution log**: each step run writes one sequence entry with the new field `origin: "step-through"`,
  and the commands of the step are its children. `GET /api/execution-logs` accepts `?origin=`. The Web UI
  shows a badge and an origin filter.

### Auto-update (feature 131)

The user clicks **Check for Update** in the **Update** area of the Web UI and confirms. The bot installs the
new version and starts again. Windows shows no SmartScreen and no UAC prompt in this path.

- **Release source**: the `release-installer` workflow publishes a GitHub Release `v<major>.<minor>.<patch>.<build>`
  only when the owner starts it by hand on `master` with `publish_release: true`. A push to `master` creates no
  release. The release has `GameBot.msi`, `GameBotInstaller.exe`, and `update-manifest.json` (version, size,
  SHA-256; made by `scripts/new-update-manifest.ps1`).
- **Check**: `UpdateCheckService` reads `GET /repos/{Update:Repository}/releases/latest` through
  `GitHubReleaseClient` (no token; settings `Update:Repository` and `Update:ApiBaseUrl`). The installed version
  is the `InformationalVersion` of the service assembly without a `+` suffix. `UpdateVersionSelector` offers
  an update only when the release version is greater (no downgrade). A failed check is a result
  (`checkFailed` with a code and a hint), not an HTTP error.
- **Install**: `UpdateCoordinator` is a singleton with one gate. It runs these steps in order:
  1. `UpdateDownloader` downloads the MSI with HttpClient, so the file has no Mark of the Web. It accepts https
     on `github.com` and `*.githubusercontent.com` only. It checks each redirect step.
  2. The downloader checks the SHA-256 of the file.
  3. The coordinator writes `update-result.json` with the state `installing`.
  4. It starts a copy of `GameBot.Updater` in `<data root>\updates\updater`.
  5. It asks the host to stop, then it stops all queues at once. The host stops first, so queues with
     `resumeOnServiceStart` start again after the restart (feature 098).
- **Updater**: the program waits for the bot to exit. It runs `msiexec /i ... /qn /norestart` (per-user MSI with
  `MajorUpgrade`). It starts the new bot and writes the final state. A failed install rolls back through
  Windows Installer, and the old bot starts again.
- **Result**: `UpdateResultReportingService` reads `update-result.json` at start. `lastResult` in
  `GET /api/update/status` shows it.
- **Guards**: an install needs two things. The request must come from the bot PC (`LoopbackGuard` reads the
  socket address, and a header has no effect). The bot must run from the installed folder
  (`InstallLocationGuard` reads the `HKCU\...\Run` value `GameBot`). The status route returns `canInstallHere`
  and `installBlockedReason`. The reason `remote` has priority over `notInstalled`.
- **Limit**: the manifest and the MSI come from the same release. The checksum finds damaged downloads. It gives no
  protection if an attacker controls the release. Only code signing fixes that, and the build is not signed.

## REST API surface

Minimal-API endpoint groups under `src/GameBot.Service/Endpoints/` (all under `/api`):
adb, backup/restore, commands, config (+ files, logging), coverage, emulator-image,
execution-logs, games, image-detections, image-references, metrics, notifications, queues,
queue-templates, sessions, step-through, steps, triggers, update. Plus `SessionsController`. Swagger groups these into sections.

Feature 131 added three routes, tag `Update` (errors use `{ "error": { code, message, hint } }`):

- `GET /api/update/status`: installed version, last check, the active attempt, the last result, `canInstallHere`,
  and `installBlockedReason`.
- `POST /api/update/check`: always `200` (`upToDate`, `updateAvailable`, `checkFailed`), or `409 update_in_progress`.
- `POST /api/update/install` (body `targetVersion`, `confirmStopQueues: true`): `202`, or `403 update_local_only`,
  `409 update_not_installed`, `400 update_confirmation_required`, `400 update_not_available`,
  `409 update_in_progress`, `422 update_disk_space`. Later failures show in the status (`update_download_failed`,
  `update_checksum_mismatch`, `update_install_failed`, `update_restart_failed`).

> Note: `TriggersEndpoints` still exists on the backend even though the Triggers authoring UI was
> removed (spec 020). Treat the API as broader than the current UI.

Feature 078 added, all additively (absent members mean pre-feature behaviour):

- `parameters` on command create/update/response and on sequence upsert/patch/response.
- `fieldTemplates` and `parameterBindings` on a command step; `parameterBindings` on a sequence step.
- `parameterValues` on a queue-template entry save; `parameterValues`, `hasParameterOverrides` and
  `effectiveParameters` on the entry in the template detail response.
- `GET /api/commands/{id}/parameter-scope` and `GET /api/sequences/{id}/parameter-scope` — read-only,
  serving the names an editor may offer (plus, for sequences, each command step's callee
  declarations). Served from the backend so the resolution rules have exactly one implementation.
- `POST /api/sequences/{id}/execute` accepts an optional `parameters` body for an ad-hoc run and
  answers `409 missing_required_parameters` when a required parameter has no value and no default.
- `POST /api/queues/{id}/start` answers `409 missing_required_parameters`, listing the offending
  entries and parameter names, before any session or device work.
- Execution-log step details gain a `parameters` item recording each resolved value and the scope
  layer it came from; a parameter whose *name* looks like a secret has its value masked.

Feature 114 added, all additively:

- Two image keys in a command step `fieldTemplates`: `primitiveTap.detectionTarget.referenceImageId`
  and `waitForImage.detectionTarget.referenceImageId`. The value must be one whole placeholder, else
  `400 invalid_field_template_value`. The message of `unknown_field_template_path` is now "is not a
  parametrizable field."
- A placeholder in `imageVisible.imageId` in each condition position of a sequence.
- `POST`, `PUT` and `PATCH /api/sequences` with a per-step body add a `warnings` member (for example
  `static_check_skipped`) when the parameter check gives warnings. The member is not present
  otherwise, and a dry run does not change.
- `POST /api/queue-templates` answers `400 unknown_image_reference` when a known value goes to an
  image field and no image has that id. The service saves nothing.

Feature 080 fixed three platform bugs, additively/tightening only (no route removed or renamed):

- `POST /api/sequences` and `PUT /api/sequences/{id}` now reject, at creation/replace time, a
  `command`-typed action step (top-level or nested in a `Loop`/`If` body) whose payload has no
  non-empty `commandId` — it used to be accepted silently and default the dispatch target to the
  step's own `stepId`, failing confusingly only when the sequence ran.
- `requireDispatch: true` on a step nested inside a `Loop` or `If` body is now honored — it used to
  be silently dropped in transit (`SequencesEndpoints.MapBodySteps` never copied it), so such a
  step could never fail its enclosing run even when nothing dispatched.
- `POST /api/sessions/{id}/inputs` no longer reports `409 not_running` for a session that is
  actually running just because posted action(s) couldn't be parsed/dispatched. It now reports
  `400 invalid_input_actions` when none of the actions dispatched, or keeps the existing
  `202 Accepted` (extended with a per-action `results` array) when some/all did; `409` is reserved
  for a session that genuinely isn't found/running.

Feature 081 added, additively (see "Loop exit reason" / "Widened `commandOutcome` `stepRef` scope"
above for detail):

- A `Loop` step's execution result gains `exitReason: { brokeVia, exhaustedMaxIterations }`.
- `POST /api/sequences` / `PUT /api/sequences/{id}` now accept a `commandOutcome` condition's
  `stepRef` naming any structurally prior step reachable from the sequence root, not only an
  immediate sibling, and accept `break`/`no_break` as `expectedState` values.

Feature 082 added, additively (see "Dry-run / validate-only sequence mode" above for detail):

- `dryRun` on the per-step `POST /api/sequences` create request: validates without persisting.
- `dryRun` on `POST /api/sequences/{id}/execute`: walks the real step tree without dispatching to
  the emulator, starting a session, or reading live capture state, reporting a new
  `actionOutcome: "skipped_dry_run"` for each step it skips.

Feature 091 fixed, tightening only (see "Dry-run / validate-only sequence mode" above; issue #177):

- `dryRun` on `PUT`/`PATCH /api/sequences/{id}`: validates without persisting (previously ignored).
- `POST`/`PUT`/`PATCH` sequence writes reject a nonexistent command reference with `400`, except an id
  the stored sequence already references.

Feature 113 fixed, tightening only (see "Dry-run / validate-only sequence mode" above; issue #242):

- A sequence write with a malformed object step gets `400` that names the step. Before, `POST`
  stored the sequence with zero steps, and `PUT`/`PATCH` kept the stored steps.
- `dryRun` on `POST /api/sequences` applies to each body shape and never stores a sequence.
- The old string-id create shape rejects an item that is not a string and a non-null `parameters`.

Feature 086 added, additively (see "Queue cycle observability" above):

- A `health` block on `GET /api/queues/{id}`, present only while the queue is Running.
- `GET /api/queues/{id}/cycles?limit=n` — recent cycles newest-first with per-entry outcomes.

Feature 121 added (see "Device liveness" above): `deviceRecovery` (`{ action, afterMs, maxAttempts,
cooldownMs }`) on the queue create and update requests and on the queue responses, copied by
`POST /api/queues/{id}/duplicate`. An absent member on `PUT` clears it. A bad value gives `400`, and
`reboot-instance` without `emulatorInstanceName` gives `400`. The `health.deviceLiveness` block has three
more read-only members: `alertSent`, `recoveryAttempts` and `recoveryState` (`idle` | `running` |
`exhausted`).

Feature 120 added (see "Queue sequence notifications" above): `notificationLevel` on the queue
responses and on the create and update requests, `PUT /api/queues/{id}/notification-level`,
`GET` and `POST /api/notifications/targets`, `PUT` and `DELETE /api/notifications/targets/{id}`,
`POST /api/notifications/targets/{id}/test` and `GET /api/notifications/types`. A bad body gives 400.

Feature 122 added (see "Excluded sequences" above): the boolean member `excludeFromSuccessNotifications`
on the sequence create, update and patch requests and on the sequence get and list responses. An absent
member on `POST` means false. An absent member on `PUT` and `PATCH` keeps the stored value. A value that
is not `true` or `false` gives `400`. The sequence editor has a checkbox, and the list shows a badge.

Feature 087 added, additively (see "Queue failure policy and outbound notification" above):

- `failurePolicy` (`{ consecutiveFailedCycles, action, notifyUrl }`) on queue create/update/response
  and carried by `POST /api/queues/{id}/duplicate`. Absent or null ⇒ no policy, no evaluation, and
  behaviour identical to before the feature. Rejected with 400 naming the offending value when the
  threshold is < 1, the action is outside `notify|stop|pause|notifyAndStop`, `notifyUrl` is not an
  absolute http/https URL, or a notifying action has no destination from either the policy or
  `Service:Notifications:DefaultUrl`.
- Eight fields on the `health` block: `failurePolicyConfigured`, `failurePolicyTripped`, `paused`,
  `pausedAt`, `pauseReason`, `lastNotificationAt`, `lastNotificationSucceeded`,
  `lastNotificationError`. Feature 096 widened `paused`/`pausedAt`/`pauseReason` to cover the idle
  pause too and added `pauseKind` (`idle` | `failurePolicy` | null); their meaning is published on the
  `QueueHealthResponse` schema by `QueueHealthSchemaFilter`.
- `POST /api/queues/{id}/resume` — releases a policy pause. 200 with `resumed: true|false` for every
  known queue (not 409 for "running but not paused"), 404 for an unknown one. Idempotent.
- A `notify` action type on sequence steps (`{ message, url? }`), validated at save time.
- A new `Service:Notifications` configuration section (`DefaultUrl`, `AuthHeaderName`,
  `AuthHeaderValue`, `TimeoutSeconds`, `MaxAttempts`). `AuthHeaderValue` is a secret and is never
  returned by any endpoint nor written to a log.
- An **outbound** contract: `POST <configured url>` with a versioned JSON payload
  (`schemaVersion: 1`). Documented in
  `specs/087-queue-failure-policy/contracts/notification-payload.md` — a receiver is written against
  it, so it is a published contract, not an internal shape.

Feature 098 added, additively (see "Resume after a service restart" above):

- `resumeOnServiceStart` (bool) on queue create/update/list/detail responses, carried by
  `POST /api/queues/{id}/duplicate`. Absent ⇒ `false`.
- A new persisted file, `<data>/queue-run-state.json`, recording the ids of queues with a live run.

Feature 105 added, additively (see "Sequence run statistics" and "The `lastRun` step condition" above):

- `sequenceStats` on every queue detail response (`GET /api/queues/{id}`, `PUT .../entries`,
  `PUT .../template`, `PUT .../game`): an object keyed by sequence ID (ordinal order), `{}` when the
  queue has no recorded run. Each value is `QueueSequenceStatsResponse` (`sequenceName`,
  `lastRunStartedAt`, `lastRunEndedAt`, `lastRunStatus`, `lastSuccessAt`, `successCount`,
  `failureCount`, `cancelledCount`), described by `QueueSequenceStatsSchemaFilter`. The list, monitor
  and cycles reads do not change, and a duplicate does not copy the statistics.
- The `lastRun` step condition on sequence create/update/PATCH/`dryRun`, with the schema
  `LastRunCondition` described by `LastRunConditionSchemaFilter`.
- A new persisted folder, `<data>/queue-sequence-stats/`, with one `<queueId>.json` for each queue.

Feature 106 added and changed (see "Device liveness" above; issue #220):

- `liveness` on `GET /api/sessions/{id}/health` (`state`, `reason`, `frameAgeMs`, `unchangedMs`,
  `stale`, `lastInputAt`, `lastInputOutcome`), described by `DeviceLivenessSchemaFilter`.
- `X-Capture-Age-Ms`, `X-Capture-Unchanged-Ms` and `X-Capture-Stale` on `GET /api/emulator/screenshot`
  and, when the session has capture data, on `GET /api/sessions/{id}/snapshot`.
- `504 capture_timeout` on the screenshot and snapshot endpoints.
- `503 device_not_live` and `504 device_timeout` on `POST /api/sessions/{id}/inputs`.
- `health.deviceLiveness` on `GET /api/queues/{id}` (`state`, `reason`, `notLiveSince`, `stale`,
  `frameAgeMs`, `unchangedMs`, `gatedFirings`).
- A new configuration section, `Service:DeviceLiveness`. No new persisted file.

Feature 127 added, additively (see "Step-through of a saved sequence" above):

- Routes under `/api/step-through`:
  - `POST /api/step-through` starts a step-through (`201`).
  - `GET /api/step-through/{id}` reads it and renews the lease. It has the optional `?afterSeq=`.
  - `POST .../run-next` runs the next step (`202`).
  - `POST .../select` sets the next step.
  - `POST .../cancel` cancels the step (`202` when a step ran, else `200`).
  - `POST .../restart`, `PUT .../values`, and `POST .../pause-queue`.
  - `DELETE .../{id}` ends it (`204`, idempotent).
- An error has the usual envelope
  `{ error: { code, message, hint, details } }`. The codes are `sequence_not_found`,
  `step_through_not_found`, `unsupported_sequence_kind`, `sequence_empty`, `unknown_step`,
  `not_selectable`, `unknown_parameter`, `session_unavailable`, `session_in_use`, `step_running`,
  `sequence_complete`, `sequence_changed`, `queue_running` (with `queueId`, `queueName`, `canPause` in
  `details`), `no_owning_queue`, and `queue_run_active`.
- The member `origin` on the execution log entry (`step-through` or absent), and the query parameter
  `origin` on `GET /api/execution-logs`.
- No new persisted file. A step-through lives in memory and ends with the view or the lease.

## Legacy / removed (don't be misled by old specs)

- **Actions** as a first-class data model were **removed (spec 039)** and replaced with **Primitive
  Actions**. Old specs (017, 021, 028, parts of 016) describe the former model.
- **Triggers UI** was **deleted (spec 020)**. Trigger *evaluation* lives on internally; the API
  endpoints remain. There is no trigger-authoring page.
- **Orphaned dead code** (present but not routed, deletion candidates):
  `web-ui/src/pages/TriggersPage.tsx`, `web-ui/src/services/triggers.ts`,
  `web-ui/src/components/TriggerPicker.tsx`.

## Where to look next

- Current behaviour of a feature → this file, then the relevant code under `src/`.
- Why a feature was built a certain way → its `specs/NNN-*/spec.md` (check the `Status` line first;
  see [`specs/STATUS.md`](../specs/STATUS.md)).
- Quality gates and the upkeep rules for this document → `.specify/memory/constitution.md`.
