# Phase 1 Data Model: Queue-Start and After-Every-Step Scheduling

**Feature**: 060-queue-start-after-every-scheduling
**Date**: 2026-06-18

This feature adds one enumeration value and renames one option's display label. There are no new entities, no new fields, and no persistence-format change.

## Entity: `ScheduleType` (enum) — extended

`GameBot.Domain.QueueTemplates.ScheduleType` governs when a `QueueTemplateEntry` executes during a run. Serialized as its string name via `JsonStringEnumConverter`.

| Value | Numeric | Persisted/API identifier | Display label | Behavior |
|-------|---------|--------------------------|---------------|----------|
| `OncePerRun` | 0 | `OncePerRun` | Once Per Run | Default. Normal step; runs in template order; defines run/cycle completion. Counts toward `executed`. (unchanged) |
| `EveryStep` | 1 | `EveryStep` | **After Every Step** *(renamed label)* | Runs after each `OncePerRun` step and once after the final step; never self-triggers; does NOT count toward `executed`. **Behavior and identifier unchanged.** |
| `Timer` | 2 | `Timer` | Timer | Evaluated at iteration boundaries; time-of-day or relative-offset / live (feature 059). (unchanged) |
| `AtQueueStart` | 3 | `AtQueueStart` | At Queue Start | **NEW.** Runs once per run, in template order, before the run loop (before timer evaluation and the first `OncePerRun` step). Counts toward `executed`. Failure is non-fatal. Not repeated per cycle. |

**Notes**
- Adding `AtQueueStart = 3` is additive; existing serialized templates contain only `OncePerRun`/`EveryStep`/`Timer` and deserialize unchanged.
- The `EveryStep` change is **display label only** — the enum name, numeric value, persisted string, and API request/response string all remain `EveryStep`. No migration.

## Entity: `QueueTemplateEntry` — unchanged shape

`GameBot.Domain.QueueTemplates.QueueTemplateEntry` is unchanged structurally:

| Field | Type | Relevance to this feature |
|-------|------|---------------------------|
| `SequenceId` | `string` | unchanged |
| `ScheduleType` | `ScheduleType` | may now be `AtQueueStart` |
| `TimerTimeOfDay` | `TimeOnly?` | null for `AtQueueStart` (timer-only) |
| `TimerRelativeOffset` | `TimeSpan?` | null for `AtQueueStart` (timer-only) |

**Validation rules** (enforced at the API layer, `QueueTemplatesEndpoints`):
- `ScheduleType` must parse to a defined `ScheduleType` value (now includes `AtQueueStart`); otherwise 400 with the `{ error: { code, message, hint } }` envelope. The "accepted values" message is updated to list `AtQueueStart`.
- `TimerTimeOfDay` / `TimerRelativeOffset` validation applies only when `ScheduleType == Timer`; `AtQueueStart` entries ignore both (unchanged logic — the existing code only inspects timer fields under the `Timer` branch).
- No additional fields are required for `AtQueueStart`.

## Run-time state (no schema change)

`QueueExecutionService.RunAsync` partitions template entries by schedule type at run start. This feature adds an `atQueueStartEntries` partition and a one-time pre-pass that runs them before the iteration loop. This is in-memory run state only; nothing is persisted beyond the existing execution-log entries written per sequence run.

| Run-state element | Lifetime | Purpose |
|-------------------|----------|---------|
| `atQueueStartEntries` (local list) | per run | template-ordered `AtQueueStart` entries, executed once before the loop |
| `executed` counter | per run | now incremented for each at-queue-start firing (FR-015) |
| `failed` counter | per run | incremented on a non-fatal at-queue-start failure (FR-007) |
