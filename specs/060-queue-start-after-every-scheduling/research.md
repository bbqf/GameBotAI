# Phase 0 Research: Queue-Start and After-Every-Step Scheduling

**Feature**: 060-queue-start-after-every-scheduling
**Date**: 2026-06-18

The Technical Context has no open `NEEDS CLARIFICATION` items — the three design decisions were resolved in the spec's clarification session. This document records the design decisions and the existing patterns this feature reuses.

## Decision 1 — Model "At Queue Start" as a new `ScheduleType` enum value

- **Decision**: Add `AtQueueStart = 3` to `GameBot.Domain.QueueTemplates.ScheduleType`. No new domain property is needed (unlike feature 059, which needed `TimerRelativeOffset`); the schedule type alone fully determines the behavior.
- **Rationale**: The schedule option is already a single enum carried on `QueueTemplateEntry`. A new sibling value is the smallest, most consistent extension and serializes additively via the existing `JsonStringEnumConverter`. Existing templates (no `AtQueueStart` entries) are unaffected; the default remains `OncePerRun`.
- **Alternatives considered**:
  - A separate boolean/flag on the entry — rejected: redundant with the existing schedule-type discriminator and would complicate validation and UI.
  - A template-level "startup sequence list" — rejected: breaks the uniform per-entry scheduling model and the template editor's per-entry control.

## Decision 2 — Execute at-queue-start entries in a one-time pre-pass before the run loop

- **Decision**: In `QueueExecutionService.RunAsync`, after the emulator session is established and before the `do/while` iteration loop, run all `AtQueueStart` entries once, in template order, via the existing `RunOneSequenceAsync`. Increment `executed` per firing; on failure increment `failed` and continue; honor cancellation and the mid-run connection-lost check exactly as the other passes do.
- **Rationale**: "Before any timer evaluation and before the first `OncePerRun` step" (FR-003/FR-014) maps cleanly to "before the loop", since timer evaluation (step a) and once-per-run steps (step b) both live inside the loop. Placing it before the loop also gives "once per run, not per cycle" (FR-004) for free, because the loop is what cycles. Reusing `RunOneSequenceAsync` inherits non-fatal failure handling, execution-log parenting, and the `++index` ordering.
- **Counting**: at-queue-start firings call `executed++` (FR-015), matching `OncePerRun` and relative/live firings, and unlike `EveryStep`/time-of-day timers.
- **Edge — only at-queue-start entries**: the existing loop is guarded by `if (oncePerRunEntries.Count > 0 || everyStepEntries.Count > 0 || timerEntries.Count > 0)`. The at-queue-start pre-pass runs independently of that guard. When only at-queue-start entries exist, they run once, the loop is skipped, and the run completes (`cycles = 1`, as in the empty-template path). No busy-loop on cycling, since there is no per-cycle work.
- **Alternatives considered**:
  - Adding at-queue-start as the first thing *inside* the loop guarded by an `oncePerRunDone`-style flag — rejected: more state and easy to accidentally re-run on cycles; the pre-pass is simpler and self-evidently once-per-run.

## Decision 3 — "After Every Step" is a display-label-only rename of `EveryStep`

- **Decision**: Keep the enum value, persisted value, and API request/response identifier as `EveryStep`. Change only the operator-facing label to "After Every Step" in the UI (`SCHEDULE_LABELS` map and the badge text). Add an XML-doc note on `ScheduleType.EveryStep` that it is displayed as "After Every Step".
- **Rationale**: Per clarification (Q3 = display label only), this is fully backward compatible: previously-saved templates, the file store, and any API/script clients keep using `EveryStep` with zero migration. Behavior is unchanged (FR-005/FR-006/FR-010). The UI is the only place an operator sees the label, and its dropdown/badges are data-driven from a single map, so the rename is a one-line label edit plus the badge string.
- **Alternatives considered**:
  - Renaming the enum/identifier to `AfterEveryStep` with a compatibility shim — rejected per clarification; adds migration/shim surface for no functional gain.
  - Hard rename with no shim — rejected per clarification; would break existing templates and clients.

## Decision 4 — No new API endpoint; reuse existing template save/read

- **Decision**: Both options flow through the existing `POST /api/queue-templates` and `GET /api/queue-templates/{id}`. `AtQueueStart` is accepted automatically once added to the enum, because validation uses `Enum.TryParse(..., ignoreCase: true)` + `Enum.IsDefined`. Only the human-readable "accepted values" message in `QueueTemplatesEndpoints` is updated to include `AtQueueStart`.
- **Rationale**: Scheduling configuration is a property of a template entry; it is created/edited through the template save path that already exists. No runtime/live channel is needed (unlike feature 059's live-schedule endpoint), because at-queue-start is purely a saved, declarative property.
- **Alternatives considered**: A dedicated endpoint — rejected: unnecessary surface; inconsistent with how `OncePerRun`/`EveryStep`/`Timer` are configured.

## Determinism / testing note

The existing `QueueExecutionService` already uses an injectable `TimeProvider` (feature 059), so at-queue-start ordering relative to timers is unit-testable with a fake clock. At-queue-start itself involves no time reads — it runs unconditionally at run start — so its tests assert ordering and counts via the execution-log/sequence-execution spy already used by `QueueExecutionServiceTests`.
