# Research: Run Next for Unscheduled Tasks

## R-001: Reuse the live-schedule call or add a new call

- **Decision**: Add a new call `POST /api/queues/{id}/run-next` and a new register on the run handle.
- **Rationale**: The live-schedule call (feature 059) takes a sequence ID. A template can hold the same
  sequence twice with different parameter values, so the operator must name the entry. The live schedule
  fires with the queue scope, so it loses the entry parameter values (spec FR-005). It also has no state
  check, and it replaces a pending live schedule of the same sequence.
- **Alternatives considered**: (a) Call live-schedule with offset `00:00:00`. Rejected: wrong scope and
  no entry identity. (b) Use the self-reschedule once-per-run register. Rejected: it drains only inside
  the once-per-run pass, and a non-cycling run that finished that pass never drains it again.

## R-002: Where the loop drains the register

- **Decision**: Use one drain method. Call it at the live-schedule step of each loop pass. Call it also
  after each firing group inside the once-per-run pass. Each call comes after the pause gates.
- **Rationale**: FR-004 says the entry runs right after the running sequence ends. A call only at the
  live-schedule step could delay a request for a whole once-per-run pass. A call after each firing group
  keeps the wait to one sequence. If no sequence runs, the next loop pass fires the request at once.
  The pause gates come first, so a paused queue fires nothing (spec User Story 1, scenario 3). The
  loop already puts a held firing back after a device-liveness hold (feature 106). The run-next drain
  uses the same rule.
- **Alternatives considered**: Drain only once for each loop pass. Rejected: the wait can be as long as
  a whole cycle.

## R-003: Keep a non-cycling run alive

- **Decision**: Count a pending run-next request in `HasPendingSelfRescheduleWork` and in the
  "pending relative or live" check, so a non-cycling run does not end before it fires the request.
- **Rationale**: Without it, a non-cycling run with only run-next work would finish at once (the same
  bug that feature 059 fixed for relative timers).
- **Alternatives considered**: None. The rule is the existing pattern.

## R-004: How to find the state of an entry

- **Decision**: The monitor adds `entryStates`, a map from entry ID to one of `running`, `queued`,
  `scheduled`, `idle`. The server decides the state. The UI only shows it.
- **Rationale**: The server knows the run schedule: consumed once-per-run steps, fired timers, pending
  self-reschedule bookings, and the current sequence. The UI cannot compute this from the template. The
  phone view already reads the monitor, so it adds no request (FR-009, mobile principle 11).
- **Rules** (for an enabled entry of the run plan):
  - `running`: the entry sequence is the current sequence, and the entry is the one that fired.
  - `queued`: a run-next request for the entry is pending.
  - `scheduled`: the entry is a once-per-run step not yet done in this cycle, an unfired timer, a
    pending timer or live booking for its sequence, or an at-queue-start, before-each-run, or
    every-step entry that did not run yet in this run.
  - `idle`: all other cases. Examples: a once-per-run step that ended, a fired timer, and an
    at-queue-start, before-each-run, or every-step entry that ran once in this run.
  - The table in `data-model.md` gives the state for each schedule type.
- **Alternatives considered**: Compute the state in the UI from the monitor `upcoming` list. Rejected:
  the list has sequence IDs, not entry IDs, and it hides repeated sequences.

## R-005: Entry identity and old templates

- **Decision**: The call takes the stable `entryId` (feature 171). An entry with no ID (a template that
  needs an upgrade) cannot use Run next. The UI shows "Upgrade the template to use Run next."
- **Rationale**: The run plan keys consumed work by entry ID, or by `idx:<position>` for an old entry.
  A position key is not stable under a live apply, so a request that uses it could hit the wrong entry.
- **Alternatives considered**: Accept `idx:<position>`. Rejected for the reason above.

## R-006: Concurrency and double requests

- **Decision**: The register is a list under a lock, with at most one request for each entry ID. A second
  request for a queued entry returns `200` with `alreadyQueued: true` and adds nothing.
- **Rationale**: This gives the spec rule "the entry runs once" (FR-004), also for two operators.
  The call stays safe to repeat, so a retry by a client cannot cause two runs.
- **Alternatives considered**: Return `409` for a repeat. Rejected: a harmless repeat is not an error.

## R-007: Entry scope for the run

- **Decision**: The request keeps the `ParameterScope` of the entry (same rule as `EntryScope`) when the
  call is accepted, and the loop fires with that scope.
- **Rationale**: FR-005. The scope is built from the run plan entry at request time. A later live apply
  does not change a request that is already queued.
- **Alternatives considered**: Build the scope at fire time. Rejected: an apply between the request and
  the fire could change the values without the operator knowing.

## R-008: Icon buttons in the editor

- **Decision**: Reuse `IconButton` from feature 173. Add icons: `runNext`, `parameters`, `remove` (can
  reuse the `delete` path), `add`, `save`, `reload`, `load`. Keep the accessible name `<Action> <name>`
  and the tooltip `<Action>`. Keep the drag handle and the form fields as they are.
- **Rationale**: One style for all pages. The `aria-label` stays the same as today for "Remove X" and
  "Parameters for X", so tests and screen readers keep working. A text button becomes an icon, so the
  row loses its text width.
- **Row length**: Put the controls in one flex row that does not wrap on desktop. Show the parameter
  form below the row only when open. A row of 10 controls fits in 1 line at 1024 px or wider.
- **Alternatives considered**: An overflow menu ("…"). Rejected: it adds a tap, and it hides **Run
  next**.

## R-009: Phone view

- **Decision**: Add the **Run next** icon button to each row of the phone schedule list, with a 44 x 44
  px target. The phone view reads `entryStates` from the monitor it already loads. The button is off
  when the connection is lost, when the queue is not running, or when the state is not `idle`.
- **Rationale**: Mobile principles 3, 9, 10, and 11. One primary action for the screen stays **Start**
  or **Stop**. The row action is secondary and is behind a tap on the row icon only.
- **Alternatives considered**: A confirm dialog. Rejected: the action is cheap and safe to repeat.

## R-010: Logging

- **Decision**: Write one information log line for each accepted request. The line has the queue ID,
  the entry ID, the sequence ID, and the request time. The firing is a normal firing in the execution
  log. It has no new label.
- **Rationale**: FR-010. The firing code has no free label field. `originActionId` is for
  self-reschedule attribution only. A new field would change the execution log record for little gain.
- **Alternatives considered**: (a) A new `Origin` field in `ExecutionLogContext`. Rejected: it changes
  the log record and its docs. (b) Reuse `originActionId`. Rejected: one field would have two meanings.
