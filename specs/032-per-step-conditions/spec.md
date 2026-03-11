# Feature Specification: Per-Step Optional Conditions

**Feature Branch**: `[032-per-step-conditions]`  
**Created**: 2026-03-06  
**Status**: Draft  
**Input**: User description: "Implement per-step optional conditions in sequence authoring and execution UI/API, replacing entry-step true/false branch flow"

## Clarifications

### Session 2026-03-06

- Q: How should pre-existing branch-mode sequences be handled? → A: Legacy sequences are out of scope; assume only new per-step sequences exist.
- Q: Which condition types are in v1 scope? → A: Support imageVisible and commandOutcome in v1.
- Q: What can commandOutcome reference? → A: Only earlier steps in the same sequence.
- Q: Which commandOutcome expected states are in v1 scope? → A: success, failed, skipped.

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Add Optional Condition Per Step (Priority: P1)

As an author, I can define a linear sequence where each step may have its own condition, so I can model "normalize to home screen" behavior without graph branching.

**Why this priority**: This is the core usability requirement and directly solves the current authoring confusion with entry-step branching.

**Independent Test**: Can be fully tested by creating a sequence where some steps have conditions and others do not, saving it, reloading it, and confirming each step retains its own condition settings.

**Acceptance Scenarios**:

1. **Given** a new sequence with three steps, **When** the author assigns a condition to step 1 and step 2 and leaves step 3 unconditional, **Then** each step displays and saves its own independent condition state.
2. **Given** an existing sequence, **When** the author toggles a step condition off, **Then** that step becomes unconditional without affecting other steps.
3. **Given** an empty workspace, **When** the author creates the first sequence with mixed conditional and unconditional steps, **Then** save succeeds and the sequence can be edited again with the same per-step condition definitions.

---

### User Story 2 - Execute Linear Step Conditions (Priority: P1)

As an operator, I want each step condition evaluated immediately before that step executes, so the sequence follows deterministic "if this screen is visible, do this step" behavior.

**Why this priority**: Runtime behavior is the value-delivery path; authoring alone is not useful unless execution semantics are predictable.

**Independent Test**: Can be tested by executing one sequence across visibility permutations plus `commandOutcome` permutations, then verifying ordered per-step outcomes (`executed`, `skipped`, `failed`) and fail-stop behavior.

**Acceptance Scenarios**:

1. **Given** step 1 conditioned on "Map visible", **When** map is visible at runtime, **Then** step 1 executes.
2. **Given** step 2 conditioned on "Bag visible", **When** bag is not visible at runtime, **Then** step 2 is skipped and execution proceeds to the next step.
3. **Given** a final unconditional step "Open Event Menu", **When** prior conditioned steps execute or skip, **Then** the final unconditional step always runs unless the sequence has already failed.
4. **Given** condition evaluation fails for a step, **When** execution reaches that step, **Then** the sequence fails fast with a clear step-level error outcome.
5. **Given** a step with `commandOutcome(stepRef=go-home, expectedState=skipped)`, **When** `go-home` is skipped, **Then** the conditioned step executes.
6. **Given** a step with `commandOutcome(stepRef=go-home, expectedState=success)`, **When** `go-home` is skipped, **Then** the conditioned step is skipped.

---

### User Story 3 - Remove Entry-Step Branching Model (Priority: P2)

As an author, I no longer need to manage entry-step and true/false branch wiring, so sequence configuration is simpler and aligned with step-by-step thinking.

**Why this priority**: Simplification reduces authoring errors and lowers onboarding time for non-technical users.

**Independent Test**: Can be tested by confirming the sequence editor no longer exposes entry-step, branch target wiring, or graph-only condition controls, while per-step condition controls remain available.

**Acceptance Scenarios**:

1. **Given** the sequence editor is open, **When** the author configures conditions, **Then** they only interact with step-level condition controls.
2. **Given** a newly created per-step sequence, **When** it is loaded after save, **Then** the editor shows the same step-level condition configuration with no branch wiring concepts.

---

### Edge Cases

- Step has condition enabled but missing required condition fields.
- Sequence contains only unconditional steps.
- Sequence contains conditions on all steps.
- Runtime screen state changes between consecutive conditioned steps.
- Condition target artifact is missing or deleted after authoring.
- commandOutcome condition references the current step or a later step.
- commandOutcome condition uses an unsupported expected state value.
- Existing branch-mode sequence handling is out of scope for this feature.

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: System MUST allow each sequence step to independently define either no condition or exactly one condition.
- **FR-002**: System MUST evaluate a step's condition immediately before that step would execute.
- **FR-003**: System MUST execute a step when its condition evaluates true, and skip a step when its condition evaluates false.
- **FR-004**: System MUST continue to the next step after a skipped step; execution MUST stop only when condition evaluation errors, action execution fails, or sequence cancellation is requested.
- **FR-005**: System MUST fail the sequence when a step condition cannot be evaluated.
- **FR-006**: System MUST support a mixed sequence of conditional and unconditional steps in one ordered list.
- **FR-007**: System MUST provide authoring controls to add, edit, enable, disable, and remove a condition per step.
- **FR-008**: System MUST remove entry-step and true/false branch configuration from primary sequence authoring.
- **FR-009**: System MUST validate condition configuration at save time and return clear step-specific errors.
- **FR-010**: System MUST persist and reload per-step condition definitions without loss or reordering.
- **FR-011**: System MUST expose execution outcomes for each step including whether the step was executed, skipped, or failed.
- **FR-012**: System MUST operate under a clean-slate assumption where only per-step conditional sequences are in scope.
- **FR-013**: System MUST support `imageVisible` and `commandOutcome` as valid per-step condition types in v1.
- **FR-014**: System MUST allow `commandOutcome` conditions to reference only prior steps in the same sequence order.
- **FR-015**: System MUST reject save requests where a `commandOutcome` condition references the current step or a later step.
- **FR-016**: System MUST support `success`, `failed`, and `skipped` as valid expected states for `commandOutcome` conditions.
- **FR-017**: System MUST reject save requests where `commandOutcome` expected state is outside `success|failed|skipped`.

### Key Entities *(include if feature involves data)*

- **Sequence**: Ordered automation definition containing metadata and a list of steps.
- **Sequence Step**: One executable sequence unit containing action reference plus optional condition metadata.
- **Step Condition**: Step-level evaluation rule supporting `imageVisible` and `commandOutcome` in v1.
- **Step Execution Outcome**: Runtime record for one step indicating condition evaluation result and final action outcome.

### Assumptions

- Each step has at most one condition in this feature scope.
- Condition false means skip (not failure); evaluation errors mean failure.
- Legacy branch-mode sequence data is out of scope and excluded from this feature.
- Sequence execution remains linear; this feature does not introduce nested branches or loops.
- Only `imageVisible` and `commandOutcome` condition types are in scope for v1.
- `commandOutcome` expected states in v1 are limited to `success`, `failed`, and `skipped`.

### Dependencies

- Existing sequence execution service and sequence authoring workflow.
- Existing condition evaluator capability for image-visibility checks.
- Existing execution logging pipeline.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: Authors can create and save a mixed 5-step sequence with per-step conditions in under 3 minutes during usability validation.
- **SC-002**: In acceptance tests covering at least 8 visibility permutations, per-step execute/skip outcomes match expected results in 100% of runs.
- **SC-003**: At least 90% of first-time authors can configure a two-condition "return home then open menu" sequence without assistance.
- **SC-004**: Sequence save validation identifies the offending step in 100% of malformed condition scenarios during acceptance validation.
