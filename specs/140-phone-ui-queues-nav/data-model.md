# Data Model: Phone UI Queue Activity and Navigation

This feature adds no stored data and no service type. All types below are web UI types. They are derived from existing service data.

## PhoneSupportLevel (changed)

`'full' | 'reduced' | 'desktop-only' | 'hidden'`

- `hidden`: the area is not part of the phone UI. No list shows it. A phone request for it shows the Queues area.
- The `execution` area has level `hidden`. Other levels stay as they are.

## NavigationPosition (new)

One position in the application. It is the state saved in a history entry.

| Field | Type | Rule |
|-------|------|------|
| `area` | `NavigationAreaId` | Never a `hidden` area in phone mode |
| `queueId` | `string \| null` | Set only when `area` is `queues` and a detail is open |
| `idx` | `number` | 0 for the first entry of the session. Each in-app push adds 1. |

Address form: `?area=<area>[&queue=<queueId>]` (the desktop also keeps `tab`).

State transitions:

- Tab bar tap to another area: push `{area, queueId: null, idx+1}`.
- Open a queue: push `{area: 'queues', queueId, idx+1}`.
- `popstate`: set the position from the entry. No push.
- On-screen back from a detail: `history.back()` if `idx > 0`, else `replaceState` to `{area: 'queues', queueId: null, idx: 0}`.
- Redirect (hidden area, unknown area): `replaceState`. No new entry.

## QueueActivity (new, derived)

What a queue does now. Built from `QueueDto`, `QueueMonitorDto`.

| Field | Source | Note |
|-------|--------|------|
| `state` | `QueueDto.status` | `Running` or `Stopped` |
| `nowLabel` | `monitor.current` | Sequence name; or "Idle pause, resumes at <time>"; or "Waiting until <time>"; or "Nothing scheduled" |
| `lastOutcome` | `monitor.lastOutcome` | `status` and `summary`, or null |
| `stale` | poll result | true when the last poll failed; data is marked old |

## ScheduleRow (new, derived)

One scheduled entry of a queue.

| Field | Type | Source |
|-------|------|--------|
| `key` | `string` | `sequenceId` and the position |
| `name` | `string` | `sequenceName`, else `sequenceId`, else "Unknown sequence" |
| `enabled` | `boolean` | template entry `enabled` (true if no template) |
| `nextAt` | `string \| null` | monitor item `expectedAt` (running queue only) |
| `timingText` | `string` | formatted time, `relativeLabel`, the template schedule text, or "not scheduled" |
| `repeats` | `boolean` | monitor item `repeats` |

Ordering rule (spec FR-005 and FR-005a):

1. Enabled rows with `nextAt`, by `nextAt` ascending.
2. Enabled rows without `nextAt`, in template order.
3. Disabled rows, in template order, marked "disabled".

Validation: rows are never made for a template that is missing; in that case the view uses `QueueDetailDto.entries` as names only.

## Existing types used without change

`QueueDto`, `QueueDetailDto`, `QueueMonitorDto`, `QueueMonitorItemDto`, `RunOutcomeDto`, `QueueTemplateDetail`, `QueueTemplateEntryDto` (see `src/web-ui/src/services/queues.ts` and `queueTemplates.ts`).
