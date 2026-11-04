# Feature Specification: Install the Same Gamepack Version Again (Force Install)

**Feature Branch**: `178-force-reinstall-gamepack`  
**Created**: 2026-11-04  
**Status**: Draft  
**Input**: User description: "GitHub issue #374, FR-041: Install the same gamepack version again (rewrite all objects from the file). Closes #374. URL: https://github.com/bbqf/GameBotAI/issues/374"

## Clarifications

### Session 2026-11-04

- Q: Which objects does the response list as overwritten? → A: The objects of the pack whose stored content differs from the file (for example a hand edit). An object that is equal to the file is not listed. Rationale: the list must show real data loss and nothing else.
- Q: What does the catalog update route do with the force option when the catalog latest version equals the installed version? → A: The route installs that version again from the catalog. It rewrites the objects. Without the force option, the route does nothing for an equal version, as it does today. Rationale: this is the only useful meaning of the option on that route.
- Q: Which version does the pack have after a rollback of a forced install of the same version? → A: The same version X. The rollback restores the object content from before the forced install. Rationale: the version number does not change in a forced install.
- Q: How does a caller pass the force option? → A: In the same way as the dry-run option on each route. The plan sets the exact form. Rationale: one consistent form for the caller.
- Q (loop 2): How many routes get the force option, and is the managed update a route? → A: Exactly four routes (install, update, catalog install, catalog update). The managed update is a mode (`managed=true`) of the update routes and also accepts the option. Rationale: analyze findings I1 and I2.
- Q (loop 2): Does the STE rule for plan.md and tasks.md belong in the spec? → A: No. The constitution (Principle VI) already sets it. Rationale: analyze finding C1.
- Q (loop 2): Which term do user story 3 and FR-007 use? → A: "reinstall point" for a forced install. Rationale: analyze finding T1.
- Q (loop 2): What does "no changelog file" mean? → A: The pack file needs no new changelog file. Rationale: analyze finding U4.
- Q (loop 1, replaced by loop 2): Which routes get the force option? → A: Four routes: direct install, direct update, catalog install, managed update (the catalog update). Rationale: analyze finding I1.
- Q (loop 1): Which term names the saved state of a forced install? → A: "reinstall point". It is a rollback point that is separate from the kept previous version. Rationale: analyze finding I2.
- Q (loop 1): Is the option name final? → A: Yes, `force`. Rationale: analyze finding A1.
- Q (loop 1): Does a list member show the reinstall point? → A: Yes, as an optional member (new FR-012). Rationale: analyze finding U1.
- Q: Does a forced install replace the "previous version" kept for rollback? → A: No. The reinstall point holds the object content from before the install. The kept previous version does not change. Rationale: a forced install is not an update, so the version history does not change.

### User Story 1 - Repair an installed pack from its file (Priority: P1)

A gamepack author finds that the objects of an installed pack are wrong. For example, a platform fault changed how the install stored the objects. The author wants to install the same name and version again. The platform then rewrites every object of the pack from the file. The author does not need to publish a new version number.

**Why this priority**: This is the core of the request. Today the only way to repair an installed pack is to publish a new version number with the same content. A platform fix that changes how an install stores objects reaches the installed objects only after a new install.

**Independent Test**: Install a pack. Install the same name and version again with the force option. Check that every object of the pack has the content of the pack file.

**Acceptance Scenarios**:

1. **Given** a pack at version X is installed, **When** the caller installs the same pack at version X without the force option, **Then** the install behaves as it does today (it does not rewrite the objects).
2. **Given** a pack at version X is installed, **When** the caller installs the same pack at version X with the force option, **Then** the install succeeds and every object of the pack gets the content of the file.
3. **Given** a pack at version X is installed and the caller changed an object by hand, **When** the caller installs the same pack at version X with the force option, **Then** the object returns to the content of the file.
4. **Given** a forced install of the same version fails, **When** the caller reads the objects, **Then** no object changed (the install is atomic). The test MUST cause the failure with an injected store write fault. The fault comes from a failing store write through the existing write gate (`IGamepackWriteGate`).
5. **Given** a pack at version X is installed and the caller deleted one object by hand, **When** the caller installs the same pack at version X with the force option, **Then** the install creates the object again from the file. An integration test deletes an object by hand and checks this result (FR-002).

---

### User Story 2 - See what the forced install overwrites (Priority: P2)

The caller wants to know which objects the forced install replaces. The response lists the overwritten objects (type and name), in the same way as it lists take-overs today. The caller can first run a dry run to see the list without any change.

**Why this priority**: A forced install overwrites changes that a user made by hand. The caller must see this before and after the install.

**Independent Test**: Change one object of an installed pack by hand. Run a forced dry run. Check that the response lists the changed object and that no object changed.

**Acceptance Scenarios**:

1. **Given** a user changed an object of an installed pack, **When** the caller runs a forced install, **Then** the response lists that object (type, name) as overwritten.
2. **Given** a pack at version X is installed, **When** the caller runs a dry run together with the force option, **Then** the response reports the result of the install and no object changes.

---

### User Story 3 - Undo a forced install (Priority: P2)

The caller made a forced install and wants to go back. The platform creates a reinstall point before it rewrites the objects. The existing rollback operation restores the state before the forced install.

**Why this priority**: A forced install overwrites data. A safe way back is necessary.

**Independent Test**: Change an object by hand. Run a forced install. Run a rollback for the pack. Check that the object has the hand-made change again.

**Acceptance Scenarios**:

1. **Given** a forced install finished, **When** the caller runs the rollback operation for the pack, **Then** the objects return to the state before the forced install.

---

### User Story 4 - Force option on catalog routes (Priority: P3)

The caller installs or updates a pack from the catalog. The same force option works on the catalog install route and on the catalog update route.

**Why this priority**: Many callers install from the catalog. The option must work on all install routes.

**Independent Test**: Install a pack from the catalog. Install the same version again from the catalog with the force option. Repeat with the update route.

**Acceptance Scenarios**:

1. **Given** a catalog pack at version X is installed, **When** the caller runs the catalog install with the force option for version X, **Then** the objects are rewritten from the file.
2. **Given** a catalog pack at version X is installed, **When** the caller runs the catalog update with the force option and the latest version is X, **Then** the objects are rewritten from the file.

---

### Edge Cases

- The same version is installed and the force option is not set: the install behaves as it does today.
- The force option is set and the pack is not installed yet: the install behaves as a normal first install.
- The force option is set and the version is different from the installed version: the rules for update and downgrade do not change. The force option does not change them.
- The forced install fails in the middle: no object changes and no rollback point stays that hides the earlier state.
- The force option is set together with the dry-run option: the response reports the result, lists the objects that the install would overwrite, and nothing changes. No rollback point is created.
- An object of the pack was deleted by hand after the first install: the forced install creates it again from the file.

## Requirements *(mandatory)*


### Functional Requirements

- **FR-001**: The system MUST let a caller install again a gamepack name and version that is already installed, when the caller sets the force option.
- **FR-002**: When the force option is set, the system MUST rewrite every object of the pack from the pack file, so that each object has the content of the file as in a first install.
- **FR-003**: When the force option is not set, an install of the same name and version MUST behave as it does today.
- **FR-004**: The force option MUST be available on exactly four routes that install a pack: `POST /api/gamepacks/install`, `POST /api/gamepacks/update`, `POST /api/gamepacks/catalog/install`, and `POST /api/gamepacks/catalog/update`. The managed update (`managed=true`) is a mode of the update routes. It is not a fifth route. It also accepts the force option.
- **FR-005**: With the force option and an equal version, an update route installs that version again. Without the force option, an update route does nothing for an equal version, as today.
- **FR-006**: A forced install MUST be atomic. If it fails, no object changes.
- **FR-007**: Before the system rewrites the objects, it MUST create a reinstall point. The existing rollback operation for the pack MUST restore the object content from before the forced install. The version stays X and the kept previous version does not change.
- **FR-008**: The response of a forced install MUST list the objects that the install overwrote because their stored content differs from the file (type and name). An object that equals the file is not listed, in the same way as the response lists take-overs today.
- **FR-009**: The dry-run option MUST work together with the force option. The dry run MUST report the result and the list of overwritten objects, and MUST NOT change any object or create a rollback point.
- **FR-010**: The force option MUST NOT change the rules for an install of a different version (update or downgrade).
- **FR-011**: The force option MUST NOT need a new version number or a changelog file. The pack file does not need a new changelog file. A test MUST use the same version in the same pack file.
- **FR-012**: The list of installed packs MAY show whether a reinstall point exists for each pack.

### Key Entities

- **Gamepack**: A named and versioned bundle of objects (for example sequences, commands, and images) that is installed from a file.
- **Installed pack record**: The record of the name and version that is installed now, and of the objects that belong to it.
- **Take-over**: An object that the install replaced because the object existed before and differed from the file. The response lists these.
- **Rollback point**: A saved state before an install. The rollback operation restores it. For a forced install of the same version, the rollback point is called the **reinstall point**. It is separate from the kept previous version.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: A caller repairs an installed pack with one forced install call, and does not publish a new version number (0 extra versions needed).
- **SC-002**: After a forced install, 100% of the objects of the pack have the content of the pack file.
- **SC-003**: In 100% of the failed forced installs, no object changes.
- **SC-004**: After a forced install, the rollback operation restores 100% of the objects to their state before the install.
- **SC-005**: A dry run with the force option changes 0 objects and creates 0 rollback points.
- **SC-006**: An install of the same version without the force option gives the same result as before this feature.
- **SC-007**: A forced install of a pack with 200 objects takes no more than 1.5 times as long as a normal install of the same pack. A timed test measures this. The test does one warm-up run. Then it does at least 5 runs each of normal and forced install, and it compares the medians. The ratio check limit is 1.5. The test saves the ratio and both medians in the quickstart file of this feature. The test is a local measure. It runs in CI only if it is stable. The test MUST have the same category trait as the other performance tests in the repo (`Category` = `Performance`).

## Assumptions


- The option name is `force` (value `true`). The issue says the name is open. Planning fixed the name as `force`.
- A list of installed packs MAY show whether a reinstall point exists (`hasReinstallPoint`), so that a caller knows that a rollback of a forced install is possible.
- A forced install of the same version uses the same take-over and rollback system as a normal install. It does not need a second system.
- The feature has no web UI change. It changes the API only.
- After the implementation, the Status line of this spec is set to "Implemented".
- The fault in the `WaitForImage` step (issue 373) is out of scope. This feature only gives the way to repair objects by an install again.
