# Feature Specification: Primitive Tap in Commands

**Feature Branch**: `027-add-primitive-tap-action`  
**Created**: 2026-02-24  
**Status**: Draft  
**Input**: User description: "Implement a possibility to add primitive tap actions to the commands. This means the command will include a click action step, where an action does not explicitly exist and will not be executed unless the detection with the x/y offset is specified. It should work as if there was an action with tap(0,0) was selected, with one exception: if a detection fails, the tap should not happen at all."

## Clarifications

### Session 2026-02-24

- Q: How should the system handle a primitive tap step that has no detection target configured? → A: Reject save/validation when a primitive tap step has no detection target (step is invalid).
- Q: How should the system handle an out-of-bounds final tap point after applying offsets? → A: Skip tap when computed point is out of bounds and mark step as skipped/invalid-target.
- Q: Which detection result should be used when multiple matches are found? → A: Use the highest-confidence detected match for primitive tap.

## User Scenarios & Testing *(mandatory)*

<!--
  IMPORTANT: User stories should be PRIORITIZED as user journeys ordered by importance.
  Each user story/journey must be INDEPENDENTLY TESTABLE - meaning if you implement just ONE of them,
  you should still have a viable MVP (Minimum Viable Product) that delivers value.
  
  Assign priorities (P1, P2, P3, etc.) to each story, where P1 is the most critical.
  Think of each story as a standalone slice of functionality that can be:
  - Developed independently
  - Tested independently
  - Deployed independently
  - Demonstrated to users independently
-->

### User Story 1 - Add Detection-Gated Primitive Tap (Priority: P1)

As a command author, I can add a primitive tap step directly in a command without creating a separate reusable action, so I can quickly define a detection-followed click flow.

**Why this priority**: This is the core requested capability and unlocks faster command authoring for the main execution path.

**Independent Test**: Can be fully tested by creating a command with a primitive tap step tied to a valid detection and confirming the click is executed at the detected location plus offsets.

**Acceptance Scenarios**:

1. **Given** a command includes a primitive tap step with detection and offsets, **When** detection succeeds, **Then** the system executes one tap using the detected point adjusted by the configured x/y offsets.
2. **Given** a command includes a primitive tap step with detection and offsets, **When** detection fails, **Then** the system does not execute any tap for that step.

---

### User Story 2 - Preserve Existing Action-Based Behavior (Priority: P2)

As an operator running existing automations, I need current action-referenced command steps to behave unchanged so that introducing primitive tap does not break existing commands.

**Why this priority**: Backward compatibility prevents regressions in existing command libraries and running automations.

**Independent Test**: Can be tested by running existing commands that use explicit action references and verifying outcomes are unchanged before and after enabling primitive tap support.

**Acceptance Scenarios**:

1. **Given** a command step references an explicit action, **When** the command runs, **Then** the system executes the explicit action flow exactly as before.

---

### User Story 3 - Prevent Unsafe Default Taps (Priority: P3)

As a command author, I need primitive tap steps to require detection input so accidental or contextless taps are prevented.

**Why this priority**: This protects reliability and safety by ensuring taps only happen with a valid detected target.

**Independent Test**: Can be tested by configuring a primitive tap step without detection details and confirming command validation rejects the step as invalid.

**Acceptance Scenarios**:

1. **Given** a primitive tap step has no detection target, **When** the command is saved or validated, **Then** the system rejects the step as invalid and the command cannot proceed with that step.

---

### Edge Cases

- Detection returns a point near screen boundaries and the configured offset moves the target outside valid tap bounds; the step records skipped/invalid-target and no tap occurs.
- Detection succeeds multiple times in one evaluation pass; only the highest-confidence match is used for the primitive tap location.
- Primitive tap step is present in legacy command data that predates this feature and omits required detection fields; validation rejects the step until detection is provided.
- Detection confidence is below threshold; the tap is treated as failed and is not executed.
- Command contains a mix of explicit action steps and primitive tap steps; each step type follows its own execution rules without cross-impact.

## Requirements *(mandatory)*

### Functional Requirements
- **FR-001**: System MUST allow a command step to define a primitive tap behavior that does not require selecting or storing a separate reusable action entity.
- **FR-002**: System MUST treat primitive tap behavior as equivalent to a tap action with base offset `(0,0)` and then apply the step’s configured detection-based x/y offset to compute the final tap location.
- **FR-003**: System MUST execute a primitive tap only when the associated detection result succeeds for that step.
- **FR-004**: System MUST skip primitive tap execution when detection returns no match or when the match confidence is below the primitive step’s configured confidence threshold (or the default detection threshold when confidence is unspecified).
- **FR-005**: System MUST reject any primitive tap step as invalid during command save/validation when required detection context is missing.
- **FR-006**: System MUST preserve existing behavior for command steps that reference explicit actions.
- **FR-007**: System MUST support command definitions containing both explicit action steps and primitive tap steps in the same command.
- **FR-008**: System MUST expose execution outcome per primitive tap step so operators can distinguish detection-driven tap success versus skipped tap due to detection failure.
- **FR-009**: System MUST skip primitive tap execution when the computed final tap point is outside valid screen bounds and record the outcome as skipped/invalid-target.
- **FR-010**: System MUST select the highest-confidence detection result when multiple candidate matches are available for a primitive tap step.

### Key Entities *(include if feature involves data)*

- **Command Step**: A single executable unit in a command flow, including step type, ordering, and execution parameters.
- **Primitive Tap Step**: A command step variant that performs a tap inline without an external action reference and requires detection input to activate.
- **Detection Target**: The matching criteria and result for finding a point of interaction, including match status and detected coordinates.
- **Tap Offset**: The x/y adjustment applied relative to the detected location to derive the final tap point.
- **Step Execution Result**: The per-step outcome that records whether detection succeeded and whether tap execution occurred or was skipped.

## Assumptions

- Primitive tap is intentionally limited to single-tap behavior only for this feature.
- Existing command schema and existing action references remain supported and backward compatible.
- Operators need visibility into why a primitive tap was skipped, not just whether the whole command failed.

## Success Criteria *(mandatory)*

### Measurable Outcomes
- **SC-001**: In validation tests, 100% of primitive tap steps with successful detection execute exactly one tap at the detected location adjusted by configured offsets.
- **SC-002**: In validation tests, 100% of primitive tap steps with failed detection execute zero taps.
- **SC-003**: Existing command runs that use only explicit action references show no behavior regression in at least 95% of a baseline suite of at least 20 pre-existing action-only scenarios, measured by matching pass/fail outcomes before and after this feature.
- **SC-004**: Authors can configure a command with primitive tap in under 1 minute without creating any additional reusable action artifact.
