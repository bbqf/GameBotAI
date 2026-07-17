# GameBot Architecture & Capability Map

**This document describes the system as it is *now*. It is living documentation and MUST be kept
current** (see the project constitution, *Living Documentation* principle). When a feature changes
the domain model, the capability set, the API surface, or the persistence layout, update this file
in the same PR.

For the *history* of how the system got here — one folder per feature, point-in-time — see
[`specs/`](../specs/) and its roll-up [`specs/STATUS.md`](../specs/STATUS.md). Specs are immutable
history; this file is the current-state source of truth. When the two disagree, this file wins and
the relevant spec should be marked superseded.

_Last reviewed: 2026-07-17._

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

- **Game** — a target app (package) the bot can connect to.
- **Image (reference image)** — a stored bitmap used as a template for on-screen detection;
  disk-backed under `data/`.
- **Command** — an ordered list of **steps**. Steps are **primitive actions** plus control
  structures (loops, per-step conditions). A command may carry a vestigial `TriggerId`.
- **Primitive Action** — the unit of input/effect. Current variants: **Tap**, **Swipe**,
  **Key**, **Wait for Image**, **Connect to Game**, **Ensure Game Running**, **Go to Home Screen**.
  (These replaced the old first-class "Action" object — see *Legacy/removed* below.) Taps/swipes
  resolve coordinates from image detection + offset, with wait-and-retry and tap-point jitter
  applied automatically. **Go to Home Screen** (`go-to-home-screen`, feature 069) is a parameterless
  action that presses Android HOME (keycode 3) so the device returns to its home/main screen,
  leaving the game running in the background — the leave-game counterpart to Connect to Game. It is
  usable both as a sequence action (dispatched through the session input pipeline) and a command
  step, and degrades to a stub success on non-Windows/non-ADB sessions.
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
- **Self-reschedule action** (within a sequence) — an authorable sequence action (`reschedule-self`,
  placeable under IF/conditional flow) that, when reached during a queue-driven run, schedules **one
  additional firing of the same sequence into the current run** using any of the schedule options
  above (At Queue Start / Once Per Run / Timer / After Every Step). It is **ephemeral** (current run
  only, never persisted) and a **success no-op** when the sequence was not started from a queue. The
  run's active-run state lives in a singleton `IQueueRunRegistry`; an `ISelfRescheduleCoordinator`
  injects the ephemeral firing, which the queue run loop drains at the matching boundary.
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
  schedule option, IF-gated), a background screen-capture service reporting FPS, and "ensure game
  running" handling.
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
