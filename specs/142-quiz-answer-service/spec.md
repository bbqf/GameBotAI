# Feature Specification: Quiz Answer Service for Sequences

**Feature Branch**: `142-quiz-answer-service`  
**Created**: 2026-10-14  
**Status**: Draft  
**Input**: User description: "FR-020 (issue #302): Two new sequence steps, `quiz-lookup` and `quiz-learn`, and a shared answer store with list, get, and delete access for the operator. An unattended run reads a quiz question and its answers, gets the correct answer from the store, taps by the answer, and later stores the correct answer that the game shows."

## Clarifications

### Session 2026-10-14

- Q: Does a lookup with an unreadable answer region still pick? → A: No. Any unreadable question or answer region gives `unreadable` and no pick. (Rationale: a pick on bad text would teach wrong records.)
- Q: What does the default similarity mean, and what is its value? → A: A value from 0 to 1 on the normalized question text, where 1 means exact. The default is 0.85. A step can set another value. (Rationale: one tolerance for small OCR errors.)
- Q: Can the operator add or edit a record by hand? → A: No. Only list, get, and delete (FR-014). (Rationale: scope of the issue.)
- Q: What is a store name? → A: A free text name. A store that does not exist yet is created on the first learn. A lookup on a store that does not exist gives a guess, not an error. (Rationale: no setup step for the operator.)
- Q: How does the step get the game connection? → A: From the step input `game`, a game identifier. The system rejects a step with a missing game. (Rationale: FR-012 links each record to a game.)

- Q (loop 1): Does "matches exactly one answer" in learn use the similarity limit? → A: Yes. "Matches" means the normalized texts reach the similarity limit of the step (default 0.85). Exactly one answer must reach it; none gives `skipped_no_match`, more than one gives `skipped_ambiguous`. (Rationale: analyze I1; one tolerance for OCR errors in the lookup and in the learn.)
- Q (loop 1): Which filters has the list operation? → A: `store`, `game`, and `category`. The record also shows its store name. (Rationale: analyze I2.)
- Q (loop 1): What is "unreadable"? → A: A region is unreadable when its text is empty or only white space after normalization. A text-reading engine failure also gives `unreadable`. `service_unavailable` is only for a store that cannot be used. (Rationale: analyze U1.)
- Q (loop 1): Is the game and category match case-sensitive? → A: The game is an exact identifier match. The category match ignores case. (Rationale: analyze U2.)
- Q (loop 1): What if the referenced lookup did not run (other branch)? → A: The save-time check covers only the order and the type. At run time a learn with no lookup result gives `skipped_no_match` and the log says why. (Rationale: analyze U3.)

- Q (loop 2): Does the web UI get a field form for the new steps? → A: Yes, a basic field form plus the type list entry. The API docs get an example step for each new step. (Rationale: analyze C2, C3, A1; the author must be able to build the steps in the editor.)
- Q (loop 2): Where is the 50 ms value? → A: It is a test budget only. SC-005 is the spec limit. (Rationale: analyze A2.)

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Answer a quiz from the store (Priority: P1)

A sequence author adds a `quiz-lookup` step after the quiz screen shows one question and a list of answers. The step reads the text of the question region and of each answer region. It normalizes the texts and asks a named store for the correct answer of this question. When the store knows the question, the step finds the answer in the list by its text and sets the outcome `pick_N`, where N is the position of that answer in the list of answer regions. A later condition on the outcome lets the sequence tap the answer at that position.

**Why this priority**: This is the core need. Without it, an unattended run can only guess with a chance of one in three.

**Independent Test**: Put one record in the store. Run a sequence with `quiz-lookup` on a screen that shows the same question. Check that the outcome is `pick_N` for the position of the stored answer, and that the run log says `known`.

**Acceptance Scenarios**:

1. **Given** a store that knows the question, **When** the `quiz-lookup` step runs, **Then** the outcome is `pick_N` for the position of the answer whose text matches the stored answer text, and the run log says `known`.
2. **Given** a store that knows the question and an answer list in a different order than when the record was stored, **When** the step runs, **Then** the outcome is the new position of the same answer text.
3. **Given** a store that does not know the question, **When** the step runs, **Then** the outcome is `pick_N` for a random position in the list, and the run log says `guess`.
4. **Given** a question region or an answer region with text that the system cannot read, **When** the step runs, **Then** the outcome is `unreadable` and no random pick is made.
5. **Given** that the store cannot be used, **When** the step runs, **Then** the outcome is `service_unavailable`.
6. **Given** a later condition on the outcome of the step, **When** the sequence runs, **Then** the condition can read the outcome and choose the branch.

---

### User Story 2 - Learn the correct answer after the game shows it (Priority: P1)

After the confirm, the game shows the correct answer. A sequence author adds a `quiz-learn` step that points to the earlier `quiz-lookup` step and names the region (or the position) of the correct answer. The step stores the pair of question text and correct answer text in the store of the lookup. All accounts that use this store profit from the record.

**Why this priority**: Learning makes the answer rate grow over time. Without it, the store stays empty.

**Independent Test**: Run a lookup on an unknown question, then run a learn step that names the correct answer. Check that the outcome is `stored`. Run the lookup again on the same question. Check that the log says `known`.

**Acceptance Scenarios**:

1. **Given** a finished lookup and a correct answer region whose text matches exactly one answer that the lookup read, **When** the `quiz-learn` step runs, **Then** the record is stored and the outcome is `stored`.
2. **Given** a correct answer text that matches none of the answers that the lookup read, **When** the step runs, **Then** nothing is stored and the outcome is `skipped_no_match`.
3. **Given** a correct answer text that matches more than one answer that the lookup read, **When** the step runs, **Then** nothing is stored and the outcome is `skipped_ambiguous`.
4. **Given** that the store cannot be used, **When** the step runs, **Then** the outcome is `service_unavailable`.
5. **Given** a lookup that made a guess, **When** a later step runs, **Then** the guess is not stored as a correct answer. Only a `quiz-learn` step stores a record.
6. **Given** a correct answer that the step names by position instead of by region, **When** the step runs, **Then** the step uses the answer text that the lookup read at that position.

---

### User Story 3 - Inspect and correct the store (Priority: P2)

The operator lists the records of the store, reads one record, and deletes a wrong record. This lets the operator fix a wrong answer that a run stored. After the delete, the next lookup of that question is a guess until a run learns it again.

**Why this priority**: A wrong record would give a wrong answer for all accounts for ever. The operator needs a way to remove it.

**Independent Test**: Store two records. List them, read one by its identifier, delete it, and list again. Check that the deleted record is gone and the other record stays.

**Acceptance Scenarios**:

1. **Given** stored records, **When** the operator lists the records, **Then** the response shows each record with its game, category, question text, answer text, and identifier.
2. **Given** a list request with a game, a category, or both, **When** the operator lists the records, **Then** the response shows only records that match.
3. **Given** a record identifier, **When** the operator reads the record, **Then** the response shows the record. An unknown identifier gives a clear "not found" response.
4. **Given** a record identifier, **When** the operator deletes the record, **Then** the record is gone and a later lookup does not find it. An unknown identifier gives a clear "not found" response.

---

### User Story 4 - Keep questions apart by game and category (Priority: P2)

Each record belongs to one game connection and has a category. The category is a free value. The Radio quiz uses the value "radio". There is no default category. A lookup finds only records of the same game and category. This lets two games, or two quizzes in one game, use the same question text without a conflict.

**Why this priority**: Without a game and category, records of unrelated quizzes could mix.

**Independent Test**: Store the same question text in two categories with two different answers. Run a lookup for each category. Check that each lookup gets its own answer. Try to store a record without a category and check that the system rejects it.

**Acceptance Scenarios**:

1. **Given** a record in category "radio" for game A, **When** a lookup for game A and category "radio" runs, **Then** the lookup finds the record.
2. **Given** a record in category "radio" for game A, **When** a lookup for game B or for another category runs on the same question text, **Then** the lookup does not find the record.
3. **Given** a step without a category, **When** the sequence is saved or the step runs, **Then** the system rejects it with a clear message. No default category is used.
4. **Given** a store that holds 200 records, **When** a lookup runs, **Then** the lookup meets the time limit in SC-005.

---

### Edge Cases

- The OCR text of the same question differs a little between runs (case, spaces, punctuation, a wrong character). The normalization and the `similarity` setting let the lookup still find the record. A match below the similarity limit counts as not known.
- The store holds a record whose answer text does not match any answer of the current list (the game changed the answers). The lookup treats this as not known and makes a guess. It does not store anything.
- A `quiz-learn` step points to a lookup step that did not run, or that gave `unreadable` or `service_unavailable`. The step stores nothing and gives `skipped_no_match` or `service_unavailable`, and the log says why.
- A lookup step has one answer region only, or no answer region. The system rejects the step at save time (at least two answer regions).
- The same question and the same answer is learned twice. The second learn does not make a duplicate; it keeps one record.
- A later learn gives a different correct answer for a question that is already stored. The newer answer replaces the old one, and the log says so.
- Two sequences or two accounts write to the store at the same time. No record is lost and no file is damaged.
- A store file is damaged or cannot be read. The step gives `service_unavailable`, and other data of the service stays usable.

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: The system MUST offer a sequence step `quiz-lookup` with the inputs: a question region, a list of answer regions, a store name, a game connection, a category, and an optional similarity limit.
- **FR-002**: The `quiz-lookup` step MUST read the text of the question region and of each answer region with the same text-reading engine that `reschedule-self` (`ocrOffset`) and `POST /api/ocr/read` use. It MUST NOT change that engine or that endpoint.
- **FR-003**: The `quiz-lookup` step MUST normalize the texts, and MUST ask the named store for the correct answer text of the question for the given game and category.
- **FR-004**: The `quiz-lookup` step MUST set exactly one outcome that a later `commandOutcome` condition can read: `pick_1` to `pick_N` (a position in the answer region list, starting at 1), `unreadable`, or `service_unavailable`.
- **FR-005**: When the store knows the question, the step MUST set the outcome to the position of the answer whose text matches the stored answer text, and the run log MUST say `known`. When the store does not know the question, the step MUST set a random position and the run log MUST say `guess`.
- **FR-006**: The system MUST offer a sequence step `quiz-learn` with the inputs: a reference to a `quiz-lookup` step, and either a correct answer region or a correct answer position.
- **FR-007**: The `quiz-learn` step MUST store the pair of question text and correct answer text in the store of the referenced lookup, for the game and category of that lookup.
- **FR-008**: The `quiz-learn` step MUST store a record only when the correct answer text matches exactly one answer text that the lookup read. Otherwise it MUST store nothing and MUST set the outcome `skipped_no_match` (no match) or `skipped_ambiguous` (more than one match).
- **FR-009**: The `quiz-learn` step MUST set exactly one outcome: `stored`, `skipped_no_match`, `skipped_ambiguous`, or `service_unavailable`.
- **FR-010**: The store MUST keep the answer text, not the position, so that a reordered answer list still finds the answer.
- **FR-011**: A guess MUST NEVER enter the store as a correct answer. Only a `quiz-learn` step MAY create or change a record.
- **FR-012**: Each record MUST be linked to a game connection and MUST have a category. The category is a free value. The system MUST reject a question without a category. The system MUST NOT use a default category.
- **FR-013**: A lookup MUST find only records of the same game and the same category as the lookup, and MUST match the normalized question text, using the optional similarity limit.
- **FR-014**: The system MUST offer API operations for the operator to list the records, read one record, and delete one record. The list MUST allow a filter by store, by game, and by category. An unknown identifier MUST give a "not found" response.
- **FR-015**: The store MUST hold at least 200 records, and a lookup MUST meet the time limit in SC-005 at that size. SC-005 is the only place that states the limit.
- **FR-016**: The store MUST use the file storage pattern of the repository: JSON files under the data root, behind an interface in the domain layer. The system MUST NOT add a database engine or a new storage package.
- **FR-017**: The store MUST survive a service restart and MUST be safe for access from more than one run at the same time.
- **FR-018**: The system MUST validate both steps when a sequence is saved. A wrong shape (no category, fewer than two answer regions, a missing lookup reference, a lookup reference that does not point to a `quiz-lookup` step before the learn step, both or neither of region and position) MUST give a validation error (a client error, not a server error).
- **FR-019**: The two new steps MUST be available at every place where the system accepts or lists the primitive step types (validation, execution, step-through, API schema and documentation including an example step for each new step, and the web UI step type list and the per-type field form of the sequence editor). The web UI MUST show the new types with a basic field form for their inputs.
- **FR-020**: The run log MUST show for each step: the outcome, `known` or `guess` for a lookup, the normalized question text, and the picked position. The system MUST NOT log secrets.
- **FR-020a**: A test with log capture MUST prove FR-020 for each outcome of both steps. The same test MUST prove that the log has no secret (no credential, token, or device identifier).
- **FR-021**: The change MUST NOT add a Radio quiz sequence, images, or a queue template change. Those stay with the requester.
- **FR-022**: A test MUST prove that each type in the primitive step type list (`SequenceActionTypes.All`) has a matching schema and documentation entry, and is in every allow-list that accepts step types. This sync check MUST fail when a type is missing at one place.
- **FR-023**: The coverage targets of the constitution MUST apply to the new code. The final polish task MUST check the coverage of the new code against these targets.

### Key Entities

- **Quiz record**: One known question. It holds an identifier, a store name, a game connection, a category, the normalized question text, the correct answer text, and the times it was created and last changed.
- **Quiz store**: A named group of quiz records. A lookup step and a learn step name the store. All sequences and accounts that name the same store share the records.
- **Lookup result**: The data that one `quiz-lookup` run keeps for a later `quiz-learn` step: the question text, the answer texts in order, the game, the category, and the store.

## Assumptions

- The store name selects one store. The game connection and the category inside the step select the records inside that store.
- The default similarity limit is chosen in the plan. A step can override it with the `similarity` input.
- The correct answer region for `quiz-learn` is read with the same text-reading engine as the lookup.
- An API for adding a record by hand is not required. The operator can list, read, and delete. The runs add records.
- This feature adds no new page to the web UI. The web UI changes only in the sequence editor: the step type list and the field form for the two new types. A test budget (for example 50 ms in memory) may be stricter than SC-005 and is not a spec limit. The phone support level of the UI does not change.
- All store access uses the existing access rules of the service API.
- The step handlers register in dependency injection only after their logic exists. The validation work for the lookup rules, the learn rules, and the game and category rules touches the same files. The team does this work in sequence, not in parallel.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: For a question that is in the store, a lookup gives the position of the correct answer in 100% of runs, also when the answer list is in a different order.
- **SC-002**: For a question that is not in the store, a lookup gives a position in the range of the list and the log says `guess` in 100% of runs, and no record is created by a guess.
- **SC-003**: After one successful learn, the next lookup of the same question gives the correct position and the log says `known`.
- **SC-004**: A learn step stores a record in 0% of the cases where the correct text matches no answer or more than one answer.
- **SC-005**: With 200 records in the store, a lookup finishes in less than 1 second, not counting the time to read the screen text.
- **SC-006**: The operator can find, read, and delete a wrong record in less than 1 minute with the API only.
- **SC-007**: A step or request without a category is rejected in 100% of tests, and no record without a category exists in the store.
