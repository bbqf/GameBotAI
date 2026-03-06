# Feature Specification: Conditional Sequence Steps (Minimal)

**Feature Branch**: `031-sequence-conditional-steps`  
**Created**: 2026-03-05  
**Status**: Draft  
**Input**: User description: "Add minimal conditional sequence step support so a sequence can run actions only when an image is visible, then continue with unconditional steps."

## Clarifications

### Session 2026-03-05

- Q: Should this feature include full branch graphing (true-target/false-target jumps)? → A: No. Keep v1 minimal with linear ordered steps; conditional steps only decide execute-vs-skip for their own action.
- Q: Should v1 support complex boolean expressions (AND/OR/NOT groups)? → A: No. Single condition per conditional step.
- Q: If condition evaluation is false, should sequence fail? → A: No. Step is skipped and sequence continues.
- Q: If condition evaluation errors (e.g., missing image reference at runtime), should sequence continue? → A: No. Step fails and sequence stops.
- Q: What is the normal-load profile for performance validation? → A: No concurrency; single sequence run with 30 steps, 10 conditional steps, measured over 15 minutes.
- Q: What data baseline does this feature assume? → A: Clean-slate scope only; assume no pre-existing actions, commands, or sequences.
- Q: What action payload scope should v1 support? → A: Generic action model; allow any existing supported action payload type in both action and conditional steps.

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Execute action only when image is visible (Priority: P1)

As an automation author, I can add a conditional step to a sequence so its action executes only when a target image is visible.

**Why this priority**: This is the direct capability needed to model location-aware movement/click flows.

**Independent Test**: Create one sequence with one conditional step and one unconditional step, run with image visible and not visible, and verify execute-vs-skip behavior.

**Acceptance Scenarios**:

1. **Given** a conditional step with `imageVisible(imageA)`, **When** image A is visible at or above threshold, **Then** the step action executes.
2. **Given** a conditional step with `imageVisible(imageA)`, **When** image A is not visible, **Then** the step action is skipped and sequence continues.
3. **Given** a sequence containing conditional then unconditional steps, **When** conditional evaluates false, **Then** later unconditional steps still execute.

---

### User Story 2 - Mix conditional and unconditional steps in one sequence (Priority: P1)

As an automation author, I can compose a sequence with multiple conditional steps followed by normal action steps.

**Why this priority**: The primary use case requires checking multiple location indicators before performing a final tap.

**Independent Test**: Build sequence: If A then tap(x1,y1), If B then tap(x2,y2), then tap(x3,y3); verify all four visibility permutations.

**Acceptance Scenarios**:

1. **Given** A visible and B not visible, **When** sequence runs, **Then** step A executes, step B skips, final tap executes.
2. **Given** A not visible and B visible, **When** sequence runs, **Then** step A skips, step B executes, final tap executes.
3. **Given** A and B both visible, **When** sequence runs, **Then** both conditional taps execute, then final tap executes.
4. **Given** neither A nor B visible, **When** sequence runs, **Then** both conditional steps skip, then final tap executes.

---

### User Story 3 - Author from empty state (Priority: P2)

As an automation author, I can create and run my first sequence in an empty repository state.

**Why this priority**: Feature rollout assumes a clean start with no prior sequence/action/command data.

**Independent Test**: Start from empty data directories, create one mixed sequence, save, execute, and verify expected outcomes.

**Acceptance Scenarios**:

1. **Given** no sequences currently exist, **When** author creates a new mixed sequence and saves, **Then** the sequence is persisted successfully.
2. **Given** the newly created mixed sequence, **When** it is executed under defined image states, **Then** outcomes match configured execute/skip rules.

### Edge Cases

- Referenced image ID does not exist at save-time.
- Referenced image is deleted after sequence authoring.
- Condition threshold omitted (must fallback to configured default).
- Condition evaluator times out or screenshot capture fails.
- Both conditional images visible in same frame (both conditional steps execute independently, in order).
- Sequence repository starts empty and first save must initialize persisted file shape correctly.
- Step references an unsupported or malformed action payload type.
- Repeated runs with identical frame inputs produce different timestamps/IDs but identical step decision outcomes.

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: System MUST support sequence steps with explicit `stepType` discriminator.
- **FR-002**: System MUST support `stepType=action` for unconditional action execution.
- **FR-003**: System MUST support `stepType=conditional` containing exactly one condition and one action payload.
- **FR-004**: V1 condition type MUST include `imageVisible` with required `imageId` and optional `minSimilarity`.
- **FR-005**: For `conditional` steps, runtime MUST evaluate condition once at step execution time.
- **FR-006**: If condition evaluates true, runtime MUST execute the step action.
- **FR-007**: If condition evaluates false, runtime MUST skip the step action and continue to next step.
- **FR-008**: False condition result MUST NOT mark sequence as failed.
- **FR-009**: Condition evaluation error MUST mark step failed and stop sequence.
- **FR-010**: Steps MUST execute strictly in declared order; no branching graph behavior in this feature.
- **FR-011**: Validation MUST reject unknown `stepType`, unknown condition type, or missing required fields.
- **FR-012**: Validation MUST reject `imageVisible` conditions that reference non-existent image IDs at save-time.
- **FR-013**: System MUST assume clean-slate data scope and require only explicit v1 step schema (`stepType`-based) for all persisted sequence steps.
- **FR-014**: Execution logging MUST include per-step fields: `stepType`, condition summary (if present), evaluation result (`true`/`false`/`error`), and action outcome (`executed`/`skipped`).
- **FR-015**: System MUST allow creating and persisting a first sequence successfully when no sequence/command/action data exists yet.
- **FR-016**: System MUST support any existing valid action payload type in `stepType=action` and as the `action` payload inside `stepType=conditional`.
- **FR-017**: Validation MUST reject unsupported or malformed action payload types in both unconditional and conditional steps.
- **FR-018**: The authoritative set of supported action payload types MUST be sourced from the existing action execution infrastructure contract and validated uniformly for both unconditional and conditional step payloads.

### Non-Functional Requirements

- **NFR-001**: Added p95 latency for one conditional evaluation under normal load MUST be <= 200 ms.
- **NFR-002**: Repeated executions of the same sequence under identical inputs MUST produce consistent step outcomes, where consistency is evaluated on ordered per-step `conditionResult` and `actionOutcome` values only.
- **NFR-003**: For this feature, normal load is defined as one active sequence execution at a time (no concurrency), with 30 total steps including 10 conditional steps, measured over a continuous 15-minute run.

### Key Entities *(include if feature involves data)*

- **Sequence Step**: Ordered unit in sequence with `stepType` and execution payload.
- **Conditional Step**: Sequence step variant containing `condition` + `action`.
- **ImageVisible Condition**: Predicate that checks if an image target is detected at or above threshold.
- **Step Execution Record**: Per-step runtime log capturing condition result and execute/skip outcome.

## Assumptions

- Existing detection pipeline and image store are reused; no new external packages.
- Existing action execution infrastructure defines the set of supported action payload types.
- Authoring UI can initially expose minimal conditional editing (no graph builder required).
- Repository starts with no pre-existing action/command/sequence data for this feature rollout.
- Determinism checks intentionally exclude execution IDs, timestamps, and other run-instance metadata.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: In validation, 100% of conditional steps with true conditions execute their action exactly once.
- **SC-002**: In validation, 100% of conditional steps with false conditions execute zero actions and sequence continues.
- **SC-003**: From an empty repository state, first mixed sequence creation and save succeeds in 100% of validation runs.
- **SC-004**: Per-step logs for conditional steps include evaluation result and executed/skipped outcome in 100% of sampled runs.
- **SC-005**: Repeated runs with identical image visibility inputs produce identical ordered tuples of per-step (`conditionResult`, `actionOutcome`) in 100% of sampled validation runs.
- **SC-006**: Performance validation for SC-001/SC-002 execution path uses NFR-003 normal-load profile.
