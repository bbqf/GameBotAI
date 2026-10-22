---

description: "Task list for Gamepack Install Failure Explanation"
---

# Tasks: Gamepack Install Failure Explanation

**Input**: Design documents from `/specs/161-gamepack-install-failure-explanation/`
**Prerequisites**: plan.md, spec.md, research.md, data-model.md, contracts/install-response.md, quickstart.md

**Tests**: The constitution needs tests (plan.md, Principle II). Test tasks are included. The explainer tests come after the explainer.

**Organization**: Tasks are grouped by user story. All text is in ASD-STE100 Simplified Technical English.

**Order rule**: The take-over flag parameter for the summary builder call and the response mapping is added in the FIRST back end task (T003). No later task adds it. No task uses the flag before T003.

## Format: `[ID] [P?] [Story] Description`

- **[P]**: Can run in parallel (different files, no dependency on an incomplete task)
- **[Story]**: User story (US1 to US4)

## Phase 1: Setup

**Purpose**: Confirm the baseline before any change.

- [X] T001 Run `dotnet build` for the solution in `C:\src\GameBot` and the web UI tests `GamepacksPage.test.tsx` and `gamepackCatalog.test.ts` in `C:\src\GameBot\src\web-ui`. Record that all pass. This is the baseline for SC-004 and FR-015.
- [X] T002 [P] Read `src/GameBot.Domain/Gamepacks/GamepackProblem.cs`, `src/GameBot.Domain/Gamepacks/GamepackService.cs`, `src/GameBot.Service/Endpoints/GamepacksEndpoints.cs`, `src/GameBot.Service/Endpoints/GamepackCatalogEndpoints.cs`, and `src/GameBot.Service/Endpoints/Dto/GamepackDtos.cs`. Note the exact names of `ToInstallResult`, `ToInstallResponse`, and `GamepackProblemDto.From`, every caller of them, and where the take-over flag of the request (`options.TakeOver`) is available.

---

## Phase 2: Foundational (blocking prerequisites)

**Purpose**: The take-over flag parameter, the shared domain rules, the outcome field, and the DTO shape. All user stories need these.

- [X] T003 FIRST back end task. In `src/GameBot.Service/Endpoints/GamepacksEndpoints.cs`, add the parameter `bool takeOver` to `ToInstallResult` and `ToInstallResponse` (the response mapping). Update every caller, in the same file and in `src/GameBot.Service/Endpoints/GamepackCatalogEndpoints.cs`, to pass `options.TakeOver`. The mapping holds the flag for the summary builder and the per-problem mapping, which later tasks add. Do not change any answer yet. Build and run the existing gamepack tests: the results must not change.
- [X] T004 [P] Create `src/GameBot.Domain/Gamepacks/GamepackFailureExplainer.cs`: a static pure class with `Explain(GamepackProblem, bool takeOverOn)` (returns `TakeOverResolves` and `Remedy`) and `Summarize(IReadOnlyList<GamepackProblem>, bool takeOverOn)` (returns headline and entries in the order structure, signature, reference, conflict). Both methods take the flag from the start. Use the rules in research.md D3, D5, D6 and the remedy table in contracts/install-response.md. Use the singular label for count 1. An empty list gives headline `No problems.` and no entries. `TakeOverResolves` is true only for a conflict with owner `user`, no `Code`, and `takeOverOn` false. All text in STE.
- [X] T005 [P] Add the property `bool WouldInstallWithTakeOver` to `GamepackInstallOutcome` in `src/GameBot.Domain/Gamepacks/GamepackService.cs`.
- [X] T006 [P] In `src/GameBot.Service/Endpoints/Dto/GamepackDtos.cs`, add `GamepackInstallSummaryDto` (headline, entries) and `GamepackInstallSummaryEntryDto` (category, count, label, remedy, resolvedByTakeOver). Add `TakeOverResolves` (bool) and `Remedy` (string) to `GamepackProblemDto`. Add `Summary` (declared before `Problems`) and `WouldInstallWithTakeOver` to `GamepackInstallResponse`. Do not remove or rename a member.
- [X] T007 Add a `takeOverOn` parameter to `GamepackProblemDto.From` in `src/GameBot.Service/Endpoints/Dto/GamepackDtos.cs` (default false). It calls `GamepackFailureExplainer.Explain(problem, takeOverOn)` and fills `TakeOverResolves` and `Remedy`. The rollback refused answer and the managed update report (`ManagedUpdateReportDto.cs`) use the default. Depends on T004 and T006.
- [X] T008 Write unit tests in `tests/unit/Gamepacks/GamepackFailureExplainerTests.cs`. This task depends on T004 (the explainer tests come after the explainer). Cases: counts and order for mixed categories; singular and plural labels; empty list; user conflict resolves; second user conflict with the same name resolves; other-gamepack conflict does not; reference, signature, and structure do not; `takeOverOn` true makes `TakeOverResolves` false; the `update_in_progress` conflict has its own remedy; the API major version problem uses the structure remedy; `resolvedByTakeOver` is true only if every problem in the entry resolves.

**Checkpoint**: The flag parameter, the explainer, and the DTO shape exist. Unit tests in T008 pass.

---

## Phase 3: User Story 1 - A dry run explains the failure and the remedy (Priority: P1) MVP

**Goal**: A dry run that is not valid has a `summary` first and a `wouldInstallWithTakeOver` field.

**Independent Test**: Dry run without take-over of a file with 20 user-owned conflicts. The summary comes first with count 20 and the remedy. `wouldInstallWithTakeOver` is true.

- [X] T009 [US1] In `GamepackService.InstallAsync` (`src/GameBot.Domain/Gamepacks/GamepackService.cs`), set `WouldInstallWithTakeOver`: true for a valid check; false when `options.TakeOver` is on and the check is invalid. When the check is invalid and take-over is off, run the validator a second time in the same gate: `ValidateAsync` with the SAME inputs as the first run (same gamepack file, same signature policy, `seeds: true`) and only `takeOver: true` changed. Use its `IsValid` as the value. Set false for invalid JSON and signature rejection (no second run). The second run must not write. Do not change which problems block an install (FR-009). Depends on T005.
- [X] T010 [US1] In `src/GameBot.Service/Endpoints/GamepacksEndpoints.cs`, in `ToInstallResult` / `ToInstallResponse`, set `Summary` (from `GamepackFailureExplainer.Summarize(problems, takeOver)` using the `takeOver` parameter from T003) and `WouldInstallWithTakeOver` (from the outcome) for the 200, 400, and 422 answers. The catalog install route uses the same function. Do not change a status code. Depends on T004, T006, T009.
- [X] T011 [P] [US1] Add service tests in `tests/unit/Gamepacks/GamepackServiceTakeOverOutcomeTests.cs`: `WouldInstallWithTakeOver` for (a) 20 user conflicts, (b) conflict plus missing reference, (c) valid file, (d) other-gamepack conflict, (e) take-over on with a remaining reference problem, (f) invalid JSON, (g) signature rejected, (h) two user objects with the same name. Check that the second run uses the same inputs as the first run except take-over, and that it causes no write.
- [X] T012 [P] [US1] Write contract tests in `tests/contract/Gamepacks/GamepackInstallFailureExplanationContractTests.cs` for `POST /api/gamepacks/install?dryRun=true`: `summary` comes before `problems` in the raw JSON; 20 conflicts give one entry with count 20 and the take-over remedy; `wouldInstallWithTakeOver` is true; a conflict plus a missing reference give two entries (reference, then conflict) and false; a valid file gives headline `No problems.`, empty entries, and true.

**Checkpoint**: US1 works alone through the API.

---

## Phase 4: User Story 2 - Each problem says if take-over resolves it (Priority: P1)

**Goal**: Every problem has `takeOverResolves` and `remedy`.

**Independent Test**: One user conflict, one missing reference, one signature problem: the first says true, the other two say false, all have a remedy.

- [X] T013 [US2] In `src/GameBot.Service/Endpoints/GamepacksEndpoints.cs`, map each problem in `ToInstallResponse` with `GamepackProblemDto.From(problem, takeOver)` (from T007), using the `takeOver` parameter that T003 added. A request with take-over on then gives `takeOverResolves` false for each remaining problem. Do not add a new parameter. Depends on T007 and T010.
- [X] T014 [P] [US2] Add contract tests in `tests/contract/Gamepacks/GamepackInstallFailureExplanationContractTests.cs`: user-owned conflict is true; other-gamepack conflict, reference, signature, and structure (including API major version) are false and have a non-empty remedy; a request with take-over on gives false for each remaining problem; the rollback refused answer and the managed update report have the two new fields and no `summary`. Depends on T013.

**Checkpoint**: US1 and US2 work.

---

## Phase 5: User Story 3 - A failed install gives the same explanation (Priority: P1)

**Goal**: The 422 answer of a real install equals the dry run for summary, problems, and take-over field.

**Independent Test**: Install the same file with and without dry run. The summary and the problem facts are equal. The instance does not change.

- [X] T015 [P] [US3] Add contract tests in `tests/contract/Gamepacks/GamepackInstallFailureExplanationContractTests.cs`: dry run (200) and real install (422) of the same file give equal `summary`, `problems`, and `wouldInstallWithTakeOver`; the 422 changes nothing in the instance; the same checks for `POST /api/gamepacks/catalog/install`; status codes and the write-failure (500) and 413 answers do not change. Depends on T013.
- [X] T016 [P] [US3] Extend the existing gamepack install integration test file in `tests/integration/Gamepacks/GamepackInstallIntegrationTests.cs`: a failed install without take-over of a file with user-owned conflicts leaves the stored objects unchanged and returns the summary. Depends on T013.

**Checkpoint**: US1 to US3 work at the API.

---

## Phase 6: User Story 4 - The Gamepacks page shows the reason when an update fails (Priority: P1)

**Goal**: The page shows the summary for a failed dry run, install, or update, and never an empty reason.

**Independent Test**: Update a gamepack with user-owned conflicts on the page. The notice shows counts and remedies. A network error shows a non-empty cause.

- [X] T017 [US4] In `src/web-ui/src/services/gamepackCatalog.ts`, extend `CatalogInstallResult` with optional `summary`, `wouldInstallWithTakeOver`, and per problem `takeOverResolves` and `remedy`. Add `CatalogInstallRejectedError extends CatalogApiError` with a `result` member. For a 422, throw it with the full body. Keep `code` and `message` as before.
- [X] T018 [US4] In `src/web-ui/src/pages/GamepacksPage.tsx`, add a helper `summaryText(result)` that makes one line per summary entry: `<count> <label>. <remedy>`. A dry run that is not valid shows the summary lines and `Take-over would resolve all problems.` or `Take-over would not resolve all problems.`. A valid dry run shows the headline `No problems.` in the dry run notice. A failed install or update (rejected error) shows the same summary lines. Any other error shows the error text; if it is empty, show `The cause is not known. See the service log.` Show the per-problem messages only when the summary is missing. Use the existing notice element (`role="alert"` for errors). Depends on T017.
- [X] T019 [P] [US4] In `src/web-ui/src/pages/GamepacksPage.css`, add a small style for the summary list: plain list, text wraps, no fixed width, no new touch target (phone support level full, `docs/mobile-ui-principles.md`).
- [X] T020 [P] [US4] Extend `src/web-ui/src/services/__tests__/gamepackCatalog.test.ts`: a 422 throws `CatalogInstallRejectedError` with the body; `instanceof CatalogApiError` still holds; `code` and `message` are unchanged. Depends on T017.
- [X] T021 [P] [US4] Extend `src/web-ui/src/pages/__tests__/GamepacksPage.test.tsx`: summary shown for a failed dry run, a failed update (422), and a failed install; the take-over sentence follows `wouldInstallWithTakeOver`; a valid dry run shows `No problems.`; a network error with an empty message shows the fallback reason; an old-server answer without `summary` still shows the problem messages. Depends on T018.

**Checkpoint**: All user stories work.

---

## Phase 7: Polish and cross-cutting concerns

- [X] T022 [P] Update OpenAPI text: descriptions of the new members in `src/GameBot.Service/Swagger/GamepackSchemaFilter.cs`, examples in `src/GameBot.Service/Swagger/SwaggerConfig.cs`, and the `WithDescription` text of the install routes in `src/GameBot.Service/Endpoints/GamepacksEndpoints.cs` and `GamepackCatalogEndpoints.cs`. Note in the `takeOverResolves` description that a second user-owned object with the same name stays a conflict. Use STE.
- [X] T023 [P] Add OpenAPI checks to the existing `GamepackOpenApiTests` (find it in `tests/contract/Gamepacks`): the new members and the two summary schemas exist and have descriptions.
- [X] T024 [P] Update `docs/gamepacks.md` for the new install answer fields and the Gamepacks page notice. Search "Gamepacks" and "install" in `docs/architecture.md`. Update the statements and "Last reviewed" if the change affects a match (FR-014).
- [X] T025 Run all back end tests with the filter `Gamepack` (unit, contract, integration) and `npm test` in `src/web-ui`. All existing tests must pass with no change to expected results (FR-015, SC-004).
- [ ] T026 Run the quickstart steps in `specs/161-gamepack-install-failure-explanation/quickstart.md`. Check the page at 375 px width (no horizontal scroll). Put the result in the PR text.
- [X] T027 Check that all new text (code comments, remedies, labels, docs, commit messages, PR text) is in STE (constitution Principle VI).
- [X] T028 Set `**Status**` in `specs/161-gamepack-install-failure-explanation/spec.md` to `Implemented`. Update the row for this feature in `specs/STATUS.md` in the same PR (FR-014). Depends on T025.
- [X] T029 Bump the version in `installer/versioning/version.override.json` before the push of the branch (FR-013, constitution "Version bump"). Run `git fetch origin master` and compare the file with `origin/master`. If `minor` or `major` changed, set `patch` to `"0"`. If not, set `patch` to the `patch` on `origin/master` plus 1. Do nothing if the file already has this value. Set `updatedBy` and `updatedAtUtc`. Use absolute paths. Do this after T028 and before `git push`.

---

## Dependencies and execution order

- Phase 1 first. Phase 2 blocks all stories.
- Inside Phase 2: T003 first (flag parameter). T004, T005, T006 in parallel after T003 (T004 and T006 touch other files; T005 is in the domain service). T007 after T004 and T006. T008 (explainer tests) after T004.
- US1 (T009 to T012): T009 after T005; T010 after T004, T006, T009. Tests T011 and T012 in parallel after T010.
- US2: T013 after T007 and T010 (same endpoint code). T014 after T013.
- US3 (T015, T016) after T013.
- US4 (T017 to T021) depends only on the contract, so it can start after Phase 2 and run in parallel with US1 to US3. T018 after T017. T019 in parallel. T020 after T017. T021 after T018.
- Polish after all stories. T028 after T025. T029 after T028 and before the push.

## Parallel example

After Phase 2: one developer does T009 to T016 (back end) while another does T017 to T021 (web UI).

## Implementation strategy

- MVP: Phase 1, Phase 2, and US1. A dry run then explains the failure and the remedy at the API.
- Then add US2 and US3 (mapping and tests), then US4 (page), then Polish.
- Each story is a separate, testable increment. The list of files that install does not change at any step.
- Process tasks T028 and T029 finish the PR: status, then version bump, then push.
