# Feature Specification: Seed queue templates in a gamepack

**Feature Branch**: `150-gamepack-seed-templates`  
**Created**: 2026-10-16  
**Status**: Draft  
**Input**: User description: "FR-028: seed templates in a gamepack - created once on the first install, never updated. GitHub issue #316. Closes #316."

## Clarifications

### Session 2026-10-16

- Q: What is the seed shape? → A: The seed reuses the template shape of the settings document (name, and entries with sequence, enabled flag, schedule type, time of day or relative offset, parameter values). The seed adds no field (FR-002).
- Q: How does the platform know a first install? → A: The gamepack registry has no record for the name. No route uninstalls a gamepack today, so a name stays in the registry after the first install (FR-003, FR-005).
- Q: What if the first install fails? → A: The seed step runs after the objects are applied. If the install fails or rolls back, the platform creates no seed template, or removes the seed templates it created in this failed install (FR-014).
- Q: Does a case difference in the template name count as the same name? → A: Yes. Template names are compared without case, as in the settings document (FR-004).
- Q: What does a rollback of the gamepack do to seeds? → A: Nothing. The rollback restores the previous version of the owned objects only (FR-007).
- Q: Is a seed template that a user created by hand before the first install changed? → A: No. It is skipped with `skipped_exists` (FR-004).

### Session 2026-10-17 (analyze loop 1)

- Q: What happens to seeds on a later install or update? → A: They are ignored. Structure checks still run. Reference checks run on a first install only (FR-015).
- Q: What if the template name becomes used between plan and write? → A: The applier checks again and skips with `skipped_exists` (FR-016).
- Q: Does the signature cover seeds? → A: Yes. A file without seeds keeps its hash (FR-017).
- Q: What do the result lists hold when it is not a first install? → A: Both are present and empty (FR-010).

### Session 2026-10-17 (analyze loop 2)

- Q: Does the dry-run list always match the real install (SC-003)? → A: Yes, when no concurrent change happens between the dry run and the install. FR-016 covers the race case.
- Q: What if the undo of a failed first install fails (FR-014)? → A: The undo is best effort. It deletes by the ids this install created. It logs each removal error and continues.
- Q: What first-install state do a rollback and a managed update use? → A: Always "not first install" for seed purposes, with empty seed lists.
- Q: How do seed sequence names map to ids? → A: Names resolve to final ids when the plan is built (FR-009).
- Q: Does the uninstall clause of FR-007 need a test? → A: No. No uninstall route exists, so the clause holds by construction until a route is added.
- Q: How is SC-005 checked? → A: By the quickstart manual step. No separate task is needed.

## User Scenarios & Testing *(mandatory)*

### User Story 1 - New user gets a starting template (Priority: P1)

A gamepack author adds one or more seed queue templates to a gamepack. A new user installs the gamepack for the first time. The platform creates each seed template that does not exist. The user sees the order, schedule and parameters of the tasks without a written guide.

**Why this priority**: This is the core value. Without it, the user builds the template by hand.

**Independent Test**: Install a gamepack with one seed on an instance that has no template of that name. Check that the template exists and that the install result lists it as created.

**Acceptance Scenarios**:

1. **Given** a gamepack name never installed and a seed whose template name does not exist, **When** the user installs the gamepack, **Then** the template is created with the entries of the seed and the install result lists it as created.
2. **Given** a gamepack name never installed and a seed whose template name already exists, **When** the user installs the gamepack, **Then** the existing template is not changed and the install result lists the seed as skipped with the reason `skipped_exists`.
3. **Given** a gamepack with no seed list, **When** the user installs it, **Then** the install works as before and both seed lists in the result are empty.

---

### User Story 2 - User adjustments stay safe (Priority: P1)

After the first install, the template belongs to the user. The user changes it, or deletes it. A later install or update of the same gamepack name never creates, updates or deletes a seed template.

**Why this priority**: The seed must never overwrite local adjustments. This is the reason for the request.

**Independent Test**: Install, change the template, update the gamepack with a changed seed, and check that the template is unchanged. Delete the template, install again, and check that it is not created again.

**Acceptance Scenarios**:

1. **Given** a gamepack installed before and a seed template changed by the user, **When** the user installs a new version, **Then** the template is not changed.
2. **Given** a gamepack installed before and a seed template deleted by the user, **When** the user installs again or updates the gamepack, **Then** the template is not created again and the seed is not in the created list.
3. **Given** a gamepack installed before, **When** a new version adds a new seed, **Then** the new seed is not created.
4. **Given** a created seed template, **When** the user rolls back the gamepack install, **Then** the template stays.
5. **Given** a created seed template, **When** the user exports the settings document, **Then** the template is in the export as an ordinary template, with no link to the gamepack.

---

### User Story 3 - Dry run shows the seeds (Priority: P2)

Before an install, the user runs a dry run. The dry run shows which seeds it would create and which it would skip. A seed entry that names a sequence that is neither in the gamepack nor on the instance is a `reference` problem.

**Why this priority**: The user can check the result before a change. It follows the existing dry-run behavior.

**Independent Test**: Run a dry run with a seed and check the lists and that no state changes.

**Acceptance Scenarios**:

1. **Given** a first install with one new seed and one seed whose name exists, **When** the user runs a dry run, **Then** the report lists the first as "would create" and the second as "would skip" and no template is created.
2. **Given** a seed entry that names a sequence in neither the gamepack nor the instance, **When** the user runs a dry run or an install, **Then** a `reference` problem is reported and the install does not start.
3. **Given** a gamepack installed before, **When** the user runs a dry run, **Then** no seed is in the "would create" list.

### Edge Cases

- A seed entry names a sequence that is in the gamepack: valid, even if the sequence is not yet on the instance.
- A seed entry names a sequence that is on the instance only: valid.
- Two seeds in one gamepack have the same template name: the gamepack is invalid (validation problem).
- A seed has an invalid schedule type, a bad timer, or a bad parameter value: a validation problem, as for a template in the settings document.
- The first install fails and rolls back: no seed template stays created by that failed install.
- No route uninstalls a gamepack today. If one is added later, the install record decides whether an install is a first install.
- The install runs through the catalog install route: seeds act in the same way.
- A managed update of a gamepack that has seeds: seeds are not created, updated or deleted.

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: A gamepack file MUST accept an optional list of seed queue templates. A file without the list MUST stay valid and MUST install as before.
- **FR-002**: Each seed MUST have the same shape as a template in the settings document: a name, and entries with sequence name, enabled flag, schedule type, timers and parameter values. The shape of the settings document MUST NOT change.
- **FR-003**: On the first install of a gamepack name, the platform MUST create each seed template whose name does not exist.
- **FR-004**: On the first install of a gamepack name, the platform MUST NOT change a template whose name matches a seed. It MUST report that seed as skipped with the reason `skipped_exists`.
- **FR-005**: On a later install or update of the same gamepack name, the platform MUST NOT create, update or delete any seed template. This holds also when the user deleted the template, and also for a seed that is new in the later version.
- **FR-006**: A created seed template MUST be an ordinary template. The settings export MUST list it as an ordinary template. The template MUST have no link to the gamepack.
- **FR-007**: A rollback of a gamepack install MUST NOT remove a created seed template. An uninstall MUST NOT remove it.
- **FR-008**: The dry run MUST list the seeds it would create and the seeds it would skip, and MUST NOT change state.
- **FR-009**: A seed entry that names a sequence that is neither in the gamepack nor on the instance MUST give a `reference` problem, in the dry run and in the install.
- **FR-010**: The install result MUST have two separate lists: created seed templates and skipped seed templates (each skipped item with its reason). Both lists MUST be present. They MUST be empty when the install is not a first install, or when the file has no seeds.
- **FR-011**: The platform MUST validate the seeds (unique names, valid schedule type, timers and parameter values) with the same rules as for a template in the settings document, and MUST report errors as problems before any change.
- **FR-012**: The OpenAPI document and the docs MUST describe the new gamepack field and the new result fields.
- **FR-013**: Tests MUST cover each acceptance scenario above.
- **FR-014**: If a first install fails after some seed templates were created, the platform MUST remove the seed templates that this install created, so that a failed install leaves no seed template. The removal is best effort undo, deletes by created ids, and logs errors.
- **FR-015**: On a later install or update, the platform MUST ignore the seeds. It MUST still check the seed structure (FR-011) on every install. It MUST NOT check seed sequence references (FR-009) on a later install.
- **FR-016**: The platform MUST check again, just before it writes a seed template, that the name is free. If the name is now used, it MUST NOT write the template and MUST report the seed as skipped with the reason `skipped_exists`.
- **FR-017**: The signature of a gamepack file MUST cover the seed list. A signed file without a seed list MUST keep the same hash, and its signature check MUST give the same result as before this feature.

### Key Entities

- **Seed template**: A template definition inside a gamepack file. It has a name and entries. It is a start value and not an owned object.
- **Seed result**: Part of the install result. It has a created list and a skipped list with a reason per item.
- **Gamepack install record**: The existing record that shows if a gamepack name was installed before. It decides whether an install is a first install.

## Assumptions

- "First install of a gamepack name" means that the platform has no install record for that name.
- A failed install does not count as an install and leaves no seed template.
- The names of the new fields are free. The plan decides them.
- No web UI change is in scope.
- The shared queue-template validation rules class has a neutral name: `QueueTemplateRules`. It is not in a Seeds folder, because the queue template route also uses it.
- The PR bumps the version in `installer\versioning\version.override.json`. The plan and the tasks must include a task for this bump before the push.
- The dry-run result wiring is shared with the install wiring, in one task. The dry-run task only checks that no template write happens.
- The first-install registry lookup and both branches (first install and not first install) go in the one validator task. A later task only adds the reference-check skip and the rollback guard.
- Tasks must not duplicate a test across user stories. A [P] marker goes only on a task that does not depend on an unfinished task.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: After a first install, 100% of seeds with a free template name exist as templates, and 100% of seeds with a used name are reported as `skipped_exists`.
- **SC-002**: In 100% of tested later installs and updates (including after deletion), zero seed templates are created, updated or deleted.
- **SC-003**: A dry run changes zero stored items, and its lists match the result of the real install, when no concurrent change happens between the dry run and the install.
- **SC-004**: Every gamepack file without a seed list installs with the same result as before this feature.
- **SC-005**: A user can start the tasks of a new game from a seed template without a written guide.
