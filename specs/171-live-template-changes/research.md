# Research: Apply template changes to a running queue

All items are decided. No NEEDS CLARIFICATION remains.

## Facts found in the code (base for the decisions)

- A run reads the linked template once at start. It keeps only the enabled entries in locals of
  `QueueExecutionService.RunAsync` (`allEntries`, and the partitions at-start, once-per-run, every-step,
  before-each-run, timer). A template edit during a run does not reach the run.
- `QueueRunSchedule` records consumed work (time-of-day fired today, relative fired, once-per-run done in this
  cycle, daily retries) under the **list index** of the entry.
- The runtime store (`IQueueRuntimeStore`) holds `QueueEntry { EntryId, SequenceId }`. `SetEntries` creates a
  new `EntryId` for each entry. The template entry has no ID.
- Two pauses exist: the idle pause (073, self-releasing) and the failure-policy pause (087, released only by
  `POST /api/queues/{id}/resume`). The 087 gate sits at the top of a loop iteration only. Health reports both
  with `paused`, `pausedAt`, `pauseReason`, `pauseKind`.
- Feature 106 added a "held iteration". When the device is not live, a firing group returns `null`. The caller
  keeps the firing due and marks nothing. The iteration does not complete the cycle. A later pass continues the
  once-per-run entries that did not run.
- A run does not survive a service restart. `ResumeOnServiceStart` starts a fresh run.
- A template save (`POST /api/queue-templates`) replaces all entries of the template by name.

## R-001: Where does the pause take effect?

- **Decision**: At each firing-group boundary and at the top of a loop iteration. A firing group is one
  scheduled sequence plus its helper sequences (before-each-run pass and every-step pass).
- **Rationale**: The helper sequences are part of the same firing. A pause in the middle would leave a firing
  that is half done when the operator changes entries. One choke point exists for the group: `FireGroupAsync`
  and the daily-retry path. This gives a clean state for an apply.
- **Alternatives**: Gate before every `RunOneSequenceAsync`. Rejected: it can split a firing and it needs a
  re-plan inside the every-step pass. Reuse the 087 policy gate. Rejected: it only gates at the iteration top,
  so a long once-per-run pass would not pause between its entries, and a change would alter the 087 behavior.

## R-002: How does the schedule state survive a plan change?

- **Decision**: Key all consumed-work state by entry ID. Move the entries and partitions into an immutable
  `QueueRunPlan`. The schedule holds the current plan and a `PlanVersion`.
- **Rationale**: An index changes when the operator adds, removes, or moves an entry. An ID does not. This is
  the only way to meet "schedules do not change without a direct instruction" (FR-008, FR-009).
- **Alternatives**: Keep indexes and rewrite them on each apply. Rejected: error-prone, and it hides the
  move. Key by sequence ID. Rejected: the same sequence can appear more than once.

## R-003: Entry ID and the old templates

- **Decision**: `QueueTemplateEntry.EntryId` is a string (GUID, format `N`). It is omitted from JSON when empty.
  `QueueTemplate.NeedsUpgrade` is computed: true when any entry has an empty ID. A template save:
  - creates IDs for all entries of a new template;
  - for an existing template that has IDs, keeps a supplied ID and creates one for an entry with none;
  - for an existing template that needs upgrade, creates no ID (no silent upgrade, FR-025).
  The runtime store takes the template IDs as its `EntryId`.
- **Rationale**: Meets FR-017 and FR-025. A client that sends back the IDs from the detail keeps identity.
- **Alternatives**: Auto-upgrade on load. Rejected by the operator (manual upgrade). A position-based match.
  Rejected in clarification Q1.

## R-004: How does the run loop read the new plan?

- **Decision**: The loop reads the plan at the top of each iteration. The apply action runs only while the
  run is paused. After the resume, the gate sees that `PlanVersion` changed. It marks the iteration "replan"
  and `FireGroupAsync` returns `null` for the rest of the iteration, as it does for a held firing. The end of
  the iteration then skips the cycle completion, sets `oncePerRunPassHeld`, and continues at once (no wait).
  The next iteration reads the new plan. The once-per-run entries that ran in this cycle do not run again
  (feature 106 logic, keyed by ID).
- **Rationale**: It reuses a path that has tests. It needs no exception for flow control.
- **Risk**: The pass counters (`executed`, `cycles`) must not change in a replan iteration. A test covers this.
- **Alternatives**: Mutate the partition lists in place. Rejected: a `foreach` over a changed list fails, and
  a stale firing can run. Restart the run. Rejected: it is the thing the feature avoids.

## R-005: Diff, selection, and atomic apply

- **Decision**: The service computes the differences between the run plan and the template. Each difference has
  a key (`<type>:<entryId>`), a type (`added`, `removed`, `enabled`, `disabled`, `parameters`, `schedule`,
  `order`), the entry name, and a text for the operator. The response has a `baseToken`: a SHA-256 of the
  template content, as the service reads it. The apply request sends the selected keys and the token. The
  service refuses with 409 when the token differs (FR-019) or when another apply runs (FR-020). It builds the
  new plan and the new runtime entries off to the side, validates them, then swaps both under the handle lock
  (FR-015). Rules per type:
  - `added`: new entry joins the plan with the template schedule; no consumed state.
  - `removed`: the entry and its consumed state and daily retry go away.
  - `enabled` / `disabled`: the entry joins or leaves the plan; its consumed state stays (the due time stays).
  - `parameters`: the plan entry takes the new values; the next firing uses them.
  - `schedule`: opt-in only (FR-010). The plan entry takes the new schedule and its consumed state clears.
  - `order`: the plan order follows the template (US4).
- **Rationale**: One token and one swap give atomic behavior with no change to the template file.
- **Alternatives**: A revision counter in the template file. Rejected: it adds a stored field for one check.

## R-006: Manual upgrade

- **Decision**: `POST /api/queue-templates/{id}/upgrade` gives IDs to all entries without one and reports the
  count. It changes nothing else. A repeat call reports 0. For a template that a running queue uses, the call
  also needs the match of the running entries: it first answers with a preview (`dryRun=true`) that lists each
  running entry and the template entry that it matches (by order and sequence). The real call needs
  `confirm=true`. Entries that do not match are listed, and the call refuses until the operator resolves
  them (stop the queue, or reload the template) (FR-027). The web UI shows the "needs upgrade" mark and the
  action. `docs/live-template-changes.md` holds the steps.
- **Key rename**: A run of an old template keys its consumed state with `idx:<position>` (R-002). When the
  upgrade gives real IDs to a running queue, `SwapPlan` takes a rename map (`idx:<n>` to `entryId`) and moves
  each consumed register to the new key in the same step as the plan swap. No state is lost and no timer moves
  (FR-027, FR-008). A test with fired timers, done once-per-run entries, and a pending daily retry checks this.
- **Rationale**: Meets FR-025 to FR-030. A preview prevents a wrong match of schedule state.

## R-007: Time limit, restart, "Pause when idle"

- **Decision**:
  - The time limit of feature 170 belongs to one sequence (`watchdogTimeoutMs`). It is not a limit for a queue
    run. A paused queue has no running sequence, so no limit applies and nothing ends a paused run. A pause
    request does not change the limit of the sequence that runs: the limit can end it, and then the pause starts
    (FR-032). The plan has no time-left field and no time-left UI.
  - A run end for any reason (stop, failure, restart) clears the operator pause and the apply lock (FR-007).
    No change to `ResumeOnServiceStart`.
  - **Idle hold (073)**: `IdlePauseHoldAsync` waits in a poll loop. The loop checks the operator pause request
    on each poll tick. On a request it stops early, does not bring the game to the front, and sets the run flag
    `GameBackedOut`. The loop top then waits at the gate, and the state is "paused" (FR-036). When the gate
    opens on resume, the loop brings the game to the front (the same call as the end of the hold) and clears the
    flag. A stop request still cancels the hold as today.
  - The liveness watch (106) and the device recovery are not gated by the operator pause. They keep running
    while the queue is paused. The gate only stops the start of new firing groups (FR-034).
  - While the operator pause is in force, the loop does not call the idle-pause hold. It waits on the pause
    gate. No game-state change is made (spec edge case).
- **Alternatives**: Persist the pause. Rejected in clarification Q4.

## R-008: Names on the wire and in the UI

- **Decision**: `pauseKind` gets the value `operator`. Health gets `operatorPause`: `none`, `pausing`, or
  `paused`. UI text: "Pause", "Resume", "Pausing (waiting for the running sequence)", "Paused (between
  sequences)". The idle setting keeps the label "Pause when idle" (FR-033). If a failure-policy pause and an
  operator pause are both in force, health reports `failurePolicy` as `pauseKind`, as it does for the idle
  pause today. `resume` releases the operator pause first.
- **Rationale**: The spec chose the words "Pause" and "Resume". The `kind` field keeps the three apart.

## R-009: Interactions with other features

- **Settings export and import (145)**: The export carries `entryId`. An import without IDs makes a new
  template with new IDs (it is a new object, as for a template save). An import over a stored template that
  needs an upgrade creates no ID. An import over a template with IDs keeps the ID of the entry at the same
  position and sequence. The canonical text ignores the IDs, so an ID-only difference is "unchanged".
- **Gamepack seed template (150)**: The seed creation gives IDs. An existing seed template is not changed by
  a gamepack, so it uses the manual upgrade.
- **Queue page by-position merge (048/049)**: The UI merges schedule state from the template detail by
  position today. With IDs it merges by ID when IDs exist, and by position when not.
- **Parallel queues on one emulator (079)**: One run for each emulator. The pause is per run. No change.
