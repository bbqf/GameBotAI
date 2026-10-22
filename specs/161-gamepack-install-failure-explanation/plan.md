# Implementation Plan: Gamepack Install Failure Explanation

**Branch**: `161-gamepack-install-failure-explanation` | **Date**: 2026-10-22 | **Spec**: [spec.md](spec.md)
**Input**: Feature specification from `/specs/161-gamepack-install-failure-explanation/spec.md`

## Summary

A failed gamepack install or dry run must explain why it fails and what resolves it (issue #337, FR-034). The install
answer gets a `summary` object before `problems`. The summary has a headline and one entry for each problem category, in
the fixed order structure, signature, reference, conflict. An entry has the category, count, label, remedy, and the flag
`resolvedByTakeOver`. Each problem gets `takeOverResolves` and `remedy`. The answer gets `wouldInstallWithTakeOver`. The
service sets it with a second validator run (take-over on, same inputs) only for a failed check with take-over off. A
pure domain class `GamepackFailureExplainer` holds the rules and the STE text. The Gamepacks page shows the server
summary for a failed dry run, install, or update, and a fallback cause for a failure with no problem list. No problem
blocks an install that did not block it before (FR-009). Take-over does not change (FR-010).

## Technical Context

**Language/Version**: C# on .NET (service, domain), TypeScript with React (web UI in `src/web-ui`)
**Primary Dependencies**: ASP.NET Core minimal APIs, Swashbuckle (OpenAPI schema filter), React, Jest. No new dependency.
**Storage**: N/A. The feature stores no data. The second validator run has no write.
**Testing**: xUnit (`tests/unit`, `tests/contract`, `tests/integration`), Jest with React Testing Library (`npm test`)
**Target Platform**: Windows service with web UI. Desktop and phone browsers.
**Project Type**: Web application (back end and front end)
**Performance Goals**: No change. One extra validator run only for a failed check with take-over off.
**Constraints**: Answer fields are additions only (FR-011). Phone support level full (`docs/mobile-ui-principles.md`):
no horizontal scroll at 375 px.
**Scale/Scope**: One new domain class, two DTO records, one outcome field, two endpoint files, one page, one service file.

## Constitution Check

*GATE: Passed before Phase 0. Re-checked after Phase 1.*

- Code quality (I): one small pure class, no new dependency, no change to validator rules. Pass.
- Testing (II): unit, contract, integration, and Jest tests (see Test Plan). Build and tests must pass before progress. Existing
  install and take-over tests stay green with no change to expected results (FR-015). Pass.
- UX consistency (III): Gamepacks page change. Phone support level: full. It follows `docs/mobile-ui-principles.md`.
  The notice wraps, adds no touch target, and has no horizontal scroll. A manual check at 375 px goes in the PR text. Pass.
- Performance (IV): no change to a valid or successful path. Pass.
- Living documentation (V): `docs/gamepacks.md` is updated. `docs/architecture.md` is checked and updated with "Last reviewed"
  if a statement changes. `specs/STATUS.md` is updated in the same PR (FR-014). Pass.
- STE (VI): all artifacts, code comments, remedies, labels, and page strings use Simplified Technical English. Pass.
- Version bump: task T028 bumps `installer/versioning/version.override.json` before the push (FR-013). Pass.

Post-design check: no violation. Complexity Tracking is not needed.

## Project Structure

### Documentation (this feature)

```text
specs/161-gamepack-install-failure-explanation/
├── plan.md
├── research.md
├── data-model.md
├── quickstart.md
├── contracts/
│   └── install-response.md
├── checklists/
│   └── requirements.md
└── tasks.md             # Created by /speckit-tasks
```

### Source Code (repository root)

```text
src/GameBot.Domain/Gamepacks/
├── GamepackFailureExplainer.cs                  # NEW: Explain and Summarize rules, STE text
└── GamepackService.cs                           # Outcome gets WouldInstallWithTakeOver; second validator run

src/GameBot.Service/
├── Endpoints/
│   ├── Dto/GamepackDtos.cs                      # Summary DTOs; problem DTO and install response get new members
│   ├── GamepacksEndpoints.cs                    # ToInstallResult gets the take-over flag; summary and field; route text
│   ├── GamepackCatalogEndpoints.cs              # Passes the take-over flag to ToInstallResult; route text
│   └── Dto/ManagedUpdateReportDto.cs            # Uses the problem DTO (default take-over flag); check only
└── Swagger/
    ├── GamepackSchemaFilter.cs                  # STE descriptions of new members and schemas
    └── SwaggerConfig.cs                         # Example of the new answer

src/web-ui/src/
├── services/
│   ├── gamepackCatalog.ts                       # Result types; CatalogInstallRejectedError for 422
│   └── __tests__/gamepackCatalog.test.ts        # 422 error tests
└── pages/
    ├── GamepacksPage.tsx                        # summaryText helper; failure notice for dry run, install, update
    ├── GamepacksPage.css                        # Summary list style (wraps, no new touch target)
    └── __tests__/GamepacksPage.test.tsx         # Page tests

tests/
├── unit/Gamepacks/
│   ├── GamepackFailureExplainerTests.cs         # NEW
│   └── GamepackServiceTakeOverOutcomeTests.cs   # NEW: WouldInstallWithTakeOver cases
├── contract/Gamepacks/
│   ├── GamepackInstallFailureExplanationContractTests.cs  # NEW
│   └── GamepackOpenApiTests.cs                  # New members and schemas have descriptions
└── integration/Gamepacks/
    └── GamepackInstallIntegrationTests.cs       # Failed install leaves the instance unchanged; summary returned

docs/gamepacks.md                                # New answer fields and page notice
docs/architecture.md                             # Check only; update if a statement changes
specs/STATUS.md                                  # Row for feature 161 (final task)
specs/161-gamepack-install-failure-explanation/spec.md  # Status set to Implemented (final task)
installer/versioning/version.override.json       # Version bump before push (FR-013)
```

**Structure Decision**: Back end and front end. The rules are in the domain. The endpoints map them. The page shows the
server text. The validator (`GamepackValidator`) and the applier do not change.

## Test Plan

- Explainer (unit): counts and order; singular and plural labels; empty list; user conflict resolves; second user conflict with
  the same name resolves; other-gamepack conflict, reference, signature, structure (API major version) do not; request with
  take-over on gives false; `update_in_progress` has its own remedy; `resolvedByTakeOver` is true only if all problems in the
  entry resolve.
- Service (unit): `WouldInstallWithTakeOver` for 20 user conflicts, conflict plus reference, valid file, other-gamepack conflict,
  take-over on with remaining problem, invalid JSON, signature rejected, two user objects with one name. The second run writes nothing.
- Contract: `summary` before `problems` in raw JSON; dry run (200) and failed install (422) give equal explanation for both routes;
  rollback refused answer and managed report have the problem fields and no summary; status codes, 500, and 413 do not change.
- OpenAPI: new members and schemas have descriptions.
- Integration: failed install leaves stored objects unchanged and returns the summary.
- Jest: 422 throws `CatalogInstallRejectedError` (still a `CatalogApiError`); summary shown for failed dry run, update, and install;
  take-over sentence follows `wouldInstallWithTakeOver`; empty-reason failure shows the fallback; old-server answer without
  `summary` still shows problem messages.
- Regression: existing install and take-over tests pass with no change (FR-015, SC-004).

## Phase 0 and Phase 1 Output

- research.md: decisions D1 to D10.
- data-model.md: problem facts, summary, outcome field, explainer, web UI types.
- contracts/install-response.md: answer shape, rules, remedy text.
- quickstart.md: API check, page check, test commands.
