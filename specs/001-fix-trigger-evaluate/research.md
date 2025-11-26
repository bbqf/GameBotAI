# Research Log: Evaluate-And-Execute Trigger Guard

## Decision 1: Enforce trigger evaluation before any command execution
- **Decision**: `CommandExecutor.EvaluateAndExecuteAsync` will always evaluate the referenced trigger before invoking `ForceExecuteAsync`, even if cached metadata or prior evaluations exist.
- **Rationale**: Cooldown and pending semantics rely on fresh evaluation timestamps. Evaluating up front guarantees that the returned status reflects current state and that `LastEvaluatedAt`/`LastResult` remain monotonic for analytics.
- **Alternatives considered**:
  - *Lazy evaluation during command recursion*: rejected because nested execution would occur before knowing trigger status, reintroducing the bug.
  - *Relying on background trigger pollers*: rejected because Evaluate & Execute is explicitly synchronous and must not depend on background cadence.

## Decision 2: Persist satisfied trigger metadata before executing actions
- **Decision**: When evaluation returns `Satisfied`, update `LastResult`, `LastEvaluatedAt`, and `LastFiredAt` in the trigger repository before calling `ForceExecuteAsync`.
- **Rationale**: Persisting first ensures that even if execution throws, cooldown timers are respected for the next attempt and observability tools can see that the trigger fired.
- **Alternatives considered**:
  - *Persist after execution completes*: rejected because failures between execution and persistence would allow duplicate firings and break cooldown tracking.
  - *Skip persistence entirely*: rejected; without state updates, subsequent evaluations cannot apply cooldown gates.

## Decision 3: Use targeted unit tests with explicit fakes instead of heavy integration harness
- **Decision**: Introduce a dedicated unit-test fixture for `CommandExecutor` that replaces repositories and session manager with in-memory fakes to assert the positive/negative trigger paths.
- **Rationale**: Unit tests run within milliseconds, remove ADB/test-environment dependencies, and can directly assert invocation ordering (e.g., verifying `SendInputsAsync` is never called when trigger pending).
- **Alternatives considered**:
  - *Extend existing integration tests only*: rejected because failures would continue to require full service bootstraps and would not catch regression until late in CI.
  - *Adopt a mocking framework (Moq/NSubstitute)*: rejected to avoid adding new dependencies when lightweight fakes are sufficient.
