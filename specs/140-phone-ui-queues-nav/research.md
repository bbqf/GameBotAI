# Research: Phone UI Queue Activity and Navigation

All items are resolved. No "NEEDS CLARIFICATION" remains.

## R-01 Does the service already give the queue activity and schedule data?

**Decision**: Yes. No service change is needed.

**Finding** (read from `src/web-ui/src/services/queues.ts` and `queueTemplates.ts`):

| Data point | Source | Fields |
|------------|--------|--------|
| Queue state | `GET /api/queues`, `GET /api/queues/{id}` | `status` (`Running` or `Stopped`), `entryCount`, `linkedTemplateId` |
| What runs now | `GET /api/queues/{id}/monitor` | `running`, `current` (`sequenceName`, `reason`, `scheduleKind`, `expectedAt`), `deviceSerial`, `cycleExecution` |
| What is next, with time | same monitor | `upcoming[]` (`expectedAt` or `relativeLabel`, `repeats`, `order`), `nothingScheduled` |
| Last result | same monitor | `lastOutcome` (`status`, `summary`) |
| Entries, enabled state, timer setting | `GET /api/queue-templates/{id}` | `enabled`, `scheduleType`, `timerTimeOfDay`, `timerRelativeOffset` |

The desktop `QueueMonitor.tsx` uses the monitor endpoint only. The phone view already loads the template for the enable switches. The monitor `upcoming` list covers only enabled entries of a running queue. Disabled entries and the entries of a stopped queue come from the template. The merge is done in the web UI (D-2 in plan.md).

**Rationale**: Keeps the change in the web UI (spec clarification Q5).

**Alternatives considered**:

- A new "schedule" endpoint that returns next times for a stopped queue: rejected. A stopped queue has no next time (timers start with the run). The spec asks for no false data (US1, scenario 3).
- Reuse the `QueueMonitor` component on the phone: rejected. Its layout is for the desktop, it stops polling at run end, and it links to `#/execution`.

## R-02 Poll interval and pause rules

**Decision**: 5 s interval (spec clarification Q1). Pause when `document.visibilityState` is `hidden` or the connection state is `lost`. On resume, call at once, then restart the interval.

**Rationale**: 5 s plus one request time is below the 15 s goal (SC-002). The desktop monitor uses 2.5 s; the phone saves battery and data.

**Alternatives considered**: Server push (SignalR or SSE): rejected, no such channel exists for queues and the service would change. A shared 2.5 s interval: rejected, higher load with no gain on a phone.

## R-03 Card summary data cost

**Decision**: The list polls `GET /api/queues` and, for each queue with `status === 'Running'`, one `GET /api/queues/{id}/monitor`, every 5 s. Stopped queues show "Stopped" and need no monitor request. A failed monitor request for one card shows the status only.

**Rationale**: A typical install has 1 to 5 queues, and few run at once (each holds its own emulator). The extra load is small. No list-level summary field exists in the service.

**Alternatives considered**: Show a summary only in the detail: rejected, because FR-006 asks for it on the card. Add a summary field to the list endpoint: rejected, service change with no need.

## R-04 Back navigation mechanism

**Decision**: Use the History API directly (`pushState`, `popstate`). Do not add a router.

**Finding**: `App.tsx` calls `history.replaceState` in an effect on every change of tab or area. This is why back leaves the application: the history has one entry. The queue selection is local state in `PhoneQueuesView`, so it has no address.

**Rationale**: The application has no router and uses query parameters. A router is a large change for a small need. The History API gives the exact behavior in the spec (add an entry on a move, restore on `popstate`).

**Alternatives considered**: `react-router`: rejected (new dependency, rewrite of the desktop navigation, risk to SC-007). Hash routes: rejected, the code already uses `#/execution` for a link, and the query style is the existing address format.

## R-05 First entry and the on-screen back control

**Decision**: Store `{ idx }` in `history.state` for each entry that the app makes. At start, the first entry gets `idx: 0` by `replaceState` (same address). The on-screen back control of the detail calls `history.back()` when `idx > 0`. When `idx` is 0 (direct link), it calls `replaceState` to the list address.

**Rationale**: `history.state` survives a reload, so the rule stays correct after a reload (FR-013). It matches spec clarification Q3 and avoids a fake history.

**Alternatives considered**: A module variable: lost on reload. `history.length`: includes entries from before the application; not reliable.

## R-06 How to hide the Execution area on the phone

**Decision**: Add the support level `hidden` to `PhoneSupportLevel`. Filter `hidden` areas in the tab bar and the "More" sheet. Redirect in `App.tsx` with `replaceState`.

**Files**: `src/web-ui/src/types/navigation.ts` (the record), `src/web-ui/src/types/__tests__/navigation.test.ts` (the type test), `src/web-ui/src/components/phone/phone.css` (the phone queue styles).

**Rationale**: One record (FR-016) drives all lists. The existing `desktop-only` level shows a notice and still lists the area, which the spec forbids (FR-001, SC-005).

**Alternatives considered**: Delete the `execution` entry on the phone only in each list: rejected, many places, easy to miss. Remove the area from `navigationAreas`: rejected, the desktop needs it.

## R-07 Time display

**Decision**: Use `toLocaleString` (device locale and zone) for absolute times. Add a relative text ("in 25 min", "in 2 h") from a pure function that takes `now` as an argument, so tests do not depend on the clock.

**Rationale**: Matches the spec edge case and assumption on time zone.
