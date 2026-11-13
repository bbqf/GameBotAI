# Contract: Scheduling Rule

This contract gives the behaviour that the tests check. Times are local times of one day. The window is 15 minutes, unless the row says a different value.

## When the rule applies

The rule applies only when all these conditions are true:

1. The step is `reschedule-self` with the option `Timer`.
2. The sequence runs in a queue run that is active.
3. The stored queue has `smartScheduling: true` at the time of the booking.
4. The sequence has `ignoreSmartScheduling: false`.

In all other cases the booking has the requested time, as before this feature.

## Targets

| Pending firing | Target? |
|----------------|---------|
| Timer booking of a different sequence (postponed or not, time-critical or not) | Yes |
| Template Timer entry (time of day), next due time | Yes |
| Template Timer entry (relative offset) that did not fire | Yes |
| Armed daily retry of a time-of-day entry | Yes |
| Pending live schedule | Yes |
| Each firing of the sequence that makes the booking | No |
| Before Each Run entry, After Every Step entry | No |
| At Queue Start, Once Per Run, and next-cycle-start work | No |
| Run-next request | No |
| Disabled template entry | No |
| Firing of a different queue | No |

## Decision table

| # | Requested | Pending firings of other sequences | Booked | Postponed |
|---|-----------|------------------------------------|--------|-----------|
| 1 | 12:00 | 12:10 | 12:10 | Yes |
| 2 | 12:00 | 12:20 | 12:00 | No |
| 3 | 12:00 | 11:55 | 12:00 | No |
| 4 | 12:00 | 12:04, 12:12 | 12:04 | Yes |
| 5 | 12:00 | 12:15 | 12:15 | Yes |
| 6 | 12:00 | 12:15:01 | 12:00 | No |
| 7 | 12:00 | none | 12:00 | No |
| 8 | 12:00 | 12:00 | 12:00 | No |
| 9 | 12:00 | 12:10 (window 5 minutes) | 12:00 | No |
| 10 | 23:55 | 00:05 on the next day | 00:05 on the next day | Yes |

## Rules that stay true

- `requested <= booked <= requested + window`, for each booking.
- A booked time never changes after the booking.
- The rule never moves the target.
- The last booking of a sequence wins. `keep: earliest` compares the booked times.
- `Cancel` removes a postponed booking in the same way as each other booking.
- A held firing that goes back to the register (`RearmTimerFiring`) keeps its booked time and its requested time. The rule does not run again.

## Order of bookings

With no other pending firing:

| Order | Result |
|-------|--------|
| B books 12:00, then C books 12:05 | B at 12:00, C at 12:05 |
| C books 12:05, then B books 12:00 | C at 12:05, B at 12:05 |

This difference is permitted (Clarifications, question 1).
