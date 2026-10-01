# Data Model: Exclude Helper Sequences From Success Notifications

## Sequence (changed)

One new attribute on `CommandSequence` (`src/GameBot.Domain/Commands/CommandSequence.cs`).

| Field | Type | Default | JSON name | Notes |
|-------|------|---------|-----------|-------|
| ExcludeFromSuccessNotifications | boolean | false | `excludeFromSuccessNotifications` | The file omits the member when the value is false. A missing member reads as false. |

### Validation rules

- The value MUST be a JSON `true` or `false`. Any other JSON value returns 400 (FR-008).
- A create body with no member sets false.
- A PUT or PATCH body with no member keeps the saved value (FR-005).
- A change of the flag increments the sequence version and `UpdatedAt`, like any other change (FR-012).
- A client clone is a GET, then a POST with the same data. The create call accepts the flag, so the clone keeps it (FR-009).

### State and transitions

The flag has two states: off and on. The operator changes it with a save. The notification worker
reads the flag from the stored sequence when it handles a run job. No other state depends on the flag.

| Run status | Flag | Open streak | Message sent |
|------------|------|-------------|--------------|
| Success | off | no | "success" only at level Success+Failure |
| Success | on | no | none |
| Success | any | yes | "recovered" (the streak closes) |
| Failure | any | no | "failure" (the streak opens) |
| Failure | any | yes | none (the streak stays open) |
| Cancelled (operator cancel or queue stop) | any | any | "cancelled" |

If the worker cannot read the sequence, it treats the flag as off. It writes log event 12033 and sends
the notification as usual.

Level None sends nothing, as before. Level Failure sends no "success" message, as before.
The flag has no effect at these two levels, because they send no "success" message already.

## Notification Message (unchanged)

The status "success" is not created for a sequence with the flag on. The message text and format do
not change. The failure streak table (`NotificationStreakState`) does not change.

## Relationships

- A queue entry and a queue template entry refer to a sequence by ID. They do not copy the flag.
- The flag applies in every queue that runs the sequence (FR-004).
- A deleted sequence takes its flag with it. The worker drops no extra state.
