# Tasks: Security Alerts Fix

**Input**: Design documents from `/specs/191-security-alerts-fix/`
**Prerequisites**: plan.md, spec.md, research.md, data-model.md, quickstart.md

**Tests**: The spec asks for no new unit tests. The checks are `npm audit`, lint, Jest, the build, the NuGet audit, and CI (plan, Constitution Check II).

**Format**: `[ID] [P?] [Story] Description`

- **[P]**: Can run in parallel (different files, no dependencies)
- **[Story]**: The user story of the task (US1, US2)

## Phase 1: Setup (baseline)

**Purpose**: Record the state before the change. Later tasks compare with it.

- [ ] T001 Run `node --version` and make sure that it is 24.x. Run `npm --prefix C:\src\GameBot\src\web-ui ci`, then `npm --prefix C:\src\GameBot\src\web-ui audit`, `npm --prefix C:\src\GameBot\src\web-ui ls minimatch --all`, and `npm --prefix C:\src\GameBot\src\web-ui ls --omit=dev --all`. Record the Node.js version, the audit result (6 moderate), the `minimatch` tree (9.0.9 below `@typescript-eslint/typescript-estree`), and the runtime tree in the "Before" section of `specs/191-security-alerts-fix/alert-audit.md`
- [ ] T002 Run `npm --prefix C:\src\GameBot\src\web-ui run lint` with `Measure-Command`. Record the count of errors and warnings and the duration in the "Before" section of `specs/191-security-alerts-fix/alert-audit.md` (research R5)

---

## Phase 2: Foundational

No foundational task. The user stories use the baseline from Phase 1 only.

---

## Phase 3: User Story 1 - The repository has no open security alerts (Priority: P1) 🎯 MVP

**Goal**: The lock file has no `minimatch` version in `>= 7.0.0, < 11.0.1`, and `npm audit` reports 0 vulnerabilities.

**Independent Test**: `npm audit` in `src/web-ui` reports `found 0 vulnerabilities`; `npm ls minimatch --all` shows 11.x below `typescript-estree` and 3.1.5 elsewhere (quickstart steps 1 and 2).

- [ ] T003 [US1] Add the entry `"minimatch@9": "^11.0.1"` to the `overrides` object in `src/web-ui/package.json`, after the `brace-expansion@2` entry. Do not change `dependencies` or `devDependencies` (FR-002, FR-003, FR-006)
- [ ] T004 [US1] Run `npm --prefix C:\src\GameBot\src\web-ui install` to update `src/web-ui/package-lock.json`. Then run `npm --prefix C:\src\GameBot\src\web-ui ci` to make sure that a clean install from the lock file works
- [ ] T005 [US1] Run `npm --prefix C:\src\GameBot\src\web-ui audit` and `npm --prefix C:\src\GameBot\src\web-ui ls minimatch --all`. Make sure that the audit reports 0 vulnerabilities and that no copy has a version from 7.0.0 to 11.0.0. Also make sure that `git diff -- src/web-ui/package-lock.json` changes only the `minimatch` entry below `@typescript-eslint/typescript-estree` and the `brace-expansion` (and its `balanced-match`) entries that this `minimatch` uses. A `brace-expansion` copy that only `minimatch` 9.0.9 used can go away. Then run `npm --prefix C:\src\GameBot\src\web-ui ls --omit=dev --all` and make sure that the output is the same as in T001 (FR-006). Record the result in the "After" section of `specs/191-security-alerts-fix/alert-audit.md` (FR-001, FR-004, SC-002)
- [ ] T006 [P] [US1] Run `dotnet list C:\src\GameBot\GameBot.sln package --vulnerable --include-transitive` and record the result in `specs/191-security-alerts-fix/alert-audit.md` (SC-003)
- [ ] T007 [P] [US1] Run `gh api` for the open Dependabot, code scanning, and secret scanning alerts (quickstart step 5). Record each count in `specs/191-security-alerts-fix/alert-audit.md`. If a new alert is open, fix it in the same way (an override in the manifest of its project) and record it (FR-007, edge case "new alert")

**Checkpoint**: The audit is clean. US1 is complete before the merge. SC-001 can only be checked after the merge: Dependabot closes alert #79 on its next scan of `master` (quickstart step 5). The PR description MUST say this.

---

## Phase 4: User Story 2 - The developer tools continue to work (Priority: P1)

**Goal**: Lint, Jest, and the build pass with the same result as before the change.

**Independent Test**: quickstart step 3; lint counts match the T002 baseline.

- [ ] T008 [US2] Run `npm --prefix C:\src\GameBot\src\web-ui run lint` with `Measure-Command`. Make sure that the count of errors and warnings is the same as in T002, and that the duration is in ±10% of T002 (if it is not, run both again once, because the first run can be slow). Record it in the "After" section of `specs/191-security-alerts-fix/alert-audit.md` (FR-005, SC-005)
- [ ] T009 [P] [US2] Run `npm --prefix C:\src\GameBot\src\web-ui test` and make sure that all tests pass (FR-005)
- [ ] T010 [P] [US2] Run `npm --prefix C:\src\GameBot\src\web-ui run build` and make sure that the build passes (FR-005)

**Checkpoint**: The developer tools work. CI (SC-004) is checked in the pipeline after the push.

---

## Phase 5: Polish & Cross-Cutting Concerns

- [ ] T011 [P] Add a `### Security` entry at the top of `## [Unreleased]` in `CHANGELOG.md` (191-security-alerts-fix). Write it in STE: the change (override of `minimatch` 9.x to 11.x, GHSA-g3hv-75wg-m249, Dependabot alert #79), "Not changed" (runtime dependencies, the shipped web UI, the service), and "Perf note: no performance effect" (FR-008)
- [ ] T012 [P] Add the row `| 191 | Security Alerts Fix | Implemented |` to `specs/STATUS.md`, and set `**Status**: Implemented` in `specs/191-security-alerts-fix/spec.md`
- [ ] T013 Bump the version in `installer/versioning/version.override.json` (CLAUDE.md rule) and add a `**Version**` line to the CHANGELOG entry from T011 (FR-008). The pipeline does this step before the implementation commit

---

## Dependencies & Execution Order

- **Phase 1** (T001, T002): no dependencies. Run first.
- **US1** (T003 → T004 → T005): in sequence. T006 and T007 do not depend on T003 to T005 and can run in parallel with them.
- **US2** (T008, T009, T010): after T004. T009 and T010 can run in parallel. T008 needs T002.
- **Polish** (T011, T012): after US1 and US2. T013 after T011.

### Parallel example

```text
After T004:
  T005 (audit), T006 (NuGet), T007 (GitHub lists), T009 (Jest), T010 (build)
```

## Implementation Strategy

1. Phase 1: record the baseline.
2. US1 (MVP): add the override, update the lock file, prove a clean audit.
3. US2: prove that lint, tests, and the build do not change.
4. Polish: CHANGELOG, status, version bump.
