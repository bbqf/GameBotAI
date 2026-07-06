# Feature Specification: If-Then-Else Conditions in Sequences

**Feature Branch**: `067-sequence-if-conditions`
**Created**: 2026-07-06
**Status**: Draft
**Input**: User description: "I need if-then-else conditions to be usable within sequences. The conditions themselves should be the same as in the definition of a while loop, both "then" and "else" parts should be optional. The contents of the "then" and "else" blocks should work exactly as the loop block contents. Visually definition of a conditional should be similar to loops as well. In fact, when defining a sequence, the part where currently only loops are listed should be renamed to "Loops and Conditions" and adding an "if" block should be put under the loops."

## Clarifications

### Session 2026-07-06

- Q: Where may if blocks appear, and what may their branches contain (given loop nesting is flat today)? → A: If blocks may appear at top level and inside loop bodies; branches accept the same content as loop bodies (commands, waits — no loops, no nested if blocks).
- Q: Is an if block with both branches empty/absent valid? → A: Yes — it saves and executes as a successful no-op, matching today's tolerance for empty loop bodies.
- Q: How should the optional else branch appear in the editor? → A: Added explicitly — a new if block shows only the then area plus an "Add else" affordance; the else area appears on demand and can be removed again.

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Author and Run an If Block with a Then Branch (Priority: P1)

As an automation author, I can add an "if (condition) then" block to a sequence so that a group of steps runs only when the condition holds at that point of execution, and is skipped otherwise.

**Why this priority**: This is the core value of the feature — conditional branching inside sequences. Without it nothing else in this feature matters. A then-only if block is already a viable MVP (it covers "do X only when Y is on screen" scenarios that today require awkward loop workarounds).

**Independent Test**: Create a sequence with an if block conditioned on an image being visible, with two steps in the then branch. Run it once with the image visible (branch steps execute) and once without (branch steps are skipped, execution continues after the block).

**Acceptance Scenarios**:

1. **Given** an if block whose condition is true when reached, **When** the sequence executes, **Then** the then-branch steps run in order and execution continues after the block.
2. **Given** an if block whose condition is false when reached, **When** the sequence executes, **Then** the then-branch steps are skipped entirely and execution continues after the block.
3. **Given** an if block, **When** the sequence executes, **Then** the condition is evaluated exactly once per encounter of the block (no re-evaluation mid-branch).
4. **Given** an if block whose condition evaluation errors (e.g. referenced image no longer exists), **When** the block is reached, **Then** the if step fails with the same failure behaviour as a while-loop condition error.
5. **Given** an if block nested inside a loop body, **When** the loop iterates, **Then** the condition is re-evaluated on each iteration when the block is reached.

---

### User Story 2 - Add an Optional Else Branch (Priority: P2)

As an automation author, I can add an "else" branch to an if block so that an alternative group of steps runs when the condition is false.

**Why this priority**: Else branches complete the if-then-else construct and cover "either/or" flows (e.g. "if error dialog visible, dismiss it; else proceed with the tap"). Valuable but the then-only form already delivers the MVP.

**Independent Test**: Create an if block with steps in both branches, run with condition true (only then-branch runs) and condition false (only else-branch runs).

**Acceptance Scenarios**:

1. **Given** an if block with then and else branches and a condition that is true, **When** the sequence executes, **Then** only the then-branch steps run.
2. **Given** an if block with then and else branches and a condition that is false, **When** the sequence executes, **Then** only the else-branch steps run.
3. **Given** an if block with only an else branch (no then steps) and a condition that is true, **When** the sequence executes, **Then** no branch steps run and execution continues after the block.
4. **Given** an if block where both branches are empty, **When** the sequence executes, **Then** the block completes successfully as a no-op and execution continues.

---

### User Story 3 - Author If Blocks Like Loop Blocks (Priority: P2)

As an automation author, I define and edit if blocks in the sequence editor the same way I define loop blocks: same condition editor as a while loop, same way of adding, reordering, and removing steps inside the branches, and a similar visual block presentation.

**Why this priority**: Consistency with the existing loop authoring experience is an explicit user requirement and keeps the learning curve flat, but it depends on the P1 construct existing.

**Independent Test**: In the sequence editor, add an if block, configure its condition using the same options available for a while loop, populate both branches with the same kinds of steps a loop body accepts (including nested loops and nested if blocks), save, reload, and confirm nothing is lost.

**Acceptance Scenarios**:

1. **Given** the sequence editor, **When** an author configures an if block's condition, **Then** the available condition types, options, and negation are identical to those offered for a while loop condition.
2. **Given** an if block branch, **When** an author adds content to it, **Then** the same step types allowed inside a loop body are allowed (commands, waits — not loops or other if blocks), with the same editing operations (add, remove, reorder, configure).
3. **Given** a sequence containing an if block with populated branches, **When** the author saves and reopens the sequence, **Then** the condition and both branches round-trip without loss.
4. **Given** an if block in the editor, **When** it is displayed, **Then** it is presented as a block visually consistent with loop blocks (header summarising the condition, distinct then/else branch areas).

---

### User Story 4 - Discover If Blocks Under "Loops and Conditions" (Priority: P3)

As an automation author, when adding steps to a sequence I find the if block in the same place I find loops: the add-step group currently labelled for loops is renamed to "Loops and Conditions", with the "If" option listed after the loop options.

**Why this priority**: Pure discoverability/labelling polish; the feature is usable without it but the user explicitly requested this placement.

**Independent Test**: Open the sequence editor's add-step area and verify the group label reads "Loops and Conditions" and contains the existing loop options followed by an "If" option that inserts a new if block.

**Acceptance Scenarios**:

1. **Given** the sequence editor's add-step area, **When** it is displayed, **Then** the group previously labelled for loops is labelled "Loops and Conditions".
2. **Given** the "Loops and Conditions" group, **When** the author activates the "If" option, **Then** a new if block with an empty then branch is appended to the sequence, positioned and behaving like a newly added loop block.
3. **Given** the "Loops and Conditions" group, **When** its options are listed, **Then** the "If" option appears after the existing loop options.

---

### Edge Cases

- Condition references an image that has been deleted → condition evaluation fails and the if step fails, consistent with while-loop condition error handling.
- Both branches are absent or empty → the block is valid, saves successfully, and executes as a no-op.
- If block is the last step of a sequence → execution completes normally after the branch (or skip).
- If block placed inside a loop body → allowed; its condition is re-evaluated each time the loop iteration reaches it.
- Loop or if block placed inside an if branch → rejected by validation, mirroring today's flat loop-nesting rule.
- Sequence is cancelled while a branch is executing → same cancellation behaviour as cancelling inside a loop body.
- A step inside a branch fails → the failure propagates exactly as it would from the same step inside a loop body.
- Execution logs must attribute branch steps to the if block so the run history remains readable (mirroring how loop iterations are represented).

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: The system MUST support an "if" block step type within sequences, containing a condition, an optional then branch, and an optional else branch, each branch being an ordered list of steps.
- **FR-002**: The if condition MUST support exactly the same condition types, parameters, and negation as a sequence while-loop condition; any condition valid for a while loop is valid for an if block and vice versa.
- **FR-003**: When an if block is reached during execution, the system MUST evaluate the condition exactly once, execute the then branch when true, execute the else branch when false, and skip absent or empty branches.
- **FR-004**: Branch contents MUST behave exactly like loop body contents: the same step types are permitted (commands, waits — loops and other if blocks are not permitted inside branches, mirroring the flat loop-body rule), and steps inside branches execute, report status, fail, and cancel with the same semantics as steps inside a loop body.
- **FR-004a**: If blocks MUST be permitted at the top level of a sequence and inside loop bodies; validation MUST reject loop steps or if steps placed inside an if branch.
- **FR-005**: An if block whose relevant branch is absent or empty MUST complete successfully as a no-op and execution MUST continue with the next step.
- **FR-006**: A condition evaluation error MUST fail the if step with the same failure semantics as a while-loop condition evaluation error.
- **FR-007**: The sequence editor MUST let authors create, configure, reorder, and delete if blocks with the same editing operations available for loop blocks, and MUST present if blocks visually consistently with loop blocks, including a header summarising the condition and clearly separated then/else branch areas.
- **FR-007a**: A newly added if block MUST show only the then branch area; the else branch area MUST be added via an explicit affordance (e.g. "Add else") and MUST be removable again, deleting any steps it contains only after the removal is confirmed or is trivially safe (empty branch removed without confirmation).
- **FR-008**: The sequence editor's condition editing controls for if blocks MUST be the same as those used for while-loop conditions.
- **FR-009**: The add-step group in the sequence editor that currently lists only loops MUST be renamed to "Loops and Conditions", and an "If" option MUST be listed in that group after the loop options.
- **FR-010**: Sequences containing if blocks MUST save, load, validate, and round-trip without data loss, and existing sequences without if blocks MUST remain fully functional and unchanged.
- **FR-011**: Execution history/logs MUST represent an if block and its executed branch steps hierarchically, consistent with how loop blocks and their iterations are represented, including which branch was taken (or that the block was a no-op).

### Key Entities

- **If Block Step**: A sequence step containing a condition, an optional then branch (ordered list of steps), and an optional else branch (ordered list of steps). May sit at the top level of a sequence or inside a loop body; may not sit inside another if branch.
- **Condition**: The same condition definition used by sequence while loops (e.g. image visibility with optional similarity threshold and negation, or a prior command's outcome), unchanged by this feature.
- **Branch**: An ordered list of sequence steps identical in capability to a loop body.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: Authors can add an if block with a condition and at least one populated branch, save, and reload the sequence without data loss, in no more interactions than creating an equivalent while loop.
- **SC-002**: In 100% of test runs, only the branch matching the condition outcome executes: then on true, else on false, and no branch steps when the matching branch is absent or empty.
- **SC-003**: Every condition configuration accepted for a while loop is accepted for an if block, and every configuration rejected for a while loop is rejected for an if block (100% parity across the supported condition set).
- **SC-004**: All sequences authored before this feature continue to load, edit, and execute with unchanged behaviour.
- **SC-005**: For any executed if block, a reader of the execution history can determine which branch was taken (or that no branch ran) without consulting the sequence definition.

## Assumptions

- The condition set for if blocks is exactly the current sequence while-loop condition set (image visibility with optional negation and similarity threshold, and command outcome); this feature adds no new condition types.
- Both branches being absent/empty is valid; such a block is a saveable no-op rather than a validation error, mirroring the tolerance for empty loop bodies. (Confirmed 2026-07-06.)
- The condition is evaluated once per encounter of the if block; there is no re-evaluation while a branch is executing (unlike a while loop, which re-evaluates per iteration).
- If blocks may appear at the sequence top level and inside loop bodies; branches follow the flat-nesting rule loop bodies follow today (no loops, no nested if blocks). (Clarified 2026-07-06.)
- A newly added if block starts with the same default condition a newly added while loop starts with, and an empty then branch; the else branch is added explicitly by the author via an "Add else" affordance. (Confirmed 2026-07-06.)
- Scheduling, queueing, and other sequence-level behaviours are unaffected; the if block only changes which steps run within a single sequence execution.
