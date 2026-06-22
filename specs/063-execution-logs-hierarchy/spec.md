# Feature Specification: Execution Logs Reflect What Was Actually Executed

**Feature Branch**: `049-execution-logs-hierarchy` (folder renumbered 049→063 to resolve a duplicate-049 collision; the merged branch kept its original name)  
**Created**: 2026-06-01  
**Status**: Implemented (iterated by 050)
**Input**: User description: "Let's change the logic of the execution logs a bit. I want to have a clear view of what's been executed. At the moment, if I execute sequence, then I see in the real time the execution logs of the individual commands from it, but nothing else and at the end of sequence execution I see the whole picture: with all the sequence of steps, primitive actions, etc. In this particular case, I want to see only the sequence with the subelements under it. Ideally it should work in the real time (with updates of the steps as they progress), but it's ok to see the proper results once the sequence ends. You may need to refactor the UI and API for that, that's OK, just make sure the execution logs contain the things what actually have been executed: stand-alone commands or sequences with all the information provided as today."

## Clarifications

### Session 2026-06-01

- Q: How should child commands invoked by a sequence be stored, given the goal of showing only the sequence at the top level? → A: Keep recording each child execution but link it to a parent/root execution; show only root entries in the top-level list (child records are filtered out of the list, not deleted).
- Q: Where should the sequence's nested sub-elements be displayed? → A: As expandable/collapsible tree rows inline in the execution logs list (each top-level row can be expanded to reveal nested sub-elements).
- Q: Should live, per-step updates during a running sequence be built now, or deferred? → A: Include live updates now — sub-elements update as steps progress without requiring a manual reload.

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Executed sequence appears as a single entry with its sub-elements nested (Priority: P1)

A user runs a sequence. They open the execution logs and see **one** entry representing that sequence run. Expanding it reveals the ordered sub-elements (steps, the commands those steps invoked, primitive actions, condition evaluations, loop iterations, wait-for-image outcomes) — exactly the information available today — grouped beneath the sequence. The individual commands that the sequence invoked do **not** appear as their own separate top-level entries in the list.

**Why this priority**: This is the core of the request. Today a single sequence run pollutes the log list with one row per invoked command plus a separate sequence row, making it impossible to tell at a glance "what did I actually run." Fixing the top-level grouping delivers the primary value on its own.

**Independent Test**: Execute a sequence that invokes multiple commands, then open the execution logs. Verify the list contains a single new top-level row (the sequence) and that all per-command/per-step detail is reachable underneath it, with no separate top-level rows for the invoked commands.

**Acceptance Scenarios**:

1. **Given** a sequence that invokes three commands, **When** the user executes the sequence and opens the execution logs, **Then** exactly one new top-level entry (the sequence) appears in the list.
2. **Given** the completed sequence entry, **When** the user views/expands its details, **Then** all sub-elements (steps, invoked command names, primitive actions, conditions, loops, wait outcomes, applied delays) are shown nested under the sequence with the same depth of information available today.
3. **Given** a sequence run, **When** the user scans the top-level list, **Then** none of the commands invoked as part of that sequence appear as standalone top-level entries.

---

### User Story 2 - Stand-alone command execution still appears as its own entry (Priority: P1)

A user executes a single command directly (not as part of a sequence). The execution logs show that command as its own top-level entry, with all of its details (primitive actions, taps, wait-for-image outcomes, applied delays) exactly as today.

**Why this priority**: The request explicitly says the log should contain "stand-alone commands or sequences." Directly-executed commands are first-class top-level items and must not be lost or hidden by the grouping change.

**Independent Test**: Execute a command directly from the Execution page, open the execution logs, and verify a single top-level entry for that command with full details.

**Acceptance Scenarios**:

1. **Given** a command executed directly (not via a sequence), **When** the user opens the execution logs, **Then** the command appears as its own top-level entry.
2. **Given** a directly-executed command entry, **When** the user views its details, **Then** all step/primitive/wait/delay information available today is present.

---

### User Story 3 - Progress updates live during sequence execution (Priority: P2)

While a sequence is running, the user sees a single in-progress top-level entry for that run, and its nested sub-elements update live as steps progress — without requiring a manual page reload. At no point does the list show orphaned standalone command rows for that run. Once the sequence finishes, the entry settles into its final, complete grouped state.

**Why this priority**: Grouping correctness (P1) is the foundation and must land first; live updates build on top of that same grouped entry. Live progress is in scope for this feature but depends on P1 being correct.

**Independent Test**: Start a longer-running sequence and observe the execution logs. Confirm a single in-progress sequence entry is shown, its sub-elements update as steps complete without a manual reload, and no standalone child-command rows ever appear.

**Acceptance Scenarios**:

1. **Given** a sequence is executing, **When** the user views the execution logs before it finishes, **Then** they see a single in-progress top-level entry for that run and no standalone top-level rows for the sequence's invoked commands.
2. **Given** a step within a running sequence completes, **When** the user is viewing the execution logs, **Then** the corresponding sub-element under the sequence entry reflects its outcome live, without requiring a manual page reload.
3. **Given** a sequence finishes executing, **When** the user views the execution logs, **Then** the same top-level entry is shown in its final state with all nested sub-elements and the correct aggregate status.

---

### Edge Cases

- **Nested sequences**: When a sequence invokes another sequence, the nested sequence and its sub-elements appear grouped beneath the parent sequence, and the nested sequence does not appear as a separate top-level entry for that run.
- **Failed / partial runs**: A sequence that fails partway still appears as a single top-level entry whose status reflects the failure, with sub-elements showing which steps succeeded, failed, were skipped, or never ran.
- **Loops**: A loop step shows its per-iteration outcomes nested under the sequence as it does today, not as separate top-level entries.
- **Command reused across runs**: The same command invoked by a sequence today and executed stand-alone tomorrow yields two distinct, correctly-attributed entries (one nested, one top-level).
- **Empty / no-op steps**: Skipped steps or steps whose condition evaluated false are still represented as sub-elements (with skipped/not-executed status), not omitted.
- **Existing/historical logs**: Logs recorded before this change are still viewable; they are not required to be retroactively re-grouped, but they must not cause errors in the list.
- **Concurrent executions**: If two sequences (or a sequence and a stand-alone command) run close together, each maps to its own correctly-grouped top-level entry without sub-elements leaking between them.

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: The execution logs list MUST show only top-level executed entities — a stand-alone command (one executed directly) or a sequence — as its rows.
- **FR-002**: A command that is invoked as part of a sequence run MUST NOT appear as a separate top-level row for that run; it MUST be represented as a sub-element of that sequence's entry.
- **FR-002a**: The system MUST continue to record each child execution as its own underlying record, linked to its parent and root execution; child records MUST be filtered out of the top-level list rather than deleted, so that the parent/child/root relationship is preserved for nesting and possible future drill-down.
- **FR-003**: A sequence entry MUST expose all of its executed sub-elements (ordered steps, the commands those steps invoked, primitive actions/taps, condition evaluations, loop iterations and their per-iteration outcomes, wait-for-image outcomes, and applied delays) with at least the same depth of information available today.
- **FR-003a**: Top-level rows in the execution logs list MUST be expandable/collapsible, revealing their nested sub-elements inline as tree rows; sub-elements that are themselves containers (e.g., a nested sequence or a loop) MUST also be expandable to show their own children.
- **FR-004**: A stand-alone command entry MUST continue to display all of its details (primitive actions, taps, wait-for-image outcomes, applied delays, reasons) exactly as today.
- **FR-005**: Each top-level entry MUST display its overall final status, and that status MUST correctly reflect the aggregate outcome of its sub-elements (e.g., a sequence with a failed step is not reported as fully successful).
- **FR-006**: The system MUST correctly attribute every sub-element to its owning top-level execution so that details from different runs never intermingle.
- **FR-007**: During sequence execution, the execution logs MUST NOT present orphaned standalone command rows for commands invoked by that sequence; the running sequence MUST be represented as a single in-progress top-level entry.
- **FR-008**: The execution logs MUST update live during a running sequence so that nested sub-elements reflect their outcomes as steps progress, without requiring a manual page reload; the same top-level entry MUST settle into its final, complete grouped state once execution ends.
- **FR-009**: Nested sequences (a sequence invoking another sequence) MUST be represented hierarchically under the parent sequence rather than as separate top-level entries for that run.
- **FR-010**: Existing filtering and sorting of the execution logs list MUST continue to operate over the top-level entries (e.g., by timestamp, object name, status).
- **FR-011**: Deep links / navigation from a sub-element to its underlying authored object (e.g., the sequence step or command) MUST continue to work where that information is available today.
- **FR-012**: Historical execution logs recorded before this change MUST remain viewable without errors.

### Key Entities *(include if data involved)*

> Terminology: a **sub-element** in this spec corresponds to an **execution tree node** (`ExecutionTreeNode`) in the plan/data-model/contracts; the two terms are interchangeable.

- **Execution Entry (top-level)**: Represents one user-initiated execution that actually ran — either a stand-alone command or a sequence. Attributes: timestamp, executed object reference (name/type), overall final status (including an in-progress state while running), summary, and an ordered collection of sub-elements. This is the only level shown as top-level rows in the list.
- **Sub-element (nested)**: Represents something that happened within a top-level execution — a sequence step, an invoked command, a primitive action/tap, a condition evaluation, a loop and its iterations, or a wait-for-image outcome. Attributes mirror what is captured today (status, reason, applied delay, command name, condition trace, wait details, deep link). Each invoked-command sub-element continues to exist as its own underlying record, linked to its parent and root execution and filtered out of the top-level list (not deleted).
- **Execution hierarchy/relationship**: The parent/child/root association that ties sub-elements to their owning top-level execution, distinguishes root (top-level) from child records for list filtering, and supports nested sequences.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: After executing any single sequence, the execution logs list shows exactly one new top-level row for that run (regardless of how many commands the sequence invoked).
- **SC-002**: 100% of the information available today for invoked commands and steps remains reachable from the sequence entry (no detail is lost relative to the current behavior).
- **SC-003**: After executing a stand-alone command, the execution logs list shows exactly one new top-level row for that command with full details.
- **SC-004**: A user can determine "what did I actually run" from the top-level list alone, without needing to mentally filter out child-command noise — verified by zero standalone rows for sequence-invoked commands.
- **SC-005**: While a sequence runs, its in-progress entry's sub-elements reflect each completed step live (within ~2 seconds of the step completing) without a manual page reload, and the entry shows its final complete state within ~2 seconds of the sequence finishing.
- **SC-006**: Sorting and filtering the execution logs returns correct results over top-level entries only, with no child-command rows leaking into results.
- **SC-007**: A user can expand any top-level entry to reveal its nested sub-elements, and 100% of the detail captured today is reachable through that expansion.

## Assumptions

- "Stand-alone command" means a command executed directly by the user (e.g., from the Execution page or queue), not one invoked as a step inside a sequence.
- The level of per-element detail currently captured (steps, primitive actions, taps, conditions, loop iterations, wait-for-image outcomes, applied delays, deep links) is the target for what must be preserved under the nested view; this feature does not require adding new kinds of detail beyond what exists today.
- Real-time, per-step live updates during a running sequence are in scope for this feature (see Clarifications): the running sequence is shown as a single in-progress top-level entry whose sub-elements update live, and orphaned child rows never appear.
- Historical logs created before this change are read-only artifacts; they do not need to be migrated/re-grouped, only to remain viewable.
- It is acceptable to refactor the UI and the execution-logs API/data shape to deliver this grouping, as the user explicitly permitted.
