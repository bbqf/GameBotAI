# Research: End a run at its time limit without a failure status

## R-001: Where the option lives and how it is stored

- **Decision**: A string member `OnTimeLimit` on `CommandSequence`, JSON name `onTimeLimit`, values `fail` and `end`.
  The file omits the member when the value is `fail` or absent.
- **Rationale**: Feature 122 (`excludeFromSuccessNotifications`) shows the pattern: optional member, omitted when
  default, so an old file reads as the default (FR-010). A string (not a bool) matches the issue and leaves room
  for more values without a file change.
- **Alternatives**: A bool `endAtTimeLimit` (rejected: the issue names `onTimeLimit` and the spec fixes the name).
  A new enum type with a JSON converter (rejected: more code; the API must name allowed values in the error text,
  which a plain list does simply).

## R-002: How the run learns the option

- **Decision**: `QueueExecutionService.RunOneSequenceAsync` already reads the sequence in
  `ResolveWatchdogTimeoutAsync`. Extend that read to return the limit and the option together, and pass the option
  to `SequenceTimeLimitScope.Push` as `endsRunAtLimit`. A lookup failure falls back to `fail`.
- **Rationale**: One read per firing, no new dependency. `SequenceExecutionService` never sees the queue tokens,
  and the scope is the existing way to give it the limit state (feature 094). Only a queue firing has a limit
  today, so "everywhere the limit applies" (clarification 1) means this one path.
- **Alternatives**: Read the sequence again in `SequenceExecutionService` (rejected: second read, and it must still
  know whether the limit elapsed, which only the scope tells).

## R-003: Status at the two finalize sites

- **Decision**:
  - `FinalizeAbandonedAsync` (the run unwound by the cancel): if the scope has elapsed and `EndsRunAtLimit`, write
    `success`, the reason `sequence_time_limit`, and the limit value. The summary says that the run ended at its
    time limit by choice.
  - Normal finalize (a step swallowed the cancel): convert `failure` to `success` only when the scope has elapsed,
    `EndsRunAtLimit` is true, and the run result holds no failed step. If a step failed, the status stays
    `failure` and the reason is stamped as today (spec edge case: a fault before the cancel keeps `failure`).
    The implementation reads the result shape to find a failed step; if the result cannot tell, the site keeps
    `failure` (safe side).
  - A stop request still wins: `HasElapsed` is false when a stop was requested, so nothing changes there.
  - Same-time fault: a failed step that the run records at the same time as the limit keeps `failure` (spec
    scenario 5 of story 1). The abandoned site also keeps `failure` if the unwinding exception is not the cancel
    of the limit timer.
  - Default limit: a sequence with no `watchdogTimeoutMs` gets the default of 240000 ms from
    `SequenceTimeLimits.DefaultWatchdogTimeoutMs`. The scope carries that value, so the `end` rule and the logged
    limit value work the same for the default (spec scenario 4 of story 1). The default does not change.
  - In-flight step: the limit cancels the step in progress as today. This feature changes only the final status
    (`success` for `end`). A test shows that the step is cancelled and the status is `success`.
  - Summary text: for an `end` stop the summary SHOULD say that the run ended at its time limit by choice. The
    contract is the structured fields (status and reason). A test on the summary text is a soft check.
- **Rationale**: The abandoned path is the common path. The normal path is rare; the safe rule never hides a fault.
- **Alternatives**: Always convert at the normal site (rejected: could hide a real fault). Never convert there
  (rejected: a swallowed cancel would then still show `failure` for a planned stop).

## R-004: Queue statistics, retry, and notification for an `end` stop

- **Decision**: For an `end` stop, `RunOneSequenceAsync` records the firing as `SequenceRunStatus.Success` and
  returns true, with `watchdogEnded` false.
- **Rationale**: The author says the stop is planned. A failure notification, a `cancelled` record, or a daily
  retry for a planned stop would repeat the original problem one layer up. Feature 120 maps a time-limit stop to a
  failure message; for `end` that mapping must not apply. The `fail` and absent cases keep all current behaviour.
- **Alternatives**: Leave the queue as is (rejected: the log says success but the user still gets a failure
  message). Add a new run status (rejected: FR-008, no new statuses).
- **Scope note**: The spec says parent-sequence handling does not change. This decision touches only the queue
  firing record, not any parent sequence logic. Today only the queue firing path applies the limit, so a parent
  that starts a child run sees no change. Observable: for the same case with `fail`, the parent run status and the
  queue record stay as they are today; only the child run status differs for `end`. A test compares the two cases
  (FR-004a and story 1 scenario 6).

## R-005: Validation

- **Decision**: Create, PUT, and PATCH check `onTimeLimit` before any other change. A value that is not a JSON string
  `fail` or `end` (case-sensitive) gives 400 with `errors` text: `onTimeLimit must be one of: fail, end.` JSON null
  on PATCH is an error; absence keeps the saved value. PUT without the member resets to `fail` (PUT replaces the
  sequence, as it does for `watchdogTimeoutMs`). `FileSequenceRepository` also rejects an unknown value on save, so
  an import or a gamepack write cannot store it. The API and the repository use one shared error message (one
  constant in `SequenceTimeLimits`), so the text cannot drift (FR-006). A test compares the two messages.
- **Rationale**: Same order and shape as feature 122. A repository check protects all write paths.
- **Alternatives**: Case-insensitive values (rejected: one spelling keeps files and docs simple).

## R-006: Export and import

- **Decision**: No new code. Sequences export and import as the stored JSON, so the member travels with the file.
  A test proves the round trip (sequence file, gamepack export, and import).
- **Rationale**: The member is a normal property of `CommandSequence`.

## R-007: Web UI

- **Decision**: One select "When the time limit is reached" with the options "Fail the run" (`fail`) and
  "End the run as success" (`end`) in the create form and in the edit form of `SequencesPage.tsx`, with a hint
  text. The save body sends `onTimeLimit` only when the value is `end` (like feature 122 sends its flag only when
  true); the edit form sends the value always, so a switch back to `fail` works with PUT. Phone support level:
  full. The select follows docs/mobile-ui-principles.md: a hit area of at least 44 x 44 CSS px and an input font
  of at least 16 px (this stops zoom on focus on a phone). A UI test checks the control classes or styles.
- **Rationale**: The form already holds the `excludeFromSuccessNotifications` checkbox. A select with two options is
  the same size and works on a phone.
- **Alternatives**: A checkbox (rejected: the value is a named choice, and more values can come later).

## R-008: Documentation

- **Decision**: Update the OpenAPI descriptions (`SequenceTimeLimitSchemaFilter`, `SwaggerConfig`) for the request
  and response fields and for `cancellationReason` (the reason can now be on a `success` entry). Update
  `docs/architecture.md` (execution log, queue watchdog), `CHANGELOG.md`, `specs/STATUS.md`, and the sequence
  authoring guide.
