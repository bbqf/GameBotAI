# Tasks: Browse and install gamepacks from a remote catalog

**Input**: Design documents in `C:\src\GameBot\specs\147-gamepack-remote-catalog\`
**Prerequisites**: plan.md, spec.md, research.md, data-model.md, contracts/, quickstart.md
**Tests**: Required. The plan and quickstart.md give a test for each requirement.

Format: `- [ ] T### [P?] [Story?] Description with file path`. `[P]` = can run in parallel (other files, no open dependency).

Paths: Domain code in `src/GameBot.Domain/Gamepacks/Catalog/`. Service code in `src/GameBot.Service/`. Tests in `tests/unit/Gamepacks/`, `tests/contract/Gamepacks/`, `tests/integration/Gamepacks/`. Web code in `src/web-ui/src/`.

## Phase 1: Setup

- [x] T001 Create the folder `src/GameBot.Domain/Gamepacks/Catalog/` and add `CatalogFailure.cs` with the reason codes from data-model.md (`not_configured`, `invalid_location`, `unreachable`, `unauthorized`, `forbidden`, `not_found`, `index_invalid`, `index_too_large`, `file_missing`, `file_too_large`, `hash_mismatch`, `redirect_refused`, `timeout`, `entry_not_listed`) and a `CatalogException` that holds the code. The reason `not_found` is only for a listed file that the catalog answers with "not found". An unlisted entry always uses `entry_not_listed`.
- [x] T002 [P] Add `GamepackCatalog` route constants (`/api/gamepacks/catalog`, `/settings`, `/install`) to `src/GameBot.Service/ApiRoutes.cs`
- [x] T003 [P] Add `CatalogOptions` (index limit 5 MB, file limit 100 MB, signature limit 1 MB, request timeout 30 s, max redirects 3) in `src/GameBot.Domain/Gamepacks/Catalog/CatalogOptions.cs`, so tests can set small values

## Phase 2: Foundational (blocks all stories)

- [x] T004 [P] Add `GamepackCatalogSettings.cs` (location, allowInsecure, token) in `src/GameBot.Domain/Gamepacks/Catalog/`
- [x] T005 [P] Add `CatalogEntry.cs` (name, version, changelog, file, signature, sha256) in `src/GameBot.Domain/Gamepacks/Catalog/`
- [x] T006 Add `CatalogLocation.cs` in `src/GameBot.Domain/Gamepacks/Catalog/`: scheme rules (https always; http for loopback or allowInsecure), base with trailing "/", safe relative path check (no "..", encoded dots, backslash, absolute path, other host, scheme, or port), folder path rules (rooted, under folder)
- [x] T007 [P] Write unit tests for `CatalogLocation` in `tests/unit/Gamepacks/CatalogLocationTests.cs` (path escape cases, scheme rules, loopback hosts)
- [x] T008 Add `GamepackCatalogIndexParser.cs` in `src/GameBot.Domain/Gamepacks/Catalog/`: parse `index.json` (formatVersion 1), validate name with `GamepackNameRules`, skip an invalid version, an invalid sha256, or an escaping file or signature location, first duplicate wins, empty changelog when missing; throw `index_invalid` for bad JSON
- [x] T009 [P] Write unit tests for the parser in `tests/unit/Gamepacks/GamepackCatalogIndexParserTests.cs` (FR-004, bad version, duplicate, missing changelog, path escape, optional signature location)
- [x] T010 Add `IGamepackCatalogSettingsStore.cs` and `GamepackCatalogSettingsStore.cs` in `src/GameBot.Domain/Gamepacks/Catalog/`: file `gamepack-catalog.json` in the data root (same pattern as the signature settings store), read on each call, atomic write, token rule (null keeps, empty clears, text sets), validation on write
- [x] T011 [P] Write unit tests for the settings store in `tests/unit/Gamepacks/GamepackCatalogSettingsStoreTests.cs` (round trip, token rules, change without restart)
- [x] T012 Add `IGamepackCatalogSource.cs` (`ReadIndex`, `ReadFile(location, limit)`) in `src/GameBot.Domain/Gamepacks/Catalog/`. The same `ReadFile` is used for the gamepack file and the signature file, with a different limit.
- [x] T013 Add `FolderCatalogSource.cs` in `src/GameBot.Domain/Gamepacks/Catalog/`: folder-bound reads, read-only stream, length check, map errors to failure codes. A missing `index.json` MUST give `index_invalid` (not `file_missing`). The token and `allowInsecure` are ignored.
- [x] T014 Add `HttpCatalogSource.cs` in `src/GameBot.Domain/Gamepacks/Catalog/`: `IHttpClientFactory` client with `AllowAutoRedirect = false`, bearer header only for the catalog host, manual redirects (max 3, same host and scheme), counted stream limit, early `Content-Length` check, timeout for each request (index, gamepack file, and signature file), map 401/403/404/network errors to failure codes. A "not found" answer for `index.json` MUST give `index_invalid`.
- [x] T015 Add `GamepackCatalogSourceFactory.cs` in `src/GameBot.Domain/Gamepacks/Catalog/` to pick the source from the current settings on each request
- [x] T016 [P] Write unit tests for `FolderCatalogSource` in `tests/unit/Gamepacks/FolderCatalogSourceTests.cs` (FR-006, file outside folder, junction case where the OS allows it, missing `index.json` gives `index_invalid`, token and `allowInsecure` ignored)

**Checkpoint**: Settings, parser, and sources work. User stories can start.

## Phase 3: User Story 1 - Browse the catalog (P1)

**Goal**: The operator sets the catalog and sees versions, changelog text, and the installed marker.
**Independent Test**: Set a catalog with two gamepacks, install one version, request the list. All versions show with changelog, and the installed one is marked.

- [x] T017 [US1] Add `GamepackCatalogService.cs` in `src/GameBot.Domain/Gamepacks/Catalog/` with `List`: read the index, group by name, sort versions new to old, set `installed` and `updateAvailable` (same version order as feature 144), set `installedVersion` from the registry; write one `LogInformation` with outcome and no token
- [x] T018 [P] [US1] Add `src/GameBot.Service/Endpoints/Dto/GamepackCatalogDtos.cs` (settings, settings write, list, item, version DTOs)
- [x] T019 [US1] Add `src/GameBot.Service/Endpoints/GamepackCatalogEndpoints.cs` with named handler methods: `GET` and `PUT` settings (return `tokenSet`, never the token; 400 `invalid_location`) and `GET` list (error map from contracts/gamepack-catalog-api.md; `index_invalid` gives HTTP 502)
- [x] T020 [US1] Register the catalog services, options, and endpoints in `src/GameBot.Service/Endpoints/GamepackServiceCollectionExtensions.cs` (keep `Program.cs` thin; no hosted service)
- [x] T021 [P] [US1] Add schema descriptions for the new types in `src/GameBot.Service/Swagger/GamepackSchemaFilter.cs`
- [x] T022 [P] [US1] Contract tests in `tests/contract/Gamepacks/GamepackCatalogSettingsContractTests.cs`: round trip, `tokenSet` only, token canary scan of responses (FR-001, FR-002, FR-002a, FR-003, SC-004), API token rule on each route (FR-018); use a temp data root
- [x] T023 [P] [US1] Contract tests in `tests/contract/Gamepacks/GamepackCatalogListContractTests.cs`: list with installed marker (FR-007), 409 `not_configured`, error codes for bad index and unreachable
- [x] T024 [P] [US1] Integration tests in `tests/integration/Gamepacks/GamepackCatalogHttpIntegrationTests.cs` with a local test HTTP server: bearer header seen (FR-005), redirect to another host fails and the token is not sent (FR-011), oversize index (FR-012), token refused (FR-013)
- [x] T025 [P] [US1] Contract test in `tests/contract/Gamepacks/GamepackCatalogLogContractTests.cs`: one log message for each list, no token canary (FR-019, SC-004); open the logging gate component first
- [x] T026 [P] [US1] Timing test for 50 gamepacks with 10 versions each, under 5 seconds, in `tests/integration/Gamepacks/GamepackCatalogPerformanceTests.cs` (SC-002)
- [x] T027 [P] [US1] Missing `index.json` test in `tests/contract/Gamepacks/GamepackCatalogMissingIndexContractTests.cs`: for a folder catalog with no `index.json`, and for a web catalog that answers "not found" for `index.json`, the list returns HTTP 502 with reason `index_invalid` and a clear message, and the instance state does not change (FR-013, SC-003)

**Checkpoint**: The catalog list works through the API.

## Phase 4: User Story 2 - Install a version from the catalog (P1)

**Goal**: The operator installs or dry-runs one listed version through the existing install flow.
**Independent Test**: Install a listed version and see it in the installed list. Run a dry run and see no change.

- [x] T028 [US2] Add `Install(name, version, dryRun, ct)` to `src/GameBot.Domain/Gamepacks/Catalog/GamepackCatalogService.cs`: fail with `entry_not_listed` before any download, download the gamepack file (100 MB limit) and the optional signature file (1 MB limit, same host, token rule, and request timeout), check `sha256` with a fixed-time compare, then call `GamepackService.InstallAsync(json, dryRun, signatureText, ct)` (signature text is null when the entry has no signature file); log name, version, and outcome without the token
- [x] T029 [US2] Add the `POST` install handler to `src/GameBot.Service/Endpoints/GamepackCatalogEndpoints.cs`: map the outcome to the same DTO as the manual install route; error map from the contract (404, 409, 502, 504, 422)
- [x] T030 [P] [US2] Contract tests in `tests/contract/Gamepacks/GamepackCatalogInstallContractTests.cs`: 404 `entry_not_listed` for an unlisted name and for an unlisted version, with no file request seen by the server and never the reason `not_found` (FR-010); `not_found` only for a listed file that the server answers with 404; success body shape; 409 `not_configured`
- [x] T031 [P] [US2] Integration tests in `tests/integration/Gamepacks/GamepackCatalogInstallIntegrationTests.cs`: install from a folder and from a web source (FR-008), dry run changes nothing (FR-009), signature policy `required` rejects a bad signature (SC-006)
- [x] T032 [P] [US2] Integration tests in `tests/integration/Gamepacks/GamepackCatalogFailureStateTests.cs`: for each failure kind (not set, unreachable, token refused, bad index, missing index, missing file, oversize file, oversize signature, timeout, hash mismatch) the registry, sequences, commands, images, and settings are equal before and after (FR-013, FR-014, SC-003)
- [x] T033 [P] [US2] Correct-hash test in `tests/integration/Gamepacks/GamepackCatalogHashTests.cs`: an index entry with the correct SHA-256 of the file (lowercase and uppercase hex) lets the install pass and the gamepack shows in the installed list; a wrong hash rejects the install with `hash_mismatch` and the instance does not change (FR-014)
- [x] T034 [P] [US2] Parallel-install test in `tests/integration/Gamepacks/GamepackCatalogParallelInstallTests.cs`: start two catalog installs of one name at the same time (`Task.WhenAll`); the results follow the feature 143 install flow rules (one rule set, no extra catalog rule), the registry holds one consistent state, and no exception escapes (edge case, FR-008)
- [x] T035 [P] [US2] Limit and timeout tests in `tests/integration/Gamepacks/GamepackCatalogLimitTests.cs` with a local test HTTP server and small `CatalogOptions` values (FR-012): an oversize signature file (over the 1 MB limit) gives `file_too_large`; an oversize gamepack file gives `file_too_large` and the download stops (counted read and early `Content-Length` check); a per-request timeout on a file download gives `timeout` (HTTP 504). The instance does not change in each case.
- [x] T036 [P] [US2] Signature source tests in `tests/integration/Gamepacks/GamepackCatalogSignatureFileTests.cs` (FR-008, F1): an entry with a signature file downloads it with the bearer token (catalog host only) and passes the text to the signature check; an entry with no signature file makes the check see "missing" (policy `required` rejects, policy `optional` or `off` follows feature 146); a signature download that fails (404, 401, redirect to another host) fails the install in the same way as a gamepack file download; the signature file is also read from a folder catalog

**Checkpoint**: Install works through the API under the signature policy.

## Phase 5: User Story 4 - No install without a user action (P1)

**Goal**: The platform never installs from the catalog by itself.
**Independent Test**: List a newer version many times, wait, and see the installed version stay the same.

- [x] T037 [US4] Integration test in `tests/integration/Gamepacks/GamepackCatalogNoAutoInstallTests.cs`: many list calls and a wait leave the installed version unchanged (FR-015, SC-005)
- [x] T038 [P] [US4] Dependency-injection registration check in `tests/unit/Gamepacks/GamepackCatalogNoPollingTests.cs`: build the real registration from `GamepackServiceCollectionExtensions.cs` (and the host registrations for `IHostedService`). For each `IHostedService`, `BackgroundService`, and timer-type registration, walk the constructor graph TRANSITIVELY, at all depth: for each constructor parameter type, resolve its registered implementation type (service descriptors, factory registrations by their declared types), then walk that implementation's constructor parameters, and so on. Keep a visited set to stop on cycles. The test fails if any path reaches `GamepackCatalogService`, `IGamepackCatalogSource`, `GamepackCatalogSourceFactory`, or the catalog settings store, including an indirect path. Add a negative control: a test-only registered hosted service that depends on the catalog service through an intermediate class MUST make the check fail. Do NOT scan source text (FR-015).

## Phase 6: User Story 3 - Review and update in the web interface (P2)

**Goal**: A "Gamepacks" page shows the list and installs on the Update button.
**Independent Test**: Open the page with a newer version, press Update, see the result and the new marker.

- [x] T039 [P] [US3] Add the API client `src/web-ui/src/services/gamepackCatalog.ts` (settings get and put, list, install with a `dryRun` flag)
- [x] T040 [US3] Add `src/web-ui/src/pages/GamepacksPage.tsx` and `src/web-ui/src/pages/GamepacksPage.css`: settings form (location, allow insecure, token field never filled from the server, "token set" hint, a short note that the token and allow insecure apply to web addresses only), list with changelog and installed marker, Update button only when `updateAvailable` (label "Install" when none is installed), a "dry run" check box (when on, the install button runs a dry run and the result text says it was a dry run), a "Refresh" button that reloads the list, list reload after install, plain-word reason text, load on open and on Refresh only (no timer, FR-015, FR-016)
- [x] T041 [US3] Add the area `gamepacks` (path `/gamepacks`, phone `full`): add the id and entry in `src/web-ui/src/types/navigation.ts`, and the area parse, `renderGamepacks`, and switch case in `src/web-ui/src/App.tsx` (pattern of `UpdatePage`); update the navigation tests in `src/web-ui/src/types/__tests__/navigation.test.ts`, `src/web-ui/src/components/__tests__/Navigation.test.tsx`, `src/web-ui/src/__tests__/navigation.spec.tsx`, and `src/web-ui/src/components/phone/__tests__/PhoneTabBar.test.tsx`
- [x] T042 [P] [US3] Jest tests in `src/web-ui/src/pages/__tests__/GamepacksPage.test.tsx`: list and changelog, Update only for newer versions, "Install" label, install result, error reason, not-configured state
- [x] T043 [P] [US3] Jest tests for the dry-run check box and the Refresh button in `src/web-ui/src/pages/__tests__/GamepacksPage.test.tsx` (FR-016): with the check box on, the install button calls the client with `dryRun` true and shows the dry-run result; with it off, `dryRun` is false; the Refresh button calls the list once more; opening the page and waiting does not call install or call list again on a timer
- [x] T044 [US3] Make the page follow `docs/mobile-ui-principles.md` (phone layout, touch targets, check box and Refresh button reachable by touch) and state phone support "full" in the page doc (FR-017)

## Phase 7: Polish and cross-cutting

- [x] T045 [P] Add a catalog section (index format, settings, routes, signature file location, limits) to `docs/gamepacks.md`
- [x] T046 Add a note to the catalog section of `docs/gamepacks.md`: the token and the "allow insecure" flag apply to web addresses only; a folder catalog ignores both (spec Assumptions, FR-005, FR-006). Add the same one-line note to the settings description in `src/GameBot.Service/Swagger/GamepackSchemaFilter.cs`.
- [x] T047 [P] Update `docs/architecture.md` (API surface and "Last reviewed")
- [x] T048 [P] Update `specs/STATUS.md` and `CHANGELOG.md`, and set the Status in `specs/147-gamepack-remote-catalog/spec.md`
- [x] T049 Run the full build and all test suites (`dotnet build`, `dotnet test`, and in `src/web-ui`: `vite build` and `jest`). Fix all failures.
- [ ] T050 SC-001 timing walk: run the quickstart.md manual walk with a timer. Record the elapsed time from the settings step to the end of the first install, and compare it with the 3-minute target. Write the measured time and the result in `specs/147-gamepack-remote-catalog/quickstart.md` under a "Walk result" heading.
- [x] T051 Bump `patch` in `installer/versioning/version.override.json` per the constitution rule (run `git fetch origin master`, compare with `origin/master`; if `minor` or `major` changed set `patch` to "0", else set `patch` to the `origin/master` patch plus 1; set `updatedBy` and `updatedAtUtc`). This is the last task.

## Dependencies

- Order: Phase 1, Phase 2, Phase 3 (US1), Phase 4 (US2), Phase 5 (US4), Phase 6 (US3), Phase 7.
- US2 needs the US1 service and endpoints. US4 needs US1 (and US2 for the full check). US3 needs the US1 and US2 routes.
- T006 before T008, T013, T014. T010 before T015. T017 before T019. T028 before T029.
- T040 before T042 and T043 (same test file for T042 and T043: run in order). T046 after T045 (same file).
- T049 after all other tasks except T050 and T051. T050 after T049. T051 last.

## Parallel examples

- Phase 2: T004, T005, T007, T009, T011, and T016 run together once their code files exist.
- US1 tests T022 to T027 run together after T020.
- US2 tests T030 to T036 run together after T029.
- Docs T045, T047, and T048 run together.

## Implementation strategy

- MVP: Phases 1 to 4 (API browse and install). Then add the no-auto-install checks (US4), the web page (US3), and polish.
- Commit after each phase. The full build and tests must pass before the push.
