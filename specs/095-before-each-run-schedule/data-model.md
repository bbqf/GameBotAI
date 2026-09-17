# Data Model: Before-Each-Run Schedule Type

## ScheduleType (domain enum, persisted by name)

| Value | Int | Operator label | Change |
|-------|-----|----------------|--------|
| OncePerRun | 0 | Once per run | unchanged |
| EveryStep | 1 | After Every Step | unchanged |
| Timer | 2 | Scheduled | unchanged |
| AtQueueStart | 3 | At Queue Start | unchanged |
| **BeforeEachRun** | **4** | **Before Each Run** | **new** |

`QueueTemplateEntry` is otherwise unchanged. For `BeforeEachRun`, `TimerTimeOfDay` and `TimerRelativeOffset` are null (the save endpoint only reads timer fields for `Timer`). `Enabled` and `ParameterValues` apply as for every type (entry-scope parameters layer on the queue scope).

## ScheduleKind (monitor, serialized as string)

Adds `BeforeEachRun` — reason "Before Each Run", `expectedAt` null, `repeats` false, listed once per enabled entry after the timed items and before the every-step items.

## Run-loop state (ephemeral, per `RunAsync`)

| Name | Type | Lifecycle |
|------|------|-----------|
| `beforeEachRunEntries` | `List<QueueTemplateEntry>` | Enabled BeforeEachRun entries in template order; snapshot at run start |
| `beforeEachRunRanThisIteration` | `bool` | Reset to false at the top of each loop iteration; set true when the pass starts |

### Pass state transition per iteration

```
iteration start → flag=false
triggering firing about to run:
  flag==false && entries>0 → flag=true → run entries in order (each: cancel check, connection check, run, failed++ if !ok, RecordEntry) → run firing
  otherwise → run firing
non-triggering firing (template OncePerRun, every-step) → no pass
```

## Web-ui

- `ScheduleType` TS union gains `'BeforeEachRun'`; `ScheduleKind` TS union gains `'BeforeEachRun'`.
- `SchedulingAreaId` gains `'beforeEachRun'` (label "Before each run"), mapped both ways to `BeforeEachRun`.
