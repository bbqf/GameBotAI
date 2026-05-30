# Data Model: Evaluate-And-Execute Trigger Guard

## Command
- **Identifiers**: `Id` (string/UUID)
- **Fields**:
  - `Name`: human-readable description
  - `TriggerId`: optional reference to a `Trigger`
  - `Steps`: ordered collection of `CommandStep`
  - `CreatedAtUtc` / `UpdatedAtUtc`
- **Relationships**: many commands reference one trigger; steps may reference nested commands.
- **Validation**:
  - `Steps` must not be empty for actionable commands.
  - `TriggerId` required for Evaluate & Execute path; if null the command is treated as force-execute only.

## CommandStep
- **Fields**:
  - `Order`: integer used for deterministic execution
  - `Type`: `Action` or `Command`
  - `TargetId`: action or command identifier based on type
  - `Args`: action-specific payload
- **Validation**: unique `Order` values per command; `TargetId` must exist in corresponding repository.

## Trigger
- **Identifiers**: `Id`
- **Fields**:
  - `Type`: e.g., delay, schedule, image match
  - `Enabled`: boolean gating evaluation/execute
  - `CooldownSeconds`: integer >= 0
  - `Params`: trigger-specific configuration
  - `LastResult`: cached `TriggerEvaluationResult`
  - `LastEvaluatedAt`, `LastFiredAt`
- **Relationships**: referenced by zero or many commands; evaluation uses `TriggerEvaluationService`.
- **Validation**:
  - Disabled triggers short-circuit Evaluate & Execute regardless of status.
  - Cooldown enforced using `LastFiredAt` + `CooldownSeconds`.

## TriggerEvaluationResult
- **Fields**:
  - `Status`: `Satisfied`, `Pending`, `Failed`
  - `EvaluatedAt`: timestamp
  - `Details`: optional metadata (e.g., reason pending)
- **Usage**:
  - Drives decision to execute vs skip commands.
  - Persisted on triggers for cooldown math and observability.

## Session
- **Identifiers**: `Id`
- **Fields**:
  - `Status`: `Running`, `Stopped`, `Errored`
  - `GameId`: owning game context
  - Transport details (ADB device id, stub mode flags)
- **Rules**: Evaluate & Execute only allowed when `Status == Running`; otherwise throw `not_running` to avoid orphaned inputs.

## Derived State
- **ExecutionOutcome** (implicit): count of accepted inputs returned from `SendInputsAsync`; used by API responses and success metrics.
- **TriggerCooldownWindow**: computed as `LastFiredAt + CooldownSeconds`; determines when status transitions from pending → satisfied on next evaluation.
