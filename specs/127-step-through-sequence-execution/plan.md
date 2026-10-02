# Implementation Plan: Step-Through Sequence Execution

**Branch**: `127-step-through-sequence-execution` | **Date**: 2026-10-02 | **Spec**: [spec.md](spec.md)
**Input**: Feature specification from `/specs/127-step-through-sequence-execution/spec.md`

## Summary

An author opens a saved sequence and starts a step-through on a game session. The author runs one step at a time
and selects any step as the next step. The service keeps an in-memory step-through run with a cursor, a frame stack for
loops and branches, carried values, and a history. A new `SequenceStepper` runs one leaf step per call and reuses the
leaf code of `SequenceRunner`. A new `PreviewEffects` option stops `reschedule-self` and `notify` from applying,
also inside command steps. The web UI gets a step-through panel on the Sequences page. The panel polls the state,
which also renews a 90-second lease. An expired lease cancels the running step and resumes a queue that the
step-through paused. See [research.md](research.md) for all decisions.

## Technical Context

**Language/Version**: C# (.NET, as in `GameBot.sln`); TypeScript with React and Vite (`src/web-ui`)
**Primary Dependencies**: ASP.NET Core minimal APIs, existing `SequenceRunner`, `ICommandExecutor`,
`IQueueRunRegistry`, `ISessionManager`; web UI: React, Jest, Testing Library
**Storage**: In memory for sessions. Execution log files (existing) for step runs (FR-016). No new files.
**Testing**: xUnit (unit, contract, integration) for the backend; Jest for the web UI
**Target Platform**: Windows service host (local). Browser UI.
**Project Type**: Web service with a web UI (backend `src/GameBot.*`, frontend `src/web-ui`)
**Performance Goals**: State read p95 under 50 ms. Status visible within 1 s after a step ends (SC-003).
Start to first step under 30 s (SC-001).
**Constraints**: One step-through for each game session. History cap 1,000 entries. Lease 90 s.
No change to real run behavior. No schedule or daily record write from a step-through (SC-006).
**Scale/Scope**: One author at a time. About 10 new files in the backend and 4 in the web UI.

No item is marked NEEDS CLARIFICATION. The spec clarifications cover all behavior questions.

## Constitution Check

*GATE: Passed before Phase 0. Re-checked after Phase 1.*

*NON-NEGOTIABLE*: If `build` or required `test` runs fail (local or CI), implementation stops until failures are fixed or a documented maintainer waiver exists.

*NON-NEGOTIABLE*: All text in this plan and in its artifacts obeys Simplified Technical English (Principle VI).

| Principle | Status | Evidence |
|-----------|--------|----------|
| I. Code quality | Pass | Stepper is a separate class. Methods stay under about 50 lines. Shared runner helpers become `internal`, not copied. No underscores in method names. |
| II. Testing | Pass | Unit tests for the cursor and frames. A parity test compares the stepper with the real runner (SC-002). Contract tests for each route and error. Web UI tests for the panel. Coverage 80% line and 70% branch for new code. |
| III. UX consistency | Pass | Errors have a code, a cause, and a next action. Status words match the execution log. The panel follows the "Record steps" pattern of command authoring. |
| IV. Performance | Pass | Goals are in Technical Context. Hot path is the state read: in memory only. A history cap protects memory. |
| V. Living documentation | Action | Update `docs/architecture.md` (API surface, new service, "Last reviewed" date). Add the spec to `specs/STATUS.md`. Set the spec `Status` line when done. |
| VI. STE | Pass | All artifacts are written in STE. Tasks and code comments must follow the same rule. |
| Definition of Done | Action | Changelog entry (user-visible). OpenAPI descriptions through the existing schema filter. |

**Post-design re-check**: No new violation. One design choice needs a note for reviewers:
the stepper re-implements the loop cursor rules. The parity test limits the drift risk. See research R1.

## Project Structure

### Documentation (this feature)

```text
specs/127-step-through-sequence-execution/
├── plan.md              # This file
├── research.md          # Phase 0 output
├── data-model.md        # Phase 1 output
├── quickstart.md        # Phase 1 output
├── contracts/
│   └── step-through-api.md
└── tasks.md             # Phase 2 output (/speckit-tasks, not created here)
```

### Source Code (repository root)

```text
src/
├── GameBot.Domain/
│   └── Services/
│       ├── SequenceRunner.cs                  # Expose leaf entry point; helpers to internal
│       └── StepThrough/
│           ├── SequenceStepper.cs             # Cursor, frames, run one leaf
│           ├── StepPath.cs                    # Path parse, build, flatten to nodes
│           └── StepThroughModels.cs           # Frame, StepNode, HistoryEntry
├── GameBot.Service/
│   ├── Endpoints/
│   │   └── StepThroughEndpoints.cs            # Routes in the contract
│   ├── Services/
│   │   ├── CommandExecutor.cs                 # ExecutionOptions.PreviewEffects
│   │   ├── ExecutionLog/                      # Origin field on context and DTO
│   │   └── StepThrough/
│   │       ├── IStepThroughService.cs
│   │       ├── StepThroughService.cs          # Sessions, lease, background run
│   │       ├── StepThroughSessionGuard.cs     # Owning queue lookup, pause, resume
│   │       └── StepThroughLeaseSweeper.cs     # Hosted service, 10 s sweep
│   └── Swagger/                               # Schema filter entries for new DTOs
└── web-ui/src/
    ├── components/stepthrough/
    │   ├── StepThroughPanel.tsx               # Step list, buttons, history
    │   └── useStepThrough.ts                  # Polling hook, lease, close beacon
    ├── pages/SequencesPage.tsx                # "Step through" entry, saved-only rule
    └── services/stepThrough.ts                # API client

tests/
├── unit/StepThrough/                          # Cursor, frames, preview, lease
├── integration/StepThrough/                   # Parity test with the real runner
└── contract/StepThrough/                      # Routes, errors, action-type list
src/web-ui/src/components/stepthrough/__tests__/
docs/architecture.md                           # Update
```

**Structure Decision**: Use the existing layout. Pure cursor logic goes in `GameBot.Domain` so unit tests need no
host. Session state, lease, queue pause, and routes go in `GameBot.Service`, next to the queue and sequence
services. The UI panel is a new component folder, opened from `SequencesPage.tsx`.
Test projects are `tests/unit`, `tests/integration`, and `tests/contract`.

## Key Design Points

1. **One step per call.** `SequenceStepper.RunNextAsync` evaluates the guard, delay, gate, and dispatch of one leaf
   with the leaf code of `SequenceRunner`. It then computes the next cursor from the frame stack (R1, R3).
2. **Same order as a real run.** A parity test runs fixtures through both paths and compares the visited paths (SC-002).
3. **No outside effects.** `PreviewEffects` skips `reschedule-self` and `notify` in both top-level and command
   steps. The history shows the intended effect (R4, FR-015, FR-015a).
4. **Session safety.** A step-through refuses to run while a queue run is active on the device. The author can pause
   the queue with the existing policy-pause gate. The lease sweeper always resumes it (R6, R7).
5. **No blocking HTTP calls.** `run-next` returns `202`. The view polls (R8).
6. **Saved sequence only.** The UI disables the entry for a new or edited sequence. The service checks a version hash
   on each call (R10).

## Risks

| Risk | Mitigation |
|------|-----------|
| Stepper and runner drift apart | Parity test. Shared `internal` helpers. A review note in the PR. |
| New action type skips the preview rule | Contract test lists every action type (R4). |
| Queue stays paused after a crash | Pause state is in memory in the same process, so a crash also clears it. The sweeper handles a lost view. |
| A firing that runs when the author pauses | `409 queue_run_active`. The step does not run until the firing ends (FR-012c). |
| Flow-graph sequences | Refused with `unsupported_sequence_kind` (R11). |

## Complexity Tracking

No constitution violation needs a justification.
