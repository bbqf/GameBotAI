# GameBot Architecture & Capability Map

**This document describes the system as it is *now*. It is living documentation and MUST be kept
current** (see the project constitution, *Living Documentation* principle). When a feature changes
the domain model, the capability set, the API surface, or the persistence layout, update this file
in the same PR.

For the *history* of how the system got here — one folder per feature, point-in-time — see
[`specs/`](../specs/) and its roll-up [`specs/STATUS.md`](../specs/STATUS.md). Specs are immutable
history; this file is the current-state source of truth. When the two disagree, this file wins and
the relevant spec should be marked superseded.

_Last reviewed: 2026-09-17 (feature 096 idle pause reported in queue health)._

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
| `src/web-ui` | React + TypeScript + Vite SPA. Authoring, Execution, Execution Logs, Queues, Configuration. |

Persistence is **file-based** under the `data/` directory (JSON documents + stored image files);
there is no database. Queue *runtime* state (loaded entries, running status) is in-memory and does
not survive a service restart; queue *configuration* and templates are persisted.

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
  applied automatically. **Go to Home Screen** (`go-to-home-screen`, feature 069) is a parameterless
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
  step and sequence exactly like a while-loop condition error.
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
- **Self-reschedule action** (within a sequence) — an authorable sequence action (`reschedule-self`,
  placeable under IF/conditional flow) that, when reached during a queue-driven run, schedules **one
  additional firing of the same sequence into the current run** using any of the schedule options
  above (At Queue Start / Once Per Run / Timer / After Every Step). It is **ephemeral** (current run
  only, never persisted) and a **success no-op** when the sequence was not started from a queue. The
  run's active-run state lives in a singleton `IQueueRunRegistry`; an `ISelfRescheduleCoordinator`
  injects the ephemeral firing, which the queue run loop drains at the matching boundary. The **Timer**
  option is **most-recent-wins per sequence** (feature 075): a new Timer firing replaces any pending
  Timer firing already queued for the same sequence in that run, so a self-rescheduling sequence never
  stacks duplicate future firings. The other options are unchanged — *Once Per Run* / *At Queue Start*
  accumulate, and *After Every Step* is idempotent per sequence. A pending booking (or pending live
  schedule) keeps the run's scheduling loop going **whatever schedule types the template uses** —
  including a template of only At Queue Start entries, whose start pass would otherwise end the run
  before its bookings fire (feature 092).
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
  it returns `409 ambiguous_session` instead of an arbitrary device.
- `POST /api/images/detect` takes `captureId` or `sessionId` (mutually exclusive; blank counts as
  absent) and follows the same rule, reusing the same codes: `409 ambiguous_session` when several
  sessions are running and none is named, `404 capture_not_found` / `404 session_not_found` for an
  unresolvable named target, `503 emulator_unavailable` when no screen is obtainable at all. Until
  feature 085 it answered every one of these with `200 {"matches":[]}` — a fabricated "absent"
  indistinguishable from a real one, which silently disarmed absence probes as soon as a second
  emulator was started. **A 200 from this route now means a measurement was actually taken.** The
  unresolved case is detected from the screen source returning null, never from counting sessions
  first: stub hosts serve a fixed bitmap with zero sessions, so a pre-emptive count would break them.
- `POST /api/images/detect` additionally reports `masked` and `retainedPixelCount` (feature 089) —
  whether the reference image's transparency mask was used, and how many pixels the comparison kept.
  Both fields are additive; `retainedPixelCount` counts pixels **kept**, not pixels masked out.
  `POST /api/images/detect-all` honours masks too but keeps its response shape: a library-wide sweep
  has no single mask state.
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

`ExecutionLogService.MapStepStatus` maps these to node statuses (`break → success`,
`no_break → no_break`); the web-ui renders `no_break` as a neutral "No break" badge distinct from
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
`specs/067-sequence-if-conditions/contracts/sequences-api.md`.

**Loop exit reason** (feature 081): a `Loop` step's `StepResult` carries a structured
`ExitReason { BrokeVia: string?, ExhaustedMaxIterations: bool }` alongside the existing
`LoopIterations`, populated identically across all three loop kinds (count/while/repeat-until) and
correct even when the firing `Break` is nested inside an `If` body within the loop. `BrokeVia` is
the firing `Break` step's own `StepId` (never an enclosing `If`'s), or `null` if none fired.
`ExhaustedMaxIterations` reports whether the loop ran its full configured `MaxIterations` without
any `Break` firing, independent of `ExitOnMaxIterations` (so it stays `true` even when
`ExitOnMaxIterations: false` also fails the loop for that reason). Both are `false`/`null` when the
loop finished its body/condition normally. Purely additive on the existing `/api/sequences/{id}/execute`
response — no separate contract to update.

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
`Break` step's outcome always failed with "reference unavailable"). A reference that is now
validation-legal (reachable + prior) but names a step that did not execute during a given run (an
`If` branch not taken, a loop body that ran zero iterations) still fails the referencing step and
the run with that same "unavailable" error — unchanged, deliberately not softened into a silent
skip. See `specs/081-loop-exit-reason-and-nested-steprefs/contracts/loop-exit-reason-and-stepref-scope.md`.

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
  Only recognized on the per-step request shape.
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

## REST API surface

Minimal-API endpoint groups under `src/GameBot.Service/Endpoints/` (all under `/api`):
adb, backup/restore, commands, config (+ files, logging), coverage, emulator-image,
execution-logs, games, image-detections, image-references, metrics, queues, queue-templates,
sessions, steps, triggers. Plus `SessionsController`. Swagger groups these into sections.

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

Feature 086 added, additively (see "Queue cycle observability" above):

- A `health` block on `GET /api/queues/{id}`, present only while the queue is Running.
- `GET /api/queues/{id}/cycles?limit=n` — recent cycles newest-first with per-entry outcomes.

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
