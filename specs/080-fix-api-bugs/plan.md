# Implementation Plan: Fix Sequence & Session-Input API Bugs

**Branch**: `080-fix-api-bugs` | **Date**: 2026-09-11 | **Spec**: [spec.md](spec.md)
**Input**: Feature specification from `/specs/080-fix-api-bugs/spec.md`

## Summary

Fix three real platform defects surfaced by an external consumer's bug tracker
(`C:\src\PNS\docs\api-bugs.md`, tracker IDs B-001, B-003, B-005):

1. **B-003** — `POST /api/sequences` (create) and `PUT /api/sequences/{id}` (replace)
   silently accept a `Command`-typed action step whose payload has no resolvable
   `commandId`, defaulting the dispatch target to the step's own `stepId` instead of
   failing validation. The same gap exists for steps nested inside `Loop`/`If`
   bodies. Fix: extend `SequenceStepValidationService` (and the matching gate in
   `FileSequenceRepository.ValidateActionPayloads`) to reject such steps at both
   top level and inside `MapBodySteps`-produced nested bodies.
2. **B-005** — `requireDispatch: true` is silently dropped for any step nested
   inside a `Loop` or `If` body because `SequencesEndpoints.MapBodySteps` never
   copies `RequireDispatch` from the request DTO onto the mapped domain
   `SequenceStep`. The execution engine (`SequenceRunner`) already honors the flag
   correctly once it's present. Fix: copy `RequireDispatch` in `MapBodySteps`,
   mirroring the existing top-level mapping in `MapToLinearSteps`.
3. **B-001** — `POST /api/sessions/{id}/inputs` reports a misleading
   `409 not_running` whenever every posted action fails to parse/dispatch
   (regardless of session health), because `SessionsEndpoints` derives its verdict
   from `accepted == 0` instead of the session's real status. Fix: add a new
   `SessionManager` method that shares the existing dispatch loop but also returns
   per-action outcomes (the existing `SendInputsAsync(...) -> Task<int>` and its
   seven other in-tree callers are untouched — they only ever needed the count),
   and have the endpoint use the session's actual status plus those per-action
   results to choose between `409` (truly not running), `400` (zero actions
   dispatched due to bad input), and the existing `202` (some/all dispatched) —
   the `202` case additionally reports per-action failures instead of only a count.

## Technical Context

**Language/Version**: C# / .NET 9.0
**Primary Dependencies**: ASP.NET Core Minimal APIs (GameBot.Service), no new
third-party dependencies required
**Storage**: File-backed repositories (`FileSequenceRepository` et al.) — unchanged
**Testing**: xUnit 2.7.1 across `tests/unit`, `tests/integration`, `tests/contract`
(GameBot.UnitTests / GameBot.IntegrationTests / GameBot.ContractTests)
**Target Platform**: Windows service host (existing GameBot.Service)
**Project Type**: Backend web service (single solution, layered: Domain / Emulator /
Service)
**Performance Goals**: No new performance budget — these are correctness fixes on
existing low-frequency, non-hot-path request handlers (sequence authoring, ad-hoc
session input); no measurable latency/throughput change expected.
**Constraints**: MUST NOT change behavior for any already-well-formed request
(regression-free for the happy path in all three bugs, per spec FR-004, FR-008,
FR-012).
**Scale/Scope**: Three targeted defect fixes confined to
`GameBot.Domain/Services/SequenceStepValidationService.cs`,
`GameBot.Domain/Commands/FileSequenceRepository.cs`,
`GameBot.Service/Endpoints/SequencesEndpoints.cs`,
`GameBot.Emulator/Session/SessionManager.cs`,
`GameBot.Service/Endpoints/SessionsEndpoints.cs`, plus their existing test
counterparts. No new endpoints, entities, or persisted schema changes.

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

*NON-NEGOTIABLE*: If `build` or required `test` runs are failing (local or CI),
implementation progression is blocked until failures are fixed or a documented
maintainer waiver exists.

- **I. Code Quality Discipline**: No new lint/static-analysis suppressions
  anticipated; changes are additive validation/mapping logic in existing,
  already-modular services. PASS.
- **II. Testing Standards**: Each bug fix requires a failing test added first that
  reproduces the bug through the real HTTP/domain path (not a hand-built domain
  object bypassing the buggy mapping layer, which is exactly how B-003/B-005 went
  undetected). PASS (gate honored by task design, not yet executed).
- **III. UX Consistency**: New/changed error responses reuse this codebase's
  existing error shapes (`Results.BadRequest(new { message, errors })` for
  validation, `Results.Conflict(new { error = new { code, message, hint } })` for
  session-state) rather than inventing a new contract. PASS.
- **IV. Performance**: No hot-path change; no new benchmark required. PASS.
- **V. Living Documentation**: `docs/architecture.md` will be checked for any
  description of these three endpoints' current (buggy) contract and updated in
  the same PR if it documents behavior this feature changes; spec Status line
  will be set to `Implemented` once implementation lands.

No violations requiring Complexity Tracking.

## Project Structure

### Documentation (this feature)

```text
specs/080-fix-api-bugs/
├── plan.md              # This file
├── research.md          # Phase 0 output
├── data-model.md         # Phase 1 output
├── quickstart.md         # Phase 1 output
├── contracts/            # Phase 1 output
└── tasks.md              # Phase 2 output (/speckit-tasks)
```

### Source Code (repository root)

```text
src/
├── GameBot.Domain/
│   ├── Services/SequenceStepValidationService.cs   # B-003: add commandId-presence gate
│   └── Commands/FileSequenceRepository.cs          # B-003: mirror gate in ValidateActionPayloads
├── GameBot.Service/
│   └── Endpoints/
│       ├── SequencesEndpoints.cs                    # B-003 + B-005: MapBodySteps fixes
│       └── SessionsEndpoints.cs                     # B-001: status-aware response selection
└── GameBot.Emulator/
    └── Session/SessionManager.cs                    # B-001: per-action outcome reporting

tests/
├── unit/Sequences/                                  # B-003, B-005 unit coverage
├── integration/Sequences/                           # B-003, B-005 through real HTTP mapping
└── integration/SessionInputTests.cs                 # B-001 coverage (incl. ADB-mode path)
```

**Structure Decision**: Existing single-solution layered structure
(`GameBot.Domain` / `GameBot.Emulator` / `GameBot.Service`, with `tests/unit`,
`tests/integration`, `tests/contract`) is unchanged. This feature adds no new
projects or top-level directories — all three fixes land in existing files, per
the research below.

## Complexity Tracking

*No Constitution Check violations — table intentionally omitted.*
