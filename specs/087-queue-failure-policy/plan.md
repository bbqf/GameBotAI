# Implementation Plan: Queue Failure Policy and Outbound Notification

**Branch**: `087-queue-failure-policy` | **Date**: 2026-09-14 | **Spec**: [spec.md](./spec.md)
**Input**: Feature specification from `/specs/087-queue-failure-policy/spec.md`
**Issue**: [#181](https://github.com/bbqf/GameBotAI/issues/181) (FR-004)

## Summary

Feature 086 published `consecutiveFailedCycles` and deliberately left it inert — "exposed; nothing
acts on it." This feature makes something act on it, and gets a signal off the machine when it does.

The shape is deliberately small, because the counting already exists:

1. **`QueueFailurePolicy`** joins `ExecutionQueue` as persisted, optional configuration: a threshold,
   an action (`notify` / `stop` / `pause` / `notifyAndStop`), and an optional destination URL that
   overrides the service-wide default.
2. **`QueueFailurePolicyEvaluator`** is called from exactly one new line in the run loop, directly
   after the existing `handle.Cycles.CompleteOpen(...)`. It reads the ledger's existing health
   snapshot, decides whether the threshold has been crossed *for the first time this episode*, and
   performs the selected action.
3. **`IFailureNotifier`** delivers the event over HTTP, entirely off the run loop's thread, with a
   bounded timeout and at most one retry, swallowing every fault.
4. **Pause** is run state on `QueueRunHandle` — not a third `QueueExecutionStatus` — gated at the top
   of the run-loop iteration *before* timer evaluation, with a new `POST {id}/resume` to release it.
5. **A `notify` action type** lets an authored sequence raise the same event itself (User Story 3).

**The load-bearing constraint, restated from 086 and now qualified**: `QueueCycleLedger` stays a pure
observer — no policy knowledge, every mutator still void and non-throwing, nothing consulted from
inside it. The evaluator is a *separate* component that reads the published snapshot. That
distinction is the whole reason this feature can act on a failing run without making the ledger
unsafe, and 086's class documentation is updated to state it rather than left asserting a property
that has silently narrowed (Constitution V).

**The second constraint, also from 086**: `ExecuteRunAsync` is an ~280-line method the Roslyn taint
analyzers already scale badly against (`gamebot-build-time-analyzers`). Every addition to it here is
a single call — one for the policy evaluation, one for the pause gate. No inline blocks.

## Technical Context

**Language/Version**: C# / .NET 9 (`net9.0`)
**Primary Dependencies**: ASP.NET Core Minimal APIs, `Microsoft.Extensions.Http` (`AddHttpClient`,
already in the shared framework — no new package), Swashbuckle
**Storage**: The policy is persisted with the queue via the existing `FileQueueRepository` JSON;
policy *state* (tripped, paused, last notification) is in-memory run state discarded with the handle
**Testing**: xUnit + FluentAssertions across `tests/unit`, `tests/contract`
(`WebApplicationFactory<Program>`), `tests/integration`
**Target Platform**: Windows service host; CI is `windows-latest`
**Project Type**: Web service (single ASP.NET Core host); the React `web-ui` is untouched (spec Out
of Scope)
**Performance Goals**: No measurable change to run-loop timing. Per completed cycle the added work
is one lock-guarded snapshot copy and an integer comparison; the HTTP delivery never runs on the run
loop's thread. A queue with no policy configured short-circuits on a null check before the snapshot.
**Constraints**: Additive to the queue representation (FR-004: an unconfigured queue is
byte-for-byte unchanged in behaviour); `QueueExecutionStatus` stays two-valued; the ledger stays a
pure observer; no web-UI change
**Scale/Scope**: One new domain type + enum, one enum value on `QueueStopReason`, one options class,
two new service types, pause state on the run handle, two run-loop call sites, one new endpoint,
one new action type across six files, plus tests, `docs/architecture.md` and `specs/STATUS.md`

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

*NON-NEGOTIABLE*: If `build` or required `test` runs are failing (local or CI), implementation
progression is blocked until failures are fixed or a documented maintainer waiver exists.

| Principle | Assessment | Status |
|-----------|-----------|--------|
| **I. Code Quality Discipline** | All new logic lands in small single-purpose types — `QueueFailurePolicyEvaluator`, `HttpFailureNotifier`, `QueueFailurePolicy` — each well under the ~50 LOC guidance per method, with XML docs on every public member including error modes (the notifier's contract is "never throws", which must be documented, not just implemented). `ExecuteRunAsync` gains exactly two one-line calls, per the analyzer hazard. No new dependencies. The one genuine risk is the secret auth header: it is write-only by construction — held in options, never copied into a DTO, never logged — and a test asserts it does not appear in any API response. | PASS |
| **II. Testing Standards** | The issue describes a *condition* (a queue failing every cycle in silence), and it gets a direct regression test at integration level: a run whose every cycle fails must trip at exactly N, notify exactly once, and re-arm after a success. Unit coverage over the evaluator's decision table and the notifier's fault-swallowing; contract coverage over validation and the resume endpoint's response contracts. Test placement respects the shared-bin-directory hazard: live runs go in `tests/integration`, never `tests/contract`. | PASS |
| **III. UX Consistency** | `POST {id}/resume` follows the sibling `{id}/stop` and `{id}/start` conventions: 404 unknown queue, and a non-error response for the "known but not applicable" case rather than a 409. Config field names **deliberately diverge** from the issue's informal sketch (`onConsecutiveFailures: { count }` → `failurePolicy: { consecutiveFailedCycles }`) — see research R11 for why, and note it is a documented API shape the requesting project codes against, not a silent rename. The payload is a versioned contract (FR-014a), satisfying the principle's "stable and versioned" requirement for programmatic outputs. Validation errors name the offending value (FR-005, FR-006). Swagger picks up the new route and fields. | PASS |
| **IV. Performance** | Budget declared above. PR perf note: per cycle, one additional uncontended lock acquisition and one integer compare, skipped entirely when no policy is configured; per episode, one HTTP request on a thread-pool thread the run never awaits. No hot path touched. SC-005 is the explicit guard — a black-hole endpoint must not change cycle timing — and is asserted by an integration test using a stub notifier that blocks. | PASS |
| **V. Living Documentation (NON-NEGOTIABLE)** | This changes the domain model (policy on the queue), the API surface (health fields, resume route, queue create/update body), persistence (queue JSON), and the sequence action-type set — so `docs/architecture.md` MUST be updated on all four axes with a refreshed "Last reviewed". `spec.md` needs its `**Status**` line off Draft and a row in `specs/STATUS.md`. Feature 086 is **extended, not superseded** — but its `QueueCycleLedger` doc comment asserts a purity property this feature qualifies, so that comment is corrected in the same PR. 086's spec Status is unchanged (still accurate). All explicit tasks. | PASS |

**Gate result: PASS** — no violations, so the Complexity Tracking table is omitted.

## Project Structure

### Documentation (this feature)

```text
specs/087-queue-failure-policy/
├── plan.md              # This file
├── research.md          # Phase 0 output — R1..R10
├── data-model.md        # Phase 1 output
├── quickstart.md        # Phase 1 output
├── contracts/
│   ├── notification-payload.md   # The outbound contract (schemaVersion 1)
│   └── queue-api.md              # Changed/added HTTP surface
├── checklists/
│   └── requirements.md  # Spec quality checklist
└── tasks.md             # Phase 2 output (/speckit-tasks — NOT created here)
```

### Source Code (repository root)

```text
src/
├── GameBot.Domain/
│   ├── Queues/
│   │   ├── ExecutionQueue.cs                 # + FailurePolicy property
│   │   ├── QueueFailurePolicy.cs             # NEW — threshold, action, optional URL
│   │   ├── QueueFailureAction.cs             # NEW — Notify|Stop|Pause|NotifyAndStop
│   │   └── QueueStopReason.cs                # + StoppedByFailurePolicy
│   ├── Actions/ActionTypes.cs                # + Notify            (US3)
│   ├── Commands/FileSequenceRepository.cs    # + notify allow-list (US3)
│   └── Services/
│       ├── ActionPayloadValidationService.cs # + notify            (US3)
│       ├── SequenceStepValidationService.cs  # + notify payload    (US3)
│       └── SequenceRunner.cs                 # + non-device step   (US3)
└── GameBot.Service/
    ├── GameBotServiceSetup.cs                # + AddHttpClient, options, DI
    ├── Contracts/Queues/
    │   ├── CreateQueueRequest.cs             # + FailurePolicy
    │   ├── UpdateQueueRequest.cs             # + FailurePolicy
    │   ├── QueueResponse.cs                  # + FailurePolicy
    │   ├── QueueDetailResponse.cs            # (Health already present)
    │   ├── QueueFailurePolicyDto.cs          # NEW — request/response shape
    │   └── QueueHealthResponse.cs            # + policy/pause/last-notification fields
    ├── Endpoints/QueuesEndpoints.cs          # + POST {id}/resume, validation, projection
    ├── Services/Notifications/
    │   ├── IFailureNotifier.cs               # NEW
    │   ├── HttpFailureNotifier.cs            # NEW — bounded, retrying, never throws
    │   ├── FailureNotificationOptions.cs     # NEW — Service:Notifications
    │   └── FailureNotificationEvent.cs       # NEW — the versioned payload
    ├── Services/QueueExecution/
    │   ├── QueueFailurePolicyEvaluator.cs    # NEW — the decision + action
    │   ├── QueueCycleLedger.cs               # doc correction only
    │   ├── QueueRunHandle.cs                 # + pause state, + policy trip state
    │   └── QueueExecutionService.cs          # + 2 one-line calls
    └── Services/SequenceExecution/
        └── SequenceExecutionService.cs       # + notify dispatch   (US3)

tests/
├── unit/          # evaluator decision table, notifier fault-swallowing, policy validation
├── contract/      # config validation, resume contracts, health fields — NO live runs
└── integration/   # real runs: trip/reset/re-arm, stop reason, pause+resume, timing guard

docs/architecture.md   # domain model, API surface, persistence, action types
specs/STATUS.md        # row for 087
```

**Structure Decision**: The existing single-host layout is unchanged. Policy *configuration* is
domain state and lives in `GameBot.Domain/Queues` next to `ExecutionQueue`; policy *behaviour* and
*delivery* are service concerns and live under `GameBot.Service/Services`, with notification split
into its own folder because `IFailureNotifier` is also consumed by the sequence executor (US3) and
must not depend on the queue engine.

## Implementation Phasing

Sequenced so each phase is independently shippable and the riskiest surface lands last.

| Phase | Delivers | Maps to |
|---|---|---|
| **A — Foundation** | `QueueFailurePolicy` + action enum, persistence, request/response DTOs, validation, options, `AddHttpClient`, DI. No behaviour yet. | FR-001..FR-006, FR-016, FR-016a |
| **B — Notify (MVP)** | The evaluator, the notifier, the payload contract, the run-loop call, health fields. A failing queue alerts. | User Story 1; FR-007..FR-015a, FR-025, FR-026 |
| **C — Actions** | `stop` (+ `StoppedByFailurePolicy`), `pause` (+ handle state, gate, `POST {id}/resume`), `notifyAndStop`. | User Story 2; FR-017..FR-021 |
| **D — Notify step** | `ActionTypes.Notify` across all six registration sites, validation, dispatch. | User Story 3; FR-022..FR-024 |
| **E — Docs** | `docs/architecture.md` (4 axes), `spec.md` Status, `specs/STATUS.md`, 086 ledger doc correction. | Constitution V |

Phase B alone closes the issue's core complaint; C, D and E complete the requested scope.

## Known Limitations (deliberate, recorded)

- **The payload names the failing sequence, not its error message.** The 086 ledger records a boolean
  per entry, not a message, and threading messages through would change its record shape and its
  memory bound. The event therefore identifies *what* failed and points at the execution log for
  *why* (research R7). Revisit only if operators find the pointer insufficient.
- **Policy state does not survive a service restart**, matching every other piece of run state
  (spec A-004). A service restarted mid-outage re-arms and will notify again — which is arguably the
  desired behaviour for an operator who was not watching.
- **No cross-queue aggregation or rate limiting** (spec Out of Scope). Three simultaneously failing
  queues produce three notifications; the trip-once-per-episode rule bounds each to one.
