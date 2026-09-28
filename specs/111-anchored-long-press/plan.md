# Implementation Plan: Press and hold at a detected point (anchored long press)

**Branch**: `claude/resolve-github-issue-fr-009-3y4vaw` (spec number 111) | **Date**: 2026-09-28 | **Spec**: [spec.md](spec.md)
**Input**: Feature specification from `specs/111-anchored-long-press/spec.md`

## Summary

A `PrimitiveTap` step gets an optional `holdMs` (integer, 0 to 5000). Today the DTO has no such field, so the service drops it (research R-001). The plan adds the field to the API DTO and to the domain configuration, maps it on save, read-back and single-step execute, and rejects a value outside the range with 400 (R-003, R-004). The parameter resolver copies it (R-005).

At run time, the tap of today is one swipe input with the same start point and end point and a duration of 200 ms. With `holdMs > 0`, the duration is `holdMs` (R-002). A small helper makes this choice, so that a Linux unit test can check it. The step outcome and the execution log `tap` detail show the hold duration (R-006). The OpenAPI document shows the field with its range and a description (R-007). The web UI tap editor gets a hold duration input (R-008). `docs/architecture.md`, `CHANGELOG.md`, `specs/STATUS.md` and `specs/openapi.json` tell the change.

## Technical Context

**Language/Version**: C# 13 on .NET 9 (`net9.0`); TypeScript and React for the web UI  
**Primary Dependencies**: ASP.NET Core minimal APIs, Swashbuckle, xUnit, FluentAssertions; Jest and Testing Library for the web UI  
**Storage**: File-backed command repository (JSON). One new optional field, not written when null. Stored commands do not change.  
**Testing**: xUnit unit tests (`tests/unit`), integration tests with `WebApplicationFactory` (`tests/integration`), contract tests (`tests/contract`), Jest (`src/web-ui`)  
**Target Platform**: Windows service (CI on `windows-latest`). Image detection runs only on Windows.  
**Project Type**: Web service with a web UI  
**Performance Goals**: No change. A press and hold takes `holdMs` on the device, as the author sets it.  
**Constraints**: No change to the tap without a hold, to the `Swipe` step, or to the session jitter (spec FR-005, FR-011). No `fieldTemplates` key for `holdMs` (FR-012).  
**Scale/Scope**: About 9 service and domain files, 1 new schema filter, 5 web UI files, 5 to 6 test files, docs.

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

*NON-NEGOTIABLE*: If `build` or required `test` runs are failing (local or CI), implementation progression is blocked until failures are fixed or a documented maintainer waiver exists.

*NON-NEGOTIABLE*: All text in this plan and in the artifacts it produces (research, data model, contracts, quickstart, tasks, code comments, user-facing messages) MUST obey Simplified Technical English (Constitution Principle VI).

| Gate | Status | Note |
|------|--------|------|
| Code quality (analyzers, `-warnaserror`) | Pass | Small change in existing style. Test method names in CamelCase, no underscores. |
| Tests first, regression test for the bug | Pass | The read-back test and the range test fail before the change (the field is dropped, and no range check exists). |
| UX / API consistency | Pass | Same error text form as other step rules. Same log detail kind `tap`. |
| Performance | Pass | No new work on the hot path. One more integer in the input. |
| Backward compatibility | Pass | An absent `holdMs` gives the same input, the same stored JSON, the same response and the same log as before. |
| Living docs | Pass | `docs/architecture.md` gets the field and a new "Last reviewed" date. `CHANGELOG.md`, `specs/STATUS.md` and `specs/openapi.json` change too. |
| Language (STE) | Pass | All new text in STE. |

Post-design re-check: Pass. No violations.

## Project Structure

### Documentation (this feature)

```text
specs/111-anchored-long-press/
├── plan.md              # This file
├── research.md          # Phase 0 output
├── data-model.md        # Phase 1 output
├── quickstart.md        # Phase 1 output
├── contracts/
│   └── primitive-tap-hold.md
├── checklists/
│   └── requirements.md  # Spec quality checklist
└── tasks.md             # Phase 2 output
```

### Source Code (repository root)

```text
src/GameBot.Domain/Commands/CommandStep.cs
    # PrimitiveTapConfig.HoldMs, MinHoldMs, MaxHoldMs, DefaultTapDurationMs
src/GameBot.Domain/Parameters/CommandStepResolver.cs
    # copy HoldMs into the resolved PrimitiveTapConfig
src/GameBot.Service/Models/Commands.cs
    # PrimitiveTapConfigDto.HoldMs ([Range], WhenWritingNull); StepExecutionOutcomeDto.HoldMs
src/GameBot.Service/Endpoints/CommandsEndpoints.cs
    # ValidateStep range rule; ToDomainPrimitiveTap / ToResponsePrimitiveTap; ToResponseOutcome
src/GameBot.Service/Endpoints/StepsEndpoints.cs
    # ValidateStep range rule; ToDomainStep; ToResponseOutcome
src/GameBot.Service/Services/ICommandExecutor.cs
    # PrimitiveTapStepOutcome.HoldMs
src/GameBot.Service/Services/PrimitiveTapInput.cs
    # new: builds the swipe input for a tap or a press and hold
src/GameBot.Service/Services/CommandExecutor.cs
    # TryDetectAndTap uses PrimitiveTapInput; sets HoldMs on the outcome
src/GameBot.Service/Services/ExecutionLog/ExecutionLogService.cs
    # tap detail text and holdMs attribute for a press and hold
src/GameBot.Service/Swagger/PrimitiveTapHoldSchemaFilter.cs
    # new: description of holdMs
src/GameBot.Service/GameBotServiceSetup.cs
    # register the schema filter

src/web-ui/src/services/commands.ts                          # PrimitiveTapConfigDto.holdMs
src/web-ui/src/components/commands/TapPanel.tsx              # hold duration input and check
src/web-ui/src/components/commands/CommandForm.tsx           # StepEntry.primitiveTap.holdMs, panel wiring, list text
src/web-ui/src/pages/CommandsPage.tsx                        # map holdMs to and from the DTO
src/web-ui/src/components/commands/__tests__/TapPanel.test.tsx

tests/unit/Commands/PrimitiveTapInputTests.cs                # new (Linux-safe)
tests/unit/Commands/CommandExecutorPrimitiveTapTests.cs      # hold tests (Windows)
tests/unit/Parameters/CommandStepResolverTests.cs            # HoldMs survives resolution
tests/integration/Commands/PrimitiveTapHoldIntegrationTests.cs   # new: save, read-back, range, steps/execute
tests/integration/ExecutionLogs/CommandExecutionLoggingIntegrationTests.cs  # log text and attribute
tests/contract/PrimitiveTapHoldOpenApiTests.cs               # new: OpenAPI field, range, description

docs/architecture.md
CHANGELOG.md
specs/STATUS.md
specs/openapi.json
CLAUDE.md
```

**Structure Decision**: Use the existing projects. The field lives in `GameBot.Domain` (configuration, resolver) and `GameBot.Service` (DTOs, endpoints, executor, execution log, OpenAPI). The web UI change stays in the existing tap editor.

## Complexity Tracking

No violations.
