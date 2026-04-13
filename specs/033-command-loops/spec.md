# Feature Specification: Command Loop Structures

**Feature Branch**: `033-command-loops`  
**Created**: 2026-03-31  
**Status**: Draft  
**Input**: User description: "I need the loop structures within commands. It should be standard iterate X times, whereas X should be somehow made available within the loop body and while(condition) do something as well as repeat until (condition). It should be possible to break out of the loops on a certain step (likely with condition). The loops should be visualized in the UI in a clear way, e.g. indented from the rest of the step or put in a rectangle with background color. Inside the loops there must be the same steps as in all other commands."

## Clarifications

### Session 2026-03-31

- Q: Should loop steps be valid inside loop bodies (loop nesting)? → A: No. Loop nesting is out of scope for v1; loop step types are not permitted inside loop bodies.
- Q: What happens when a loop condition evaluation errors at runtime? → A: Condition eval error fails the loop step immediately and stops the command.
- Q: How is the iteration index consumed in step parameters? → A: Via string template placeholder (e.g. `{{iteration}}`) substituted into step parameter values at execution time.
- Q: Should `commandOutcome` be valid as a loop condition (while/repeat-until/break)? → A: Yes, `commandOutcome` is valid as a loop condition using the same scoping rules as feature 032.
- Q: What is the default safety iteration limit and is breaching it a hard failure or warning? → A: Hard failure (command stops, marked failed); default limit = 1000 iterations.

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Author a Count-Based Loop (Priority: P1)

As an automation author, I can add a "repeat N times" loop block to a command so that its inner steps execute exactly N times, and I can reference the current iteration index within the loop body.

**Why this priority**: The count-based loop is the most common automation pattern (e.g. "tap the upgrade button 10 times"). It is self-contained and requires no external condition evaluation, making it the simplest loop to implement and verify independently.

**Independent Test**: Can be fully tested by creating a command containing a single count-based loop with N=3 containing one tap action, executing it, and verifying the tap was performed exactly 3 times with correct iteration index values logged per iteration.

**Acceptance Scenarios**:

1. **Given** a command with a count-based loop configured for 5 iterations containing one step, **When** the command is executed, **Then** the inner step runs exactly 5 times.
2. **Given** a count-based loop with N=3, **When** the loop body references the iteration index variable, **Then** the index takes values 1, 2, 3 (or 0, 1, 2 — see Assumptions) across successive iterations.
3. **Given** a count-based loop with N=3 and a step parameter containing `{{iteration}}`, **When** the command executes, **Then** the placeholder is substituted with the current iteration index value (1, 2, 3) for each inner step execution.
3. **Given** a count-based loop with N=0, **When** the command is executed, **Then** the loop body is skipped and execution continues after the loop.
4. **Given** a command with a count-based loop saved and reloaded, **When** the author opens the command editor, **Then** the loop block and its inner steps are displayed intact.

---

### User Story 2 - Author a While-Condition Loop (Priority: P2)

As an automation author, I can add a "while (condition) do" loop block so that its inner steps repeat as long as the condition remains true, evaluated before each iteration.

**Why this priority**: While loops are essential for waiting and polling scenarios (e.g. "while loading screen is visible, wait and retry"). Depends on P1 loop infrastructure already in place.

**Independent Test**: Can be tested by creating a command with a while loop conditioned on an image being visible, running it with image initially visible then absent after two iterations, and confirming the loop ran exactly twice.

**Acceptance Scenarios**:

1. **Given** a while loop where the condition is true initially, **When** the command executes, **Then** the loop body runs at least once.
2. **Given** a while loop where the condition becomes false after two iterations, **When** the command executes, **Then** the loop body runs exactly twice and execution continues after the loop.
3. **Given** a while loop where the condition is false on entry, **When** the command executes, **Then** the loop body is skipped entirely and execution continues after the loop.
4. **Given** a while loop with no maximum iteration guard, **When** the condition never becomes false, **Then** the loop runs indefinitely until the command is cancelled or a configured safety limit is reached.
5. **Given** a while loop whose condition evaluation errors (e.g. referenced image missing), **When** the error occurs on any iteration, **Then** the loop step fails and the command stops immediately.

---

### User Story 3 - Author a Repeat-Until Loop (Priority: P2)

As an automation author, I can add a "repeat … until (condition)" loop block so that its inner steps execute at least once and repeat until the condition becomes true, evaluated after each iteration.

**Why this priority**: Repeat-until covers "do action, then check result" patterns (e.g. "tap, then wait until success screen appears"). At the same parity as while loops.

**Independent Test**: Can be tested by creating a command with a repeat-until loop that runs its body once when the exit condition is immediately true, confirming exactly one execution of the inner steps.

**Acceptance Scenarios**:

1. **Given** a repeat-until loop where the condition is true after the first iteration, **When** the command executes, **Then** the loop body runs exactly once.
2. **Given** a repeat-until loop where the condition becomes true after three iterations, **When** the command executes, **Then** the loop body runs exactly three times.
3. **Given** a repeat-until loop whose condition never becomes true, **When** the command executes and a safety iteration limit is reached, **Then** the loop terminates with a failure outcome and the command is marked failed.

---

### User Story 4 - Break Out of a Loop Conditionally (Priority: P2)

As an automation author, I can place a break step inside a loop body so that execution exits the loop immediately when a specified condition is met.

**Why this priority**: Break prevents unintended infinite looping and models early-exit patterns (e.g. "stop repeating if error screen appears"). Required for safe while/repeat loops.

**Independent Test**: Can be tested by adding a break step (conditioned on image X visible) inside a count-based loop with N=10; confirming the loop exits after the iteration where image X first becomes visible, with fewer than 10 total iterations logged.

**Acceptance Scenarios**:

1. **Given** a count-based loop N=5 containing a break step conditioned on image visible, **When** image becomes visible on iteration 3, **Then** the loop exits after iteration 3 and the command continues after the loop.
2. **Given** a break step whose condition is never met, **When** the loop completes all iterations normally, **Then** the break step is evaluated but never triggers, and the loop runs to completion.
3. **Given** a break step with no condition (unconditional break), **When** the step is reached, **Then** the loop exits immediately on that iteration.

---

### User Story 5 - Visualize Loop Structures in the Authoring UI (Priority: P1)

As an automation author, I can see loop blocks visually distinguished in the command editor — indented, boxed, or background-colored — so that I can clearly understand nesting and structure at a glance.

**Why this priority**: Without clear visual grouping, loop bodies are indistinguishable from surrounding steps, making authoring error-prone. This is a prerequisite for confident editing of any loop type.

**Independent Test**: Can be tested by opening a command containing loops of each type and confirming that loop bodies appear visually contained (e.g., indented or inside a colored rectangle) and separate from non-loop steps.

**Acceptance Scenarios**:

1. **Given** a command with a count-based loop, **When** the author views the command editor, **Then** the loop header and its body steps are visually grouped and distinguished from non-loop steps.
2. **Given** a command with nested-by-proximity loops (a loop followed by another loop), **When** the author views the editor, **Then** each loop's body is independently bounded with no visual overlap.
3. **Given** a loop body containing a break step, **When** the author views the editor, **Then** the break step is visually inside the loop boundary.
4. **Given** a loop body with multiple inner steps, **When** the author adds a new step inside the loop, **Then** the new step appears within the visual loop boundary.

---

### Edge Cases

- Count N specified as a negative number at save time.
- Iteration index variable referenced outside a loop body.
- Loop body contains zero steps.
- While/repeat-until condition references an image that no longer exists.
- While loop safety iteration limit is configured to zero or a negative value.
- Deeply complex commands (loop containing steps that reference outputs of prior steps).
- Break step placed as the first step in a loop body (loop body executes once then immediately breaks).
- Loop type changed from count-based to while after inner steps have been authored.
- Condition evaluation error inside a while or repeat-until loop body.
- Author attempts to add a loop step inside an existing loop body (must be rejected at save time).
- `commandOutcome` loop condition references a step inside the loop body of the current iteration (forward reference — must be rejected at save time).
- `commandOutcome` loop condition references a step from an outer scope when loop is at top level.

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: System MUST support a count-based loop step type that executes its body steps exactly N times, where N is a positive integer.
- **FR-002**: System MUST make the current iteration index available within the loop body via the string template placeholder `{{iteration}}`, substituted into step parameter values at execution time; the value is the 1-based iteration number.
- **FR-002a**: System MUST validate at save time that `{{iteration}}` placeholders only appear inside a loop body and MUST reject save requests where they appear outside any loop.
- **FR-003**: System MUST support N=0 for count-based loops, resulting in the loop body being skipped entirely.
- **FR-004**: System MUST reject count-based loop definitions where N is negative at save/validation time.
- **FR-005**: System MUST support a while loop step type that evaluates its condition before each iteration and executes the body only while the condition is true.
- **FR-006**: System MUST support `imageVisible` and `commandOutcome` as valid condition types in loop conditions (while entry, repeat-until exit, and break), applying the same scoping rules as feature 032: `commandOutcome` may only reference steps that have already executed before the current evaluation point (i.e. steps earlier in the command, or steps in prior iterations of the loop body).
- **FR-007**: System MUST skip the while loop body entirely when the condition is false on first evaluation.
- **FR-008**: System MUST apply a configurable safety iteration limit to while loops and repeat-until loops; when the limit is reached the loop MUST terminate with a hard failure outcome and the command MUST be marked failed. The default limit when not explicitly configured is 1000 iterations.
- **FR-008a**: System MUST fail the loop step and stop command execution immediately when a loop condition cannot be evaluated (evaluation error), consistent with per-step condition error behavior.
- **FR-009**: System MUST support a repeat-until loop step type that executes its body steps at least once and re-evaluates the exit condition after each iteration, stopping when the condition is true.
- **FR-010**: System MUST support a break step type that, when reached inside a loop body, immediately exits the enclosing loop and continues execution after the loop.
- **FR-011**: System MUST support a conditional break where the break triggers only when a specified condition evaluates true, and is otherwise a no-op for that iteration.
- **FR-012**: System MUST allow placing any step type valid in commands (action, conditional, break) inside a loop body, EXCEPT loop step types; loop steps MUST NOT be valid inside a loop body in v1.
- **FR-013**: System MUST persist and reload all three loop types together with their complete inner step lists without loss of data or ordering.
- **FR-014**: System MUST validate that loop body steps are well-formed under the same validation rules as top-level steps.
- **FR-015**: System MUST expose per-iteration execution outcomes in execution logs, including iteration index, inner step results, and break events.
- **FR-016**: System MUST expose a loop-level outcome in execution logs: total iterations executed, break triggered, or limit reached.
- **FR-017**: Authoring UI MUST visually distinguish loop blocks from surrounding steps through indentation, a bounding box, or background color differentiation.
- **FR-018**: Authoring UI MUST display the loop type (count/while/repeat-until) and its key parameter (count or condition summary) in the loop header.
- **FR-019**: Authoring UI MUST allow adding, reordering, and removing steps inside a loop body using the same interactions as for top-level steps.
- **FR-020**: Authoring UI MUST display the iteration index variable name inside the loop header so authors know how to reference it.
- **FR-021**: System MUST validate that break steps only appear inside loop bodies and MUST reject save requests where a break step appears outside any loop.

### Key Entities

- **Loop Step**: A command step that contains an ordered list of inner steps and a loop control definition (count, while-condition, or repeat-until-condition). Has a `loopType` discriminator and a `body` list.
- **Break Step**: A command step that conditionally or unconditionally exits the nearest enclosing loop.
- **Iteration Variable**: A named binding (e.g. `iteration`) available within a loop body, resolving to the current 1-based iteration index.
- **Safety Iteration Limit**: A configured ceiling on the number of iterations for while and repeat-until loops to prevent unbounded execution.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: Authors can create a count-based loop, a while loop, and a repeat-until loop, each with inner steps, and save/reload them without data loss in under 5 interactions per loop.
- **SC-002**: A count-based loop configured for N iterations executes its body exactly N times in 100% of test runs.
- **SC-003**: A while loop exits immediately (body skipped) when the entry condition is false in 100% of test runs.
- **SC-004**: A repeat-until loop executes its body at least once in 100% of test runs regardless of exit condition state.
- **SC-005**: A break step exits the loop on the iteration where its condition first becomes true in 100% of test runs.
- **SC-006**: While and repeat-until loops terminate within the configured safety iteration limit and report a failure outcome; no unbounded loop execution occurs.
- **SC-007**: Loop blocks are visually distinct from non-loop steps such that a first-time user can identify the loop body without instructions.
- **SC-008**: Execution logs include per-iteration entries for each loop run, enabling complete audit of loop behavior.

## Assumptions

- Iteration index is 1-based (first iteration = 1). It is referenced in step parameter values using the `{{iteration}}` template placeholder, which the execution engine substitutes before invoking each inner step.
- The condition types available inside loop conditions (while/repeat-until/break) are the same set supported by per-step conditions (feature 032), starting with `imageVisible`.
- A configurable safety iteration limit applies to while and repeat-until loops. The default limit is **1000 iterations**. Reaching the limit is a hard failure: the loop step fails and the command stops. A global override is supported via system configuration (`LoopMaxIterations`). **Per-loop UI override of the safety limit is out of scope for v1**: `LoopConfig.MaxIterations` exists in the domain model and is validated, but is not exposed in the authoring UI; it may be set via direct JSON editing only.
- Loop nesting (a loop body containing another loop) is **explicitly out of scope for v1**. Loop step types are not valid inside loop bodies; this constraint is enforced at save/validation time.
- The iteration index variable is consumed via `{{iteration}}` string template substitution in step parameter values. This mechanism is scoped to loop bodies only and is validated at save time.
- "Steps as in all other commands" means action steps, conditional steps (032), and break steps (this feature) are all valid inside loop bodies.
- Co-operative `CancellationToken` cancellation of a running loop is **out of scope for v1**. The configured safety iteration limit is the sole bound on unbounded loop execution; external cancellation of the host service terminates the process, not the loop gracefully.
