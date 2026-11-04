# Feature Specification: Fix inline WaitForImage in installed gamepack sequences

**Feature Branch**: `177-fix-installed-waitforimage`  
**Created**: 2026-11-04  
**Status**: Implemented  
**Input**: User description: "Fix B-039: inline WaitForImage steps of installed gamepack sequences run as a 1 s delay with no image. Issue #373. Closes #373."

## Clarifications

### Session 2026-11-04

- Q: Which payload fields does the shared mapping read? → A: The fields the save path reads today: timeoutMs (default 1000 ms), until, absentCaptures, and detectionTarget (reference image id, confidence, offsets). Install adds no new field and drops none. Rationale: equal behavior with the save path is the goal.
- Q: Does install reject a WaitForImage payload with no image id, or does it keep the step? → A: Install keeps the step and gives the same configuration the save path gives for the same payload (no detection target). Install adds no new validation. Rationale: no change to install validation rules; the bug is only the missing configuration.
- Q: Where must the shared mapping live so both paths can call it? → A: In a place that the Domain gamepack code and the Service endpoint can both reach (the Domain layer). The Service endpoint keeps no private copy. Rationale: Service depends on Domain, not the other way round.
- Q: (Analyze I6) Is the detection region reader shared too? → A: Yes. The region reading is part of the one shared mapping. The save path and install both use it; no second region reader stays. The save-path region check (feature 130) stays in Service and runs before the mapping. Rationale: FR-005 allows no second copy.
- Q: (Analyze I7) Which payload value types must the mapping accept? → A: Every type that the save path accepts today (JSON element, string, number). The install test MUST start from a real pack JSON file, so it shows the real value types of the pack read path.
- Q: (Loop 2, D1) Can the Service keep its own region reader for the save-time check? → A: No. The Domain region reader is the only reader. The save-time region check in Service calls it for its error text and keeps its error messages. Rationale: two readers can drift apart, which is the cause of B-039.
- Q: (Loop 2, D2) Where does the number reading (int, long, JSON number, string) live? → A: In Domain, with the mapper. Service calls it. No second number parser stays for the WaitForImage fields.
- Q: (Loop 2, I1) How is save and install equality proved? → A: A contract test compares the stored step of a pack install with the stored step of POST /api/sequences for the same payload. A test that compares the mapper with itself does not count.
- Q: (Loop 2, I2) What if the pack read path gives a value type that the mapper does not read? → A: The plan MUST first check the real runtime type of the nested detectionTarget value after a pack read. The mapper MUST accept that type. A test that starts from a real pack file proves this.
- Q: (Loop 2, C1) Do the checks on image references change? → A: No. Image-reference collection and validation of a pack behave as before. A test with a missing reference image on a WaitForImage step shows the install result does not change.
- Q: (Loop 2, A1) What does an update do to old installed sequences? → A: An update of a gamepack-owned pack writes the corrected steps, even if the old content looked equal. A test covers this.
- Q: (Analyze I10) How do we prove the run-time result? → A: The runner is unchanged and reads only the WaitForImage configuration. A test that checks the stored configuration of the installed step (equal to the saved step) proves SC-002. A manual run is optional.
- Q: (Analyze I4, I5) Which process rules apply? → A: The bug-fix rule applies: a failing reproduction test MUST be written and shown to fail before the fix. The living-documentation rule applies: the work MUST update specs/STATUS.md and the spec Status, and MUST review docs/architecture.md.
- Q: (Analyze I1) Where does the mapping run in install? → A: In the gamepack plan builder, after the command ids are rewritten and before the planned object is added and the take-over hash is computed. It does not run in the applier. The walk code is in a new Domain helper file. Rationale: the content must be final before the hash.
- Q: (Analyze I2, I3) Where are Body and ElseBody, and how does the walk go? → A: Body and ElseBody are members of the step itself, not of the if configuration. The walk is one recursive walk from the start. Rationale: this covers all depths with one code path.
- Q: (Analyze I8) What if a step has no action? → A: The mapping skips the step and does not fail. Rationale: install adds no new validation.

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Installed inline WaitForImage waits for the image (Priority: P1)

An operator installs a gamepack that has sequences with inline WaitForImage steps (for example, a step with a 3000 ms timeout and the reference image "pns-city-anchor", confidence 0.85). The operator runs the installed sequence, directly or in a queue. The step must wait for the image up to 3000 ms and end at the first match. Today the step runs as a plain 1 s delay and ignores the image. A menu check then fails, and the sequence and the next sequences of the queue fail.

**Why this priority**: This is the reported bug. It makes installed gamepack sequences fail without any warning.

**Independent Test**: Install a pack with a sequence that has one inline WaitForImage step. Run the sequence with the image on screen. The step log shows exitCondition "image_detected", effectiveTimeoutMs 3000 and the image id set.

**Acceptance Scenarios**:

1. **Given** a gamepack with an inline WaitForImage step (timeoutMs 3000, image id set), **When** the operator installs the pack and runs the installed sequence, **Then** the step ends at the first match with exitCondition "image_detected", effectiveTimeoutMs 3000 and the image id set.
2. **Given** the same pack, **When** the operator saves the same step with POST /api/sequences, **Then** the installed step and the saved step behave the same at run time.
3. **Given** an installed gamepack, **When** the operator does an install with takeOver, or an update of a gamepack-owned pack, **Then** the WaitForImage step behaves correctly in both cases, in queue runs and in direct runs.
4. **Given** an installed sequence, **When** the operator makes no PUT of the sequence, **Then** the step still behaves correctly (no repair step is needed).

---

### User Story 2 - Nested inline WaitForImage steps also work (Priority: P1)

A pack can put WaitForImage steps inside loop and if steps (the Body and ElseBody members of the step). These nested steps must behave the same as top-level steps after install.

**Why this priority**: The same fault affects nested steps. A fix for top-level steps only leaves the bug in loops and conditions.

**Independent Test**: Install a pack with a WaitForImage step inside a loop Body and inside an if ElseBody. Read the installed sequence and check that each nested step has the timeout and the image id. Run the sequence and check the exit condition.

**Acceptance Scenarios**:

1. **Given** a pack with a WaitForImage step in a loop Body, **When** the pack is installed, **Then** the nested step has the same timeout and image as in the pack.
2. **Given** a pack with a WaitForImage step in an if Body and in an if ElseBody, **When** the pack is installed, **Then** both nested steps have the correct timeout and image.

---

### User Story 3 - Save and install use the same rules (Priority: P2)

A maintainer changes the WaitForImage rules in one place only. The rules for how a step payload becomes a WaitForImage configuration are the same for the save endpoint and for gamepack install.

**Why this priority**: One implementation prevents the two paths from drifting apart again.

**Independent Test**: Review the code and tests. Save and install both call one shared mapping. A test shows equal results for the same payload.

**Acceptance Scenarios**:

1. **Given** the same WaitForImage payload, **When** it goes through the save path and through the install path, **Then** both paths give the same configuration.

---

### Edge Cases

- A WaitForImage payload with no timeoutMs: install gives the same default as the save path.
- A WaitForImage payload with an "until" setting (for example, until absent): install keeps it, as the save path does.
- A payload with no image id: install keeps the step with no detection target, as the save path does. Install adds no validation.
- A pack with other step types (for example, WaitForStill, commands): their install result does not change.
- Sequences that were installed before this fix: not changed by this fix. The operator can reinstall or update the pack to get the corrected steps.
- A pack with deeply nested steps (a loop inside an if inside a loop): every level is handled.

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: On gamepack install, the system MUST build the WaitForImage configuration for every inline WaitForImage step of an installed sequence, from the step payload (image, timeout, confidence, until).
- **FR-002**: The system MUST do FR-001 for top-level steps and for nested steps (the Body member of loop and if steps, the ElseBody member of if steps, and deeper levels), with one recursive walk. The mapping MUST be null-safe: a step with no action is skipped and install does not fail.
- **FR-003**: The system MUST do FR-001 for a new install, for an install with takeOver, and for an update of a gamepack-owned pack.
- **FR-004**: An installed inline WaitForImage step MUST behave at run time the same as the same step saved through POST /api/sequences: exitCondition "image_detected" at first match, the image id set, and effectiveTimeoutMs equal to the step timeoutMs.
- **FR-005**: The save endpoint and the gamepack install MUST use one shared mapping from payload to WaitForImage configuration. The system MUST NOT keep a second copy of this logic.
- **FR-005a**: The shared mapping MUST read the same payload fields as the save path today (timeoutMs with default 1000 ms, until, absentCaptures, detectionTarget) and MUST live where both the gamepack install code and the save endpoint can call it. Install MUST add no new validation.
- **FR-005d**: The Domain mapping (including region reading and number reading) MUST be the only reader of the WaitForImage payload fields. The Service save-time region check MUST call it. The mapping MUST accept the real value types of the pack read path.
- **FR-005b**: The shared mapping MUST include the detection region reading. Install MUST apply the mapping where the plan builder sets the final sequence content, before the content hash for take-over is computed, so the hash matches the stored content.
- **FR-005c**: The nested-step walk MUST visit the Body of loop and if steps and the ElseBody of if steps (these are members of the step itself), at every depth.
- **FR-006**: The fix MUST NOT change WaitForImage steps in commands, WaitForStill steps, or any other step type.
- **FR-007**: The fix MUST NOT add a new API endpoint or change an API contract.
- **FR-008**: An automated test MUST install a pack with an inline WaitForImage step and check that the installed step has timeoutMs 3000 and the image id. A second case in the same test set MUST cover a nested step.
- **FR-009**: The fix MUST NOT change the stored step data that the API shows for existing steps, apart from the added configuration.

### Key Entities

- **Sequence step (WaitForImage)**: A step that waits for a reference image. Has an action payload (timeout, detection target with image id and confidence, until) and a WaitForImage configuration that the runner reads.
- **Gamepack**: A package with sequences and commands. Install creates stored sequences from the pack.
- **WaitForImage configuration**: The data the runner uses to wait: image id, confidence, timeout, until setting.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: After install, 100% of inline WaitForImage steps (top-level and nested) of the test pack have the timeout and image id from the pack.
- **SC-002**: A run of an installed sequence with the image on screen ends the wait at the first match, not after 1 s, in 100% of test runs.
- **SC-003**: The result of an installed step equals the result of the same step saved through POST /api/sequences, with 0 differences in the compared fields.
- **SC-004**: The operator needs 0 manual PUT calls to repair an installed sequence.
- **SC-005**: Existing tests for commands, WaitForStill and the save endpoint continue to pass.

## Assumptions

- The fault is in the install path only; the runner and the save endpoint are correct.
- Sequences installed before this fix are repaired by a reinstall or an update of the pack. No data migration is part of this fix.
- This feature does not change the web UI.
- Constraint: the nested-step walk runs in the gamepack plan builder (the validator code that loops over the pack sequences after the command ids are rewritten), in a new Domain helper file. It runs before the planned object is added and before the take-over hash is computed. The applier is not changed. The content is final before the hash.
- The exact file paths for new code and tests are fixed in the plan and tasks. New tests respect the contract test shard balance rule.
- The task list states task order as a strict dependency list; tests come before the code they test; the manual run is optional proof for SC-002.
