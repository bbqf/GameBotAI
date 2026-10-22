# Feature Specification: Gamepack Install Failure Explanation

**Feature Branch**: `161-gamepack-install-failure-explanation`  
**Created**: 2026-10-22  
**Status**: Draft  
**Input**: User description: "Issue 337 (FR-034): a failed gamepack install or dry run explains why it fails and what resolves it. Closes #337."

## Clarifications

### Session 2026-10-22

- Q: Where does the summary appear in the install answer, and in what shape? -> A: The answer gets a new `summary` object that comes before the problem list. It holds a headline text and one entry for each category with the category, the count, a label, and the remedy. Rationale: the issue asks for a summary that comes first; a structured entry lets the page and API callers show the same text.
- Q: Which category order does the summary use? -> A: Fixed order: structure, signature, reference, conflict. Rationale: a stable order makes tests and display predictable.
- Q: Is "API major version" a separate category? -> A: No. It is reported in an existing category (structure or manifest problem). The rule for take-over is by category: only a conflict with an object owned by the user is resolved by take-over. Rationale: the issue forbids new scope and a change of which problems block an install.
- Q: How does the page get the summary for a failed update? -> A: From the failed install answer, which has the same summary. If the answer has no summary (network or server error), the page shows the error message. Rationale: one source of text for API and page.
- Q: What does `wouldInstallWithTakeOver` give when the dry run itself used take-over? -> A: It states the result of the install with take-over for the same file; with take-over already on, it equals `valid`. Rationale: a stable meaning for all callers.

### Session 2026-10-22 (loop 1, from analyze findings)

- Q: Do cases with an update lock or a second same-name user object count as resolved by take-over (U2)? -> A: A conflict with owner `user` and no special code is resolved by take-over. A conflict that has a special code (for example an update in progress) is not resolved by take-over and gives its own remedy. A second user-owned object with the same name stays a conflict that take-over resolves. Rationale: take-over replaces user objects only; a lock is not a user object.
- Q: Does each summary entry tell if take-over resolves its category (I2)? -> A: Yes. Each entry has a flag `resolvedByTakeOver`. Rationale: the page and API callers can show the choice with no second request.
- Q: What does a request with take-over on give for the per-problem fact (U1)? -> A: The problem still lists real remaining problems. With take-over on, no remaining problem is resolved by take-over, so each gives `takeOverResolves` false. Rationale: take-over is already applied; a remaining problem cannot be resolved by it.
- Q: What is the rule for `wouldInstallWithTakeOver` (I1)? -> A: Valid check gives true. Take-over on and invalid check gives false. Take-over off and invalid check gives the result of a second check with take-over on. Rationale: one rule, exact.
- Q: Does the page show the summary for the Update button and the Install button (I3)? -> A: Yes, for both, and for a failed dry run.

### Session 2026-10-22 (loop 2, from analyze findings)

- Q: Edge case wording for a dry run with take-over on (I2)? -> A: The field is false when the check is invalid. Rationale: matches the exact rule.
- Q: Do the rollback refused answer and the managed update report get a summary (U1)? -> A: No. They get the per-problem fields only.
- Q: What does the page show for a valid dry run (U3)? -> A: The headline "No problems." in the dry run notice.
- Q: The second check in the service (U2): which inputs? -> A: The same inputs as the first check, except take-over is on. Rationale: the result is then exact.

## User Scenarios & Testing *(mandatory)*

### User Story 1 - A dry run explains the failure and the remedy (Priority: P1)

An operator or a buyer of a gamepack does a dry run of an install. The file has 20 problems. Each problem is a conflict with an object that the user owns. Today the answer lists 20 messages that state a fact. The answer does not say what to do. With this feature, the answer has a summary at the start. The summary says "20 conflicts: objects owned by the user" and gives the remedy: install with take-over to replace them, or rename or delete them. The answer also says if the same file would install with take-over, so the caller does not need a second request.

**Why this priority**: This is the gap that the issue reports. A buyer of the gamepack has no other tool to find the cause.

**Independent Test**: Do a dry run, without take-over, of a gamepack that has 20 same-named user-owned objects. Check that the summary comes first, shows the count and the remedy, and that the answer says the file would install with take-over.

**Acceptance Scenarios**:

1. **Given** a gamepack with 20 conflicts with objects owned by the user, **When** the caller does a dry run without take-over, **Then** the answer is not valid, the summary comes first, it shows "20 conflicts", and it gives the remedy for that category.
2. **Given** the same dry run, **When** the caller reads the answer, **Then** a field tells that the same file would install with take-over.
3. **Given** a gamepack with a conflict and a missing reference, **When** the caller does a dry run, **Then** the summary has one line for each category with the count and the remedy, and the field tells that the file would not install with take-over.
4. **Given** a gamepack with no problems, **When** the caller does a dry run, **Then** the answer is valid, the summary shows no problems, and the field tells that the file would install.

---

### User Story 2 - Each problem says if take-over resolves it (Priority: P1)

A caller reads the list of problems. A conflict problem with an object owned by the user says that take-over would resolve it. A conflict problem that take-over cannot resolve, and every problem of another category (reference, signature, API major version, structure), says that take-over cannot resolve it. Each such problem also gives the remedy for it.

**Why this priority**: The summary counts are not enough. The caller must know which problems stay after take-over.

**Independent Test**: Make a gamepack with one user-owned conflict, one missing reference, and one signature problem. Check that the first problem says take-over resolves it and the other two say it does not.

**Acceptance Scenarios**:

1. **Given** a conflict with an object that the user owns, **When** the caller reads the problem, **Then** the problem says that take-over would resolve it.
2. **Given** a reference, signature, structure, or API major version problem, **When** the caller reads the problem, **Then** the problem says that take-over cannot resolve it and gives the remedy.
3. **Given** a conflict with an object that another gamepack owns, **When** the caller reads the problem, **Then** the problem says that take-over cannot resolve it, because take-over replaces only objects of the user.

---

### User Story 3 - A failed install gives the same explanation (Priority: P1)

An operator starts a real install of a gamepack and the install fails because of problems. The answer of the failed install has the same summary, the same per-problem information, and the same field about take-over as the dry run.

**Why this priority**: The issue names the failed install, the dry run, and the update as one gap. The explanation must be the same in all of them.

**Independent Test**: Start an install, without take-over, of a gamepack with user-owned conflicts. Check that the failure answer has the same summary as the dry run.

**Acceptance Scenarios**:

1. **Given** a gamepack with conflicts, **When** the operator starts an install without take-over, **Then** the install fails with no change to the instance, and the answer has the summary first.
2. **Given** the same file, **When** the dry run and the failed install give their answers, **Then** the summary text and the problem details are the same.

---

### User Story 4 - The Gamepacks page shows the reason when an update fails (Priority: P1)

An operator presses Update for an installed gamepack on the Gamepacks page. The update fails because of problems. The page shows the same summary: the count of problems by category and the remedy for each. The page never shows a failure with no visible reason.

**Why this priority**: The operator in the issue could not see why the update of PNS from 1.0.1 to 1.1.0 failed.

**Independent Test**: Use the page to update a gamepack that has user-owned conflicts. Check that the page shows the summary with the counts and the remedies.

**Acceptance Scenarios**:

1. **Given** an update that fails because of problems, **When** the page shows the failure, **Then** it shows the summary with the count and the remedy for each category.
2. **Given** an update that fails for a cause that is not a list of problems (for example a network error or a server error), **When** the page shows the failure, **Then** it shows a message that gives the cause. The page never shows a failure with an empty reason.
3. **Given** a dry run on the page, **When** the dry run is not valid, **Then** the page shows the same summary.

---

### Edge Cases

- A gamepack has one problem only. The summary uses the singular form and still gives the remedy.
- A gamepack has problems in many categories. The summary lists each category once, in a fixed order, each with its count and remedy.
- A dry run is done with take-over on and still has problems. The summary shows the remaining problems. The take-over field is false, because take-over is already on.
- The failure has no list of problems. The caller or the page still gets a message with the cause.
- The list of problems is long. The summary stays short: one line for each category.

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: The answer of a dry run that is not valid MUST have a summary that comes before the list of problems. The summary MUST give the number of problems for each category, with a short name of the category (for example "20 conflicts: objects owned by the user"). "Short" means one line for each category. The summary MUST have no more than one entry for each category.
- **FR-002**: The summary MUST give the remedy for each category (for example "install with take-over to replace them, or rename or delete them" for conflicts with objects of the user).
- **FR-003**: The answer of a failed install MUST have the same summary, the same per-problem information, and the same take-over field as the dry run of the same file.
- **FR-004**: Each problem MUST say if take-over would resolve it. A conflict with an object that the user owns MUST say that take-over resolves it. A conflict with an object that another gamepack owns, and each problem of the category reference, signature, structure, or API major version, MUST say that take-over cannot resolve it.
- **FR-004a**: A conflict with a special code (for example an update in progress) MUST say that take-over cannot resolve it and give its own remedy. When the request uses take-over, each remaining problem MUST say that take-over cannot resolve it.
- **FR-005**: Each problem that take-over cannot resolve MUST give a remedy for it.
- **FR-006**: The dry run answer MUST have a field that tells if the same file would install with take-over. The field MUST be true only if take-over would resolve all problems. A valid dry run MUST give the value true.
- **FR-007**: The Gamepacks page MUST show the same summary when an update, an install, or a dry run fails because of problems.
- **FR-008**: The Gamepacks page MUST show a message with the cause for each failure, also when the failure has no list of problems. A failure with no visible reason MUST NOT happen.
- **FR-009**: The feature MUST NOT change which problems block an install. Only the explanation changes.
- **FR-010**: The feature MUST NOT change how take-over works, and MUST NOT add an install mode.
- **FR-011**: The new answer fields MUST be additions. A caller that does not read them MUST see the same result as before. All text MUST be in Simplified Technical English.
- **FR-012**: The Gamepacks page change MUST state its phone support level (full) and follow docs/mobile-ui-principles.md.
- **FR-013** (process): Before the push of the branch, the PR MUST bump the version in `installer/versioning/version.override.json`, as the constitution ("Version bump") says.
- **FR-014** (process): When the feature is done, the spec Status MUST be set to Implemented, and `specs/STATUS.md` MUST be updated in the same PR. The author MUST check `docs/architecture.md` and update it if the feature changes a statement in it.
- **FR-015**: Existing tests of take-over behaviour MUST stay green with no change to their expected results. This is the acceptance for FR-010.

### Key Entities *(include if feature involves data)*

- **Problem**: One finding of the install check. It has a category, an object type, a name, a message, and an owner for a conflict. It gets two new facts: if take-over resolves it, and the remedy.
- **Failure summary**: A short list that comes first in the answer. It has one line for each problem category with the count, the label, the remedy, and a flag that tells if take-over resolves the category.
- **Take-over outcome**: A yes or no fact in the dry run answer. It tells if the same file would install with take-over.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: A caller finds the cause and the remedy of a failed dry run in one request. The caller needs no second request to learn if take-over resolves the problems.
- **SC-002**: For a file with 20 user-owned conflicts, the answer shows one summary line with "20" and the remedy, before the 20 problems.
- **SC-003**: In 100% of tested update failures on the Gamepacks page, the page shows a non-empty reason.
- **SC-004**: The set of files that install, and the set that fail, is the same before and after the feature, for all existing test cases. The existing install tests are the evidence for this criterion: they MUST stay green with no change to their expected results.

## Assumptions

- The install check today has the categories structure, reference, conflict, and signature. An API major version problem is one of these categories and is handled in the same way.
- Take-over replaces only objects that the user owns. A conflict with an object of another gamepack is not resolved by take-over.
- The summary text comes from the server, so the API callers and the page show the same text.
- The file in the PNS repository is not changed by this feature.
- The rollback refused answer and the managed update report get the per-problem fields only. They get no summary.
- A valid dry run on the page shows the summary headline "No problems." in the dry run notice.
- The request take-over flag is passed to the summary builder and to the install response mapping in one change. This change is done before the work on the per-problem mapping, so no task depends on a later task.
- The work touches these files in addition to the core files, and the plan lists them: the catalog install endpoint file (`src/GameBot.Service/Endpoints/GamepackCatalogEndpoints.cs`), the Gamepacks page style file (`src/web-ui/src/pages/GamepacksPage.css`), the integration test folder (`tests/integration`), and the gamepacks document (`docs/gamepacks.md`).
