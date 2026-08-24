# Implementation Plan: Sequence & Command Parameters

**Branch**: `078-sequence-parameters` | **Date**: 2026-08-24 | **Spec**: [spec.md](./spec.md)
**Input**: Feature specification from `/specs/078-sequence-parameters/spec.md`

## Summary

Let one command and one sequence serve N emulator instances by making the value that varies a named
parameter instead of a literal. The approach reuses the `{{key}}` substitution machinery that already
exists for loop `{{iteration}}`, adds a layered immutable `ParameterScope` resolved innermost-first
(call-site binding → inherited ambient → declared default), and auto-exposes the four values a queue
already stores (`queue.emulatorSerial`, `queue.instanceName`, `queue.instanceIndex`, `queue.gameId`)
as read-only built-ins. The motivating three-emulator case therefore needs **zero new configuration** —
three queues already differ by serial. Declared parameters with defaults and per-call-site bindings
cover everything the queue does not already know, and queue-template entries may additionally supply
ad-hoc names that flow to any depth so intermediate sequences never re-declare a pass-through value.

Everything is additive and optional: absent declarations and absent bindings mean today's behaviour
byte-for-byte. Migration is manual and documented in [quickstart.md](./quickstart.md); no migration
code is written.

## Technical Context

**Language/Version**: C# / .NET 9 (`net9.0`), TypeScript 5 + React 18 (Vite) for `src/web-ui`
**Primary Dependencies**: ASP.NET Core Minimal APIs, `System.Text.Json`, OpenCvSharp (untouched here),
xUnit + FluentAssertions, Jest + React Testing Library
**Storage**: JSON files on disk via the existing `File*Repository` classes (commands, sequences,
queue templates); no database, no schema migration
**Testing**: `dotnet test` across `tests/unit`, `tests/integration`, `tests/contract`; `npm test`
(Jest) and `npm run build` (Vite) for `src/web-ui`
**Target Platform**: Windows 11 desktop service (`GameBot.Service`, port 8080) driving LDPlayer
emulators over ADB
**Project Type**: Web application — .NET backend + React SPA in one repository
**Performance Goals**: scope resolution + substitution < 1 ms per step dispatch; no measurable change
to queue-run wall-clock time (every step it precedes performs device I/O costing 10–100× more)
**Constraints**: no automatic migration; stored JSON without the new members must round-trip
byte-identically; concurrent access — the queue run loop mutates run state while the monitor thread
reads it, so the scope type must be immutable
**Scale/Scope**: ~20 sequences, ~60 commands, ~6 queue templates in the operator's live authoring
store; single operator, single machine

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-checked after Phase 1 design — see bottom of section.*

*NON-NEGOTIABLE*: If `build` or required `test` runs are failing (local or CI), implementation
progression is blocked until failures are fixed or a documented maintainer waiver exists.

| Principle | Status | How this feature satisfies it |
|---|---|---|
| **I. Code Quality Discipline** | PASS | New types are small and cohesive (`ParameterScope`, `ParameterDeclaration`, `ParameterBinding`, `ParameterNameRules`, `ParameterReferenceScanner`, `CommandStepResolver`), each well under the ~50 LOC/method bar. Every new public member gets XML docs with inputs, outputs and error modes. No new dependencies. `Program.cs` stays thin so the build-time taint analyzers do not blow up (known repo hazard). Method names are CamelCase, no underscores. |
| **II. Testing Standards** | PASS | Unit tests for scope resolution precedence, name rules, numeric coercion, the reference scanner and the step resolver; integration tests for the queue→template→sequence→command propagation chain and the queue-start refusal; contract tests for the additive DTO members. Target ≥80% line / ≥70% branch on touched areas. Two existing tests (`LoopValidationTests.TopLevelStepWithTemplatePlaceholderIsRejected`, `IfValidationTests.TemplatePlaceholderInTopLevelIfBranchIsRejected`) assert on `{{iteration}}` and must keep passing **unmodified** — that is the regression guard for research R4. |
| **III. UX Consistency** | PASS | Reuses the existing `ConfigParameterList`/`ConfigParameterRow`, `SearchableDropdown` and `FormField` patterns. Error messages follow a fixed, actionable form naming the parameter, field and step (contracts/api.md). API members are additive, so no breaking change and no version bump. |
| **IV. Performance** | PASS | Budget declared above (< 1 ms/step); a micro-benchmark under `tests/unit/Performance/` holds the ceiling. No hot-path allocation growth beyond one small scope node per invocation level. |
| **V. Living Documentation** | PASS | This change alters the domain model, the API surface and the persistence layout, so `docs/architecture.md` MUST be updated with a refreshed "Last reviewed" date in the same PR. `specs/STATUS.md` gets the new entry. No earlier spec is superseded — features 034 (loops/`TemplateSubstitutor`), 047 (queue templates) and 077 (entry toggle) are **extended**, not replaced, so their Status lines stay as they are. |

**Post-Phase-1 re-check**: PASS. The design added no new project, no new dependency, and no
constitutional exception. The Complexity Tracking table below is intentionally empty.

## Project Structure

### Documentation (this feature)

```text
specs/078-sequence-parameters/
├── spec.md               # Feature specification (with Clarifications)
├── plan.md               # This file
├── research.md           # Phase 0 — R1..R10 technical decisions
├── data-model.md         # Phase 1 — new/changed types, field-template paths, back-compat
├── quickstart.md         # Phase 1 — the manual migration guide (FR-034)
├── contracts/
│   └── api.md            # Phase 1 — additive REST contract changes
├── checklists/
│   └── requirements.md   # Spec quality checklist
└── tasks.md              # Phase 2 — created by /speckit-tasks, not by this command
```

### Source Code (repository root)

```text
src/GameBot.Domain/
├── Parameters/                          NEW — the whole mechanism lives here
│   ├── ParameterValueType.cs            NEW: text | number
│   ├── ParameterDeclaration.cs          NEW: name/type/default/required/description
│   ├── ParameterBinding.cs              NEW: name + value (null = inherit)
│   ├── ParameterScope.cs                NEW: immutable layered scope, TryResolve/Child/Describe
│   ├── ParameterNameRules.cs            NEW: identifier regex, reserved names, queue.* catalogue
│   ├── ParameterReferenceScanner.cs     NEW: find {{name}} refs + field paths in an entity
│   ├── CommandStepResolver.cs           NEW: apply a scope to a CommandStep (inline + overlay)
│   └── ParameterResolutionError.cs      NEW: unresolved / not_a_number
├── Utils/TemplateSubstitutor.cs         CHANGED: dotted-name regex + strict TrySubstitute
├── Commands/Command.cs                  CHANGED: + Parameters
├── Commands/CommandStep.cs              CHANGED: + FieldTemplates, + ParameterBindings
├── Commands/CommandSequence.cs          CHANGED: + Parameters (writable-collection pattern)
├── Commands/SequenceStep.cs             CHANGED: + ParameterBindings
├── Commands/FileCommandRepository.cs    CHANGED: persist/validate new members
├── Commands/FileSequenceRepository.cs   CHANGED: persist + ValidateActionPayloads parity (R7)
├── QueueTemplates/QueueTemplateEntry.cs CHANGED: + ParameterValues
├── Services/SequenceRunner.cs           CHANGED: scope param; ApplyIterContext → ApplyScope,
│                                                 also applied on the top-level step path
├── Services/SequenceStepValidationService.cs CHANGED: narrow the loop-only rule to `iteration` (R4)
└── Services/ParameterValidationService.cs    NEW: save-time + pre-run validation

src/GameBot.Service/
├── Services/CommandExecutor.cs          CHANGED: accept scope, resolve steps before dispatch
├── Services/ICommandExecutor.cs         CHANGED: scope on force-execute overloads
├── Services/SequenceExecution/SequenceExecutionService.cs CHANGED: scope param + command bindings
├── Services/QueueExecution/QueueExecutionService.cs       CHANGED: build root scope from the queue,
│                                                                   layer entry values, pre-run check
├── Services/QueueExecution/QueueRunHandle.cs              CHANGED: SelfRescheduleEntry carries scope
├── Services/ExecutionLog/ExecutionLogService.cs           CHANGED: record resolvedParameters
├── Models/Commands.cs                   CHANGED: + ParameterDeclarationDto, step members
├── Models/SequenceStepContracts.cs      CHANGED: + parameterBindings
├── Contracts/QueueTemplates/*.cs        CHANGED: + parameterValues, warnings, effectiveParameters
├── Endpoints/CommandsEndpoints.cs       CHANGED: validation + /parameter-scope
├── Endpoints/SequencesEndpoints.cs      CHANGED: validation + /parameter-scope + execute body
├── Endpoints/QueueTemplatesEndpoints.cs CHANGED: entry values + warning projection
└── Endpoints/QueuesEndpoints.cs         CHANGED: start-time required-parameter refusal

src/web-ui/src/
├── components/parameters/               NEW
│   ├── ParameterDeclarationList.tsx     NEW: the Parameters section
│   ├── ParameterizableField.tsx         NEW: input + { } insert-parameter picker
│   ├── ParameterBindingForm.tsx         NEW: callee declarations, inherit-by-default, preview
│   └── useParameterScope.ts             NEW: fetch + cache /parameter-scope
├── components/commands/CommandForm.tsx  CHANGED: Parameters section + parametrizable fields
├── components/commands/*Panel.tsx       CHANGED: fields wrapped in ParameterizableField
├── components/SortableSequenceStepList.tsx CHANGED: per-step binding form
├── components/queues/QueueEntryList.tsx    CHANGED: override badge (FR-030)
├── components/queues/SchedulingSequenceCard.tsx CHANGED: entry parameter form + preview
├── services/commands.ts, sequences.ts, queueTemplates.ts CHANGED: types + new endpoints
└── lib/validation.ts                    CHANGED: surface parameter errors inline

tests/
├── unit/Parameters/                     NEW: scope, name rules, scanner, step resolver, coercion
├── unit/Performance/ParameterScopeBenchmarkTests.cs NEW: < 1 ms/step ceiling
├── unit/Sequences/                      CHANGED: substitution + narrowed loop-only rule
├── integration/Queues/                  NEW: queue→entry→sequence→command propagation; start refusal
├── contract/ApiContractSnapshots/       CHANGED: regenerate for additive members
└── web-ui __tests__                     NEW: picker, binding form, inline validation

docs/architecture.md                     CHANGED: domain model + API surface + persistence (Principle V)
specs/STATUS.md                          CHANGED: add 078
```

**Structure Decision**: The existing four-project layout is kept unchanged
(`GameBot.Domain` / `GameBot.Emulator` / `GameBot.Service` / `web-ui`, with `tests/{unit,integration,contract}`).
The mechanism is placed in `GameBot.Domain/Parameters/` because resolution is pure domain logic with
no I/O, which keeps it directly unit-testable and keeps `GameBot.Service` responsible only for
*supplying* scope layers (queue built-ins, entry values) and for threading them through execution.

## Implementation Sequencing

Ordered so each stage is independently verifiable and maps onto the spec's prioritized user stories.

| Stage | Delivers | Spec coverage |
|---|---|---|
| 1. Domain core | `Parameters/*`, `TemplateSubstitutor` widening, name rules, scanner, step resolver + unit tests | FR-001..003, FR-005..009, FR-017..019 |
| 2. Persistence & model | New members on `Command`, `CommandStep`, `CommandSequence`, `SequenceStep`, `QueueTemplateEntry`; repository round-trip tests proving byte-identical output when empty | FR-004, FR-032 |
| 3. Execution threading | `SequenceRunner` scope + top-level `ApplyScope`; `CommandExecutor`/`SequenceExecutionService` scope params; `QueueExecutionService` root scope + entry layer; self-reschedule scope | **US1**, FR-010..016, FR-024 |
| 4. Validation | `ParameterValidationService`; narrowed loop-only rule; both sequence validators in sync; endpoint wiring; queue-start refusal | **US2**, FR-020..023a |
| 5. API surface | DTO members, `/parameter-scope` endpoints, template warnings, execute-with-parameters | contracts/api.md |
| 6. Web UI | The three new components + editor/queue/template wiring | **US3**, FR-025..031 |
| 7. Docs | `docs/architecture.md` refresh, `specs/STATUS.md`, quickstart already written | **US4**, FR-033, FR-034, Principle V |

**US1 is shippable after stage 3** and already collapses N commands and sequences into one — matching
the spec's claim that the P1 story alone is a viable MVP.

## Risks & Mitigations

| Risk | Mitigation |
|---|---|
| The current validator rejects all top-level `{{...}}`, silently blocking the feature | Narrowed in stage 4; the two existing `{{iteration}}` tests are the guard and must pass unmodified (research R4) |
| The two sequence validators drift (`SequenceStepValidationService` vs `FileSequenceRepository.ValidateActionPayloads`) — a known repo hazard that produces a 500 | Stage 4 adds the check to both, with a test that saves a parametrized sequence through the real endpoint |
| `ApplyScope`'s step clone silently drops a new field, so bindings vanish inside loop bodies | The clone at SequenceRunner.cs:1308 enumerates members explicitly; a loop-body binding test covers it |
| A parametrized numeric field reaches a device action as text | Whole-field-only rule for numeric fields plus explicit coercion failure (FR-019); no silent fallback |
| Contract snapshot tests fail on the additive members | Regenerated deliberately in stage 5 and reviewed as part of the diff |
| Widened placeholder regex changes existing matching | `\w+(?:\.\w+)*` is a strict superset of `\w+`; existing `TemplateSubstitutor` tests cover the old behaviour |

## Complexity Tracking

> No constitutional violations. No new project, dependency, or exception was required.
