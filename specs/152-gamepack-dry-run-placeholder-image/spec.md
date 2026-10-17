# Feature Specification: Gamepack dry run skips parameter placeholder image ids

**Feature Branch**: `152-gamepack-dry-run-placeholder-image`  
**Created**: 2026-10-17  
**Status**: Draft  
**Input**: User description: "B-036: the reference check of a gamepack dry run reports a problem for an image id that is a parameter placeholder, for example `{{novaOptionImage}}`. GitHub issue #326. Closes #326."

## Clarifications

### Session 2026-10-17

- Q: What is "one whole placeholder"? → A: The rule of `IsWholePlaceholder` (the field-template check): the text, after trimming outer white space, is exactly `{{name}}` and holds one key. Text around the placeholder (`pre-{{name}}`) is not whole and is checked as before. Spaces inside the braces are not matched by this rule; the result is a `reference` problem.
- Q: Does the parameter name match without letter case? → A: The match uses the same name comparison as the engine for sequence parameters (ordinal, as for declared names). A different letter case is a different name and is checked as before.
- Q: Does a built-in name (for example `{{iteration}}`) count as a declared parameter? → A: No. Only a parameter that the sequence declares counts (FR-001, FR-006).
- Q: Which dry run checks change? → A: Only the image id reference check of sequences. The command reference check, the check of commands, and the seed template checks stay as they are.

- Q: Who uses the whole-placeholder rule today? → A: The command step field-template check in `ParameterValidationService` (`invalid_field_template_value`), not the queue template save. The queue template save uses `unknown_image_reference` and stays unchanged. The shared helper keeps the field-template result the same; its tests guard this.
- Q: What is the result for `{{ name }}` and for a name in other letter case? → A: The engine key pattern is `\{\{(\w+(?:\.\w+)*)\}\}` and the name compare is ordinal. So `{{ name }}` is not a placeholder: it is checked as before and gives a `reference` problem. `{{Name}}` with a declared `name` is a different name: it is checked as before and gives a `reference` problem. Tests assert both.
- Q: Which image id sources does the skip cover? → A: All sources of `GamepackReferences.ImageIds(sequence)`: step conditions, `breakCondition`, `if` conditions (including `elseBody` steps), loop conditions, and nested steps. Tests cover a Loop `breakCondition` and an `if` branch. An id source outside that method is out of scope.
- Q: Does a command image id with a placeholder change? → A: No. It is checked as before. A test locks this.
- Q: Where do the dry run and install tests live? → A: One dry run test and one separate install test, with the minimal JSON of the issue as the fixture.
- Q: What about the OpenAPI text? → A: Check the gamepack schema filter text. If it lists reference rules, add one sentence. "No change needed" is an allowed result and the task records it.
- Q: Use one term for the placeholder? → A: Use "declared whole placeholder" for an image id that is one whole placeholder with a parameter of that name declared in the same sequence.

### Session 2026-10-17 (loop 2)

- Q: What if a sequence has no parameter list (null or empty)? → A: The declared set is empty. Every placeholder image id is checked as before. A test covers a sequence with no parameters.
- Q: Where do the new gamepack tests live? → A: In the integration test project, next to the existing gamepack dry run tests. Unit tests for the helper and the validator live in the unit test project.
- Q: Which commands run the full suites, and how are flaky CI tests handled? → A: The tasks list the exact commands. A known flaky CI test is rerun once before it counts as a failure.

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Dry run accepts a parameterized image id (Priority: P1)

A gamepack author puts a sequence in a gamepack file. The sequence declares a parameter, for example `novaOptionImage`. A condition of the sequence uses the whole placeholder `{{novaOptionImage}}` as its image id. The queue template entry gives the value at run time. The author runs the dry run. The dry run reports no `reference` problem for that image id.

**Why this priority**: Today the dry run reports `valid: false` for such a sequence. The same sequence saves without a problem through `POST /api/sequences`. A gamepack cannot carry a parameterized sequence and pass its dry run, so release 1.0.0 of the requester (it has `PNS.NovaInteract`) cannot pass its release proof.

**Independent Test**: Send `POST /api/gamepacks/install?dryRun=true&takeOver=true` with a gamepack that has one sequence with the parameter `novaOptionImage` and the condition `{"type":"imageVisible","imageId":"{{novaOptionImage}}","minSimilarity":0.85,"negate":false}`. The result is `valid: true` with an empty problem list.

**Acceptance Scenarios**:

1. **Given** a sequence that declares the parameter `name` and uses `{{name}}` as an image id, **When** the user runs the dry run, **Then** the result has no `reference` problem for that image id.
2. **Given** the same gamepack and no other problem, **When** the user runs the dry run, **Then** the result is `valid: true`.
3. **Given** the same gamepack, **When** the user installs it (not a dry run), **Then** the install works as it does for any other valid gamepack.

---

### User Story 2 - Other image ids are still checked (Priority: P1)

An author makes a mistake in an image id. The dry run still reports it.

**Why this priority**: The skip must not hide real errors. A missing image must still give a `reference` problem.

**Independent Test**: Run the dry run with each of the cases below. Each case gives the same result as before the change.

**Acceptance Scenarios**:

1. **Given** a sequence that uses `{{name}}` as an image id and declares no parameter `name`, **When** the user runs the dry run, **Then** the result has a `reference` problem for that image id.
2. **Given** a sequence that uses an image id with text around a placeholder (for example `pre-{{name}}` or `{{name}}-x`), **When** the user runs the dry run, **Then** the image id is checked as before and the result has a `reference` problem.
3. **Given** a sequence that uses a plain image id that is not in the file and not in the installation, **When** the user runs the dry run, **Then** the result has a `reference` problem.
4. **Given** a sequence that uses a plain image id that is in the file, **When** the user runs the dry run, **Then** the result has no problem for it.
5. **Given** a sequence that declares the parameter `a` and uses `{{b}}` as an image id, **When** the user runs the dry run, **Then** the result has a `reference` problem.

---

### Edge Cases

- The placeholder has spaces inside the braces (for example `{{ name }}`): this is not a placeholder, because the engine key pattern does not match it. The image id is checked as before and gives a `reference` problem.
- The parameter name differs from the placeholder name only in letter case (for example `{{Name}}` and a declared parameter `name`): the name compare is ordinal, so these are different names. The image id is checked as before and gives a `reference` problem.
- An image id placeholder appears in a nested step (a Loop body, or a conditional `body` or `elseBody`) or in a `breakCondition`: every place where the dry run checks an image id gets the same rule.
- An image id that is empty: unchanged.
- A placeholder in any other field than an image id: unchanged.

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: The reference check of a gamepack dry run MUST skip an image id that is a declared whole placeholder (one whole parameter placeholder `{{name}}`, with a parameter of that name declared in the sequence that holds the image id).
- **FR-002**: The skip MUST apply to every place in a sequence where the dry run checks an image id, including conditions, nested steps, and `breakCondition`.
- **FR-003**: An image id that is not one whole placeholder MUST be checked as before.
- **FR-004**: An image id that is not a declared whole placeholder (for example `{{name}}` with no parameter `name` declared in the sequence) MUST be checked as before. A missing image gives a `reference` problem.
- **FR-005**: A dry run of a gamepack with a parameterized image id and no other problem MUST give `valid: true`.
- **FR-006**: The skip MUST apply to the sequence that declares the parameter only. A parameter of another sequence MUST NOT count.
- **FR-007**: Tests MUST cover the placeholder case (including a nested step) and each unchanged case in User Story 2. Tests MUST also cover these cases:
  - a declared whole placeholder in a Loop `breakCondition`;
  - a declared whole placeholder in an `if` branch condition and in an `elseBody` step;
  - a command image id with a placeholder, which is checked as before (not skipped);
  - `{{ name }}` (spaces inside the braces), which gives a `reference` problem;
  - `{{Name}}` with a declared parameter `name`, which gives a `reference` problem;
  - a sequence with null or empty parameters: every placeholder image id is checked as before.
- **FR-008**: The change MUST NOT change how a queue template save checks image values, MUST NOT change how `POST /api/sequences` validates a sequence, and MUST NOT add other gamepack dry run changes (issues #322, #323, #324, #325).

### Key Entities

- **Gamepack file**: JSON file with a manifest, sequences, commands, images, and seed templates.
- **Sequence**: ordered steps and conditions. It can declare parameters.
- **Sequence parameter**: a named value. Its value comes from the queue template entry at run time.
- **Parameter placeholder**: the text `{{name}}` in a field of a sequence. It names a parameter.
- **Dry run problem**: an entry with a category (for example `reference`), an object type, a name, and a message.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: The reproduction gamepack of issue #326 (sequence `PNS.NovaInteract`, parameter `novaOptionImage`) gives `valid: true` and 0 problems in a dry run.
- **SC-002**: 5 of 5 unchanged cases in User Story 2 give the same result as before the change.
- **SC-003**: 0 gamepack files with a missing image (not a declared whole placeholder) pass the dry run.
- **SC-004**: The full unit, contract, and integration test suites pass, including the existing tests for the gamepack dry run, `POST /api/sequences`, and queue template save.

## Assumptions

- The whole-placeholder rule comes from the field-template check in `ParameterValidationService` (`invalid_field_template_value`). It does not come from the queue template save. The result of the field-template check does not change. The name compare is ordinal. The check does not add a new placeholder syntax.
- New gamepack tests are integration tests next to the existing dry run tests. A known flaky CI test is rerun once before it counts as a failure.
- The build and the tests run green before each commit.
- A queue template entry gives the value at run time, and the queue template save checks it (`unknown_image_reference`). This feature does not change that.
- The change is in the dry run reference check only. The install result for a valid gamepack does not change.
