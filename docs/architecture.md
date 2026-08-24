# GameBot Architecture & Capability Map

**This document describes the system as it is *now*. It is living documentation and MUST be kept
current** (see the project constitution, *Living Documentation* principle). When a feature changes
the domain model, the capability set, the API surface, or the persistence layout, update this file
in the same PR.

For the *history* of how the system got here — one folder per feature, point-in-time — see
[`specs/`](../specs/) and its roll-up [`specs/STATUS.md`](../specs/STATUS.md). Specs are immutable
history; this file is the current-state source of truth. When the two disagree, this file wins and
the relevant spec should be marked superseded.

_Last reviewed: 2026-08-24._

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
  disk-backed under `data/`.
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
  `imageVisible`/`commandOutcome` with negation, `IfConfig`), a then branch (reuses
  `SequenceStep.Body`), and an optional else branch (`SequenceStep.ElseBody`; `null` = absent).
  If blocks may sit at the sequence top level or inside loop bodies; branches are flat (no loops,
  no nested ifs; breaks only when the if is inside a loop, where a branch break exits the
  enclosing loop). The condition is evaluated once per encounter; a condition error fails the
  step and sequence exactly like a while-loop condition error.
- **Queue** — bound to exactly one emulator; holds ordered **entries** (sequences) and a
  cycle-execution flag. Runs entries against the emulator; can cycle.
- **Queue Template** — a named, persisted snapshot of a queue's ordered entries and their
  **schedules**. A queue links to 0..1 templates (auto-loaded when the queue opens); a template can
  be shared across queues.
- **Sequence schedule** (within a template) — how/when an entry runs in a queue cycle:
  *Once per run*, *At queue start*, *After every step*, and *Scheduled* (absolute or relative time).
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
  accumulate, and *After Every Step* is idempotent per sequence.
- **Queue monitor** — a read-only "live plan" view of a *running* queue. `GET /api/queues/{id}/monitor`
  returns a pure projection (`IQueueMonitorService`) that folds the active `QueueRunHandle` (the
  sequence-level "now" indicator plus pending live schedules and self-reschedule firings) and the
  linked template with the current local clock into the sequence running **now** and the ordered
  **up-next** list — each with a schedule reason and a best-effort expected time (exact for
  live/self-reschedule/relative; next-eligible for time-of-day timers). Nothing is persisted; the
  snapshot is computed per request. When the queue is not running the endpoint returns
  `running:false` with the best-effort last outcome from the execution log. The web UI opens this
  monitor (polling ~2.5s) in place of the entry editor while a queue is Running (feature 072).
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
  never reads as hung. Disabled queues are byte-for-byte unchanged.
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
  command → primitive action) with outcomes, timings, detections, and condition traces.

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
- **Execution Logs** (separate tab): filterable/sortable grid, expandable hierarchy reflecting
  what actually executed, deep links into authoring, snapshots and step outcomes; non-technical
  presentation (no raw JSON).
- **Configuration**: dynamic backend-driven config editor (drag-and-drop reorder, filtering),
  runtime per-component logging level control, jitter/retry/delay parameters.
- **Packaging**: standalone Windows installer (EXE/MSI) with semantic-version upgrade flow
  (build auto-versioning, downgrade prohibition).

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
