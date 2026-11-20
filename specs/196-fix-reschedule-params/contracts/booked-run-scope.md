# Contract: The parameter scope of a booked run

**Feature**: 196-fix-reschedule-params

This is an internal behavior contract of the queue run. The HTTP API, the stored JSON, and the web UI do not change.

## 1. Which values a booked run uses

| Condition at the time of the firing | Values of the booked run |
|---|---|
| The booking came from a run of template entry E, and E is in the run plan (enabled or disabled) | The values that E has in the run plan now |
| The booking came from a run of template entry E, and an upgrade renamed the key of E | The values of E under its new key |
| The booking came from a run of template entry E, and E is no longer in the run plan | The values that the booking kept (behavior of today) |
| The booking came from a run that has no template entry (live schedule, booking with no scope) | The values that the booking kept, or the queue scope (behavior of today) |

"The booking came from a run of template entry E" also applies to a booking that a booked run of E made (a chain), and to a booking that a run-next run of E made.

## 2. What does not change

- The fire time, the register, the `keep` rule, and the number of bookings.
- The bookings of other entries.
- The stop and start behavior, the parameter types, and the validation of number parameters.
- The values of a run-next request: they stay fixed at the time of the request (feature 184, FR-005).
- The `resolvedParameters` attribute of the `parameters` log item: the same names, values, and origin layers for the same values. A value from the entry has the origin layer `entry`.

## 3. Execution log

No new log text. The `parameters` item of each command step of the booked run shows the value that the run resolved, with the origin layer `entry` when the template entry supplied it.
