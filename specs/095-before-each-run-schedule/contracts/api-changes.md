# API Changes: Before-Each-Run Schedule Type

All changes are additive.

## PUT/POST queue template save (`TemplateEntrySaveRequest.scheduleType`)

- Accepted values: `OncePerRun` (default), `EveryStep`, `Timer`, `AtQueueStart`, **`BeforeEachRun`** (case-insensitive).
- `BeforeEachRun` requires no timer fields; any supplied timer fields are ignored, as for other non-Timer types.
- Invalid value → `400 invalid_request`, message:
  `entries[i].scheduleType '<value>' is not valid; accepted values: OncePerRun, EveryStep, Timer, AtQueueStart, BeforeEachRun`

## GET queue template detail (`QueueTemplateDetailResponse` entry `scheduleType`)

- May return `BeforeEachRun`. XML doc: runs immediately before each timed, live-scheduled or self-rescheduled firing, at most once per scheduler wake-up; displayed as "Before Each Run".

## GET queue monitor (`QueueMonitorResponse` item `scheduleKind`)

- May return `BeforeEachRun` with `reason: "Before Each Run"`, `expectedAt: null`, `repeats: false`.

## Execution log

- No shape change. Each Before Each Run execution is logged as a normal sequence execution under the queue run root.
