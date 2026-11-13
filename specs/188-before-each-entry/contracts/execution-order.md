# Contract: Execution Order of the Before Each Run Pass

This contract is the behaviour that the unit tests check. The API shape does not change.

## Symbols

- `B`, `B1`, `B2`: Before Each Run entries, in template order.
- `T1`, `T2`, `T3`: triggering firings that are due in one wake-up.
- `E`: an After Every Step entry.
- `A`: a template OncePerRun step.

## Rules

| Template and wake-up | Execution order |
|----------------------|-----------------|
| `B`, three co-due timers `T1`, `T2`, `T3` | `B, T1, B, T2, B, T3` |
| `B1`, `B2`, two co-due timers `T1`, `T2` | `B1, B2, T1, B1, B2, T2` |
| `B`, `E`, two co-due timers `T1`, `T2` | `B, T1, E, B, T2, E` |
| `B`, one timer `T` | `B, T` (no change) |
| `B`, `A` only (no triggering firing) | `A` (no change) |
| `B`, `E`, timer `T`, liveness gate holds | nothing runs (no change) |

## Counts

- Executed total: counts the triggering firings only.
- Failed total: counts each failed Before Each Run execution, one time for each pass.

## Run log

- Each Before Each Run execution is its own sequence node under the run root, immediately before the node of its triggering firing.

## API descriptions

- `TemplateEntrySaveRequest.ScheduleType` and `QueueTemplateDetailResponse` entry `ScheduleType`: the description of `"BeforeEachRun"` says that the entries run immediately before each timed, live-scheduled, or self-rescheduled firing. It does not say "at most once per scheduler wake-up".
