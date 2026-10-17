# Feature Specification: Accept any member order in gamepack files

**Feature Branch**: `154-gamepack-any-member-order`  
**Created**: 2026-10-17  
**Status**: Draft  
**Input**: User description: "Issue #322 (B-035): gamepack dry run returns 500 when the type member of a condition is not first. Closes #322. The install endpoint must accept polymorphic objects with the type member in any position. A missing or bad type gives 400 with the JSON path, never 500. Hash and signature results are the same for both orders. Tests cover type first, last, missing."

## Clarifications

### Session 2026-10-17

- Q: How does the platform treat a duplicate "type" member in one object? → A: Same as a bad "type": status 400 with the JSON path (no 500).
- Q: Which scope applies for the fix? → A: All polymorphic types that the gamepack parse path reads, through the shared JSON options, so that the same rule applies to every one of them.
- Loop 1 (analyze I2, A2, C1): The 400 body MUST use the error code that the install endpoint already uses for an invalid file (the `InvalidJson` status), and the message MUST hold the JSON path and the reason. The large-pack edge case is a non-measurable note: the fix adds no second parse pass. The tests for "type in the middle" run at unit level and at endpoint level.
- Loop 2 (analyze F1, U2): Out-of-order member reading applies only to the parser options that the gamepack install path uses. If the same options object also serves a non-install reader, the change is accepted only if it has no visible effect there (it accepts more files, never fewer). Status per error class: a bad, missing, duplicate or not-string "type" gives 400 with the `InvalidJson` code in dry run and in real install; other wrong-shape problems keep their current status.
- Q: What does the 400 message contain? → A: The existing error code of the install endpoint for invalid files, plus the JSON path (for example `$.steps[0].breakCondition`) and a short reason (missing type, unknown type, bad type value).

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Install a pack whose type member is not first (Priority: P1)

A pack author writes the pack files with the members of each object in ordinal order. This is the order that the signature rules use for the canonical text. In this order, the "type" member of a condition object is not the first member. The author runs a dry run and then a real install. Both calls must succeed, the same as for a file with "type" first.

**Why this priority**: Today the dry run fails with an internal error (500) for a file in canonical order. A client that follows the signature rules cannot install a pack. This is the reported defect.

**Independent Test**: Send one pack that has a step condition with members `imageId`, `minSimilarity`, `negate`, `type` (in this order) to the install endpoint, with dry run on and with dry run off. Compare with the same pack that has `type` first.

**Acceptance Scenarios**:

1. **Given** a pack with a step condition where "type" is the last member, **When** the author sends a dry run, **Then** the answer is a success (same as for "type" first) and not 500.
2. **Given** the same pack, **When** the author sends a real install, **Then** the install succeeds and the stored objects are the same as for the "type first" file.
3. **Given** two packs that differ only in the member order of an object, **When** the platform checks the content hash and the signature, **Then** both checks give the same result for both packs.

---

### User Story 2 - Clear error for a missing or bad type (Priority: P2)

A pack author makes a mistake: a polymorphic object has no "type" member, or the "type" value is not known. The author gets a client error (400) that names the JSON path of the bad object. The author can then fix the file. The platform never answers with 500 for this kind of file error.

**Why this priority**: A file error must not look like a platform fault. The author needs the path to find the error.

**Independent Test**: Send a pack with a step condition that has no "type", and a pack with a step condition that has an unknown "type" value. Check for status 400 and the JSON path in the answer, for dry run and for real install.

**Acceptance Scenarios**:

1. **Given** a pack with a polymorphic object that has no "type" member, **When** the author sends a dry run or a real install, **Then** the answer is 400 and the message contains the JSON path of the object.
2. **Given** a pack with a polymorphic object with an unknown "type" value, **When** the author sends a dry run or a real install, **Then** the answer is 400 and the message contains the JSON path.

---

### Edge Cases

- The "type" member is in the middle of the members of the object.
- A polymorphic object is nested in another polymorphic object (for example, a condition in a condition).
- Other polymorphic types in the pack parse path (not only the step condition) have the same problem. They must follow the same rule.
- A file has a "type" member with a value that is not a string (for example, a number or null).
- A file has a duplicate "type" member.
- A pack that is very large or has many objects must not be slower in a measurable way.

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: The platform MUST accept the members of every polymorphic object in a pack file in any order. The "type" member can be first, in the middle, or last.
- **FR-002**: The dry run and the real install MUST give the same result for two files that differ only in the member order of an object.
- **FR-003**: The content hash and the signature check MUST give the same result for both member orders. The canonical text rules and the hash algorithm MUST NOT change.
- **FR-004**: If a polymorphic object has no "type" member, the platform MUST answer with status 400 and a message that contains the JSON path of the object. It MUST NOT answer with 500.
- **FR-005**: If a polymorphic object has a bad "type" member (unknown value, or a value that is not a string), the platform MUST answer with status 400 and a message that contains the JSON path. It MUST NOT answer with 500.
- **FR-006**: The rules in FR-001 to FR-005 MUST apply to all polymorphic types that the pack parse path reads, not only to the step condition. The plan MUST list every polymorphic type in the gamepack parse path. Each listed type MUST have at least one test.
- **FR-007**: Automated tests MUST cover three cases: "type" first, "type" last, and "type" missing. Each case MUST run for dry run and for real install.
- **FR-008**: Only the two install routes (dry run and real install) change. Other API endpoints and code paths MUST keep their current behavior.
- **FR-009**: The code that classifies a type error as a 400 answer MUST NOT depend on runtime message text where a structural check exists.

### Key Entities

- **Gamepack file**: A JSON file in a pack that holds an object such as a sequence. Objects in it can be polymorphic (the "type" member selects the kind).
- **Polymorphic object**: An object whose kind depends on its "type" member, for example a step condition.
- **Canonical text**: The text that the platform builds from the parsed file, with keys in ordinal order. The hash and signature use it.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: A pack in canonical member order installs with success in 100% of test runs, for dry run and for real install.
- **SC-002**: For each tested pair of files that differ only in member order, the hash result and the signature result are equal.
- **SC-003**: For files with a missing or bad "type", 100% of tested cases give status 400 with a JSON path, and 0 cases give status 500.
- **SC-004**: The existing gamepack tests all pass without change in their expected results.

## Assumptions

- The reported failure comes from the JSON parser rule that needs the "type" member first for polymorphic objects. The fix is a parse rule change, not a change of the file format.
- The existing 400 error format of the install endpoint is used for the new error cases.
- Test projects are in `tests/unit`, `tests/contract`, and `tests/integration`. New tests go there.
- The dry run already answers 400 with the `InvalidJson` error code for a bad file. The new type errors use this same path and code.
- Scope of FR-008: the change applies to the two install routes only (dry run and real install). Other paths only keep their current behavior.
- Constraint: the code that sorts a type error into a 400 answer must not depend on runtime message text where a structural check exists. It reads the JSON path and the reason from the exception data. If text matching is unavoidable, one unit test MUST cover each message form.
- Required nested test (U1): one concrete test sends a bad "type" value in a nested object where "type" is not the first member. The answer is 400 and the message holds the nested JSON path.
- Large pack check (C1): one smoke test with hundreds of objects gives the same result as a small pack. The test makes no assertion on the number of parse passes.
- File location (I1): the plan MUST find and name the exact file path of `SettingsDocumentJson` (the shared JSON options holder), and MUST state whether a non-install reader uses it.
- Build order (I2): tests that use new types compile only after the foundational phase of the tasks. The task order MUST show this.
- Non-goals: no change to the canonical text rules or the hash algorithm; no change to non-gamepack API endpoints; no new gamepack features.
