# Feature Specification: Command & Trigger Test Confidence

**Feature Branch**: `003-command-trigger-tests`  
**Created**: 2025-11-21  
**Status**: Draft  
**Input**: User description: "Improve unit and integration testing for command execution (evaluate-and-execute & force-execute) and trigger evaluation (found/not-found correctness)"

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

### User Story 1 - Reliable Command Execution Evaluation (Priority: P1)

Stakeholders (product owners, maintainers) need confidence that a command only executes when its evaluation (including trigger gating) is satisfied, and that cycle detection or failed steps stop execution predictably.

**Why this priority**: Incorrect or unstable command gating risks unintended automated actions; ensuring correctness is foundational for further automation features.

**Independent Test**: Validate a set of commands with varied step structures (empty, single, multi-step) against trigger states (satisfied, pending, disabled) and verify execution outcomes and recorded evaluation reasons.

**Acceptance Scenarios**:
1. **Given** a command with a satisfied trigger, **When** evaluate-and-execute is invoked, **Then** all steps run in order and a success result is recorded.
2. **Given** a command whose trigger is pending, **When** evaluate-and-execute is invoked, **Then** no steps run and a pending status reason is returned.
3. **Given** a command containing a cycle (A references B, B references A), **When** force-execute is invoked, **Then** a clear cycle rejection response is returned (no execution).
4. **Given** a multi-step command where step 2 fails logically, **When** evaluate-and-execute is invoked, **Then** steps after the failure do not run and failure reason is surfaced.

---

### User Story 2 - Comprehensive Trigger Condition Coverage (Priority: P2)

Stakeholders require assurance that all trigger types (text-match found/not-found, image-match similarity threshold, delay, schedule) evaluate consistently at boundary and nominal conditions.

**Why this priority**: Triggers drive automation timing; inconsistent evaluation (e.g., misclassified text presence) causes missed or spurious actions.

**Independent Test**: Individually exercise each trigger type with controlled input stimuli (mocked screen/text/time) to assert status transitions and reasons.

**Acceptance Scenarios**:
1. **Given** a text-match trigger (mode=found) with target present at required confidence, **When** evaluated, **Then** status is satisfied and reason indicates text_found.
2. **Given** a text-match trigger (mode=not-found) with target absent, **When** evaluated, **Then** status is satisfied and reason indicates text_absent.
3. **Given** an image-match trigger at exact similarity threshold, **When** evaluated, **Then** status is satisfied (inclusive boundary) and similarity recorded.
4. **Given** a delay trigger before elapsed time, **When** evaluated, **Then** status pending with waiting_delay reason; after time passes, satisfied with delay_elapsed.
5. **Given** a schedule trigger evaluated before timestamp, **Then** pending with waiting_for_time; after timestamp, satisfied with time_reached.

---

### User Story 3 - Deterministic & Non-Flaky Test Execution (Priority: P3)

Engineering wants repeatable test runs (unit + integration) producing identical outcomes across multiple consecutive executions without intermittent failures.

**Why this priority**: Flaky tests erode trust and slow delivery; determinism enables rapid iteration and safer refactors.

**Independent Test**: Execute targeted command & trigger test suite repeatedly (≥30 runs) capturing any divergence; suite confined to isolated data directories and non-parallel collections where needed.

**Acceptance Scenarios**:
1. **Given** the test suite executed 30 consecutive times, **When** monitoring results, **Then** zero failures or status inconsistencies occur.
2. **Given** concurrent non-related tests run in parallel, **When** isolation-marked config/trigger tests run, **Then** no cross-run contamination (environment variables, persisted config) is observed.

---

### Edge Cases

- Command with zero steps (should execute trivially—recorded as success without side effects)
- Command referencing itself directly (single-node cycle)
- Text trigger with target present but confidence just below threshold (pending)
- Text trigger with mixed case target vs different case in source (case-insensitive match behavior)
- Image trigger with similarity exactly one increment below threshold (pending) and one above (satisfied)
- Delay trigger with seconds = 0 (immediate satisfaction)
- Schedule trigger with timestamp in the past at enable time (immediate satisfaction vs disabled scenario)
- Disabled trigger (always treated as inactive—command evaluation blocked or bypass only via force-execute)
- Cooldown expiry boundary: evaluation just before and just after cooldown window
- Force-execute when underlying trigger would be pending (should bypass gating but still reject cycles)
- Multiple triggers scenario (if supported in future—documented assumption: single trigger per command currently)

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: Provide unit tests covering command evaluation outcomes: satisfied, pending, failed, cycle-detected, force-execute bypass.
- **FR-002**: Provide integration tests for endpoints: evaluate-and-execute, force-execute with real persistence and isolation of state.
- **FR-003**: Implement deterministic test inputs for text OCR (stubbed text and confidence), image matching (generated bitmap fixtures), and time-based triggers (controllable clock abstraction or offset approach).
- **FR-004**: Validate text-match triggers for both modes (found, not-found) at confidence boundary (just below, at, above threshold).
- **FR-005**: Validate image-match similarity boundary conditions (threshold - ε, threshold, threshold + ε) recording similarity values.
- **FR-006**: Validate delay and schedule triggers before, exactly at, and after target times, asserting correct reasons and status transitions.
- **FR-007**: Ensure cycle detection test covers direct self-reference and multi-step indirect cycles (A→B→C→A).
- **FR-008**: Ensure force-execute tests confirm bypass of trigger pending/disabled but preserved cycle rejection and failure propagation.
- **FR-009**: Provide isolation for any test mutating persisted configuration or environment using collection-level serialization (reusing existing isolation pattern) to prevent flakiness.
- **FR-010**: Capture and assert structured evaluation reasons for each trigger status (e.g., text_found, text_absent, similarity_below_threshold, waiting_delay, waiting_for_time, delay_elapsed, time_reached, cycle_detected).
- **FR-011**: Introduce repeatability validation (script or test harness) that runs the focused suite ≥30 times and reports zero divergence.
- **FR-012**: Achieve ≥90% line coverage for command execution coordination and ≥90% line coverage for trigger evaluators (qualitative value focus—not tied to tooling specifics in spec).
- **FR-013**: Document assumptions and boundaries (single trigger per command; sequential execution; stop-on-failure) within test descriptions for clarity.
- **FR-014**: Ensure tests do not rely on wall-clock timing susceptible to race conditions (use deterministic waits or simulated time).
- **FR-015**: Provide negative tests: disabled trigger, unmet similarity, low confidence, unresolved delay, schedule future time, cooldown active.

### Key Entities *(include if feature involves data)*

- **Command**: Represents an ordered collection of steps gated by a trigger; attributes: id, steps, triggerId (or trigger link), evaluation outcome.
- **CommandStep**: Atomic action reference; attributes: type, order, target; used to verify execution ordering and stop-on-failure behavior.
- **Trigger**: Condition definition (text-match, image-match, delay, schedule) with parameters and enabled state.
- **TriggerEvaluationResult**: Outcome snapshot with status (satisfied, pending, failed), reason code, similarity or confidence metrics, timestamps.
- **CommandEvaluationScenario**: Aggregated test fixture representing command + trigger + expected outcome for boundary and nominal cases.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: ≥90% code coverage for command execution coordination and trigger evaluator components (measured via internal tooling) achieved upon feature completion.
- **SC-002**: 100% deterministic outcomes across 30 consecutive full test suite runs (no intermittent failures or status variance).
- **SC-003**: Found/not-found text trigger tests correctly classify presence/absence with 100% accuracy in controlled fixtures.
- **SC-004**: Image similarity boundary tests consistently classify threshold cases (no misclassification) across 20 repeated evaluations.
- **SC-005**: Delay and schedule trigger evaluations return correct status transitions at before/at/after timestamps in all tested permutations.
- **SC-006**: Cycle detection tests reliably reject cycles (direct + indirect) with consistent reason codes in 100% of attempts.
- **SC-007**: Force-execute tests demonstrate bypass of trigger pending/disabled states while preserving cycle protection and stop-on-failure semantics.
- **SC-008**: Average execution time for the focused command/trigger test suite ≤10 seconds on a standard development machine, supporting rapid feedback.
- **SC-009**: Zero environment/config leakage detected (no unexpected persisted values) verified by isolation checks and post-run directory diff.

### Assumptions

- Single trigger per command (multi-trigger orchestration deferred).
- Sequential execution and stop-on-failure semantics remain unchanged during this feature.
- Existing isolation collection pattern is sufficient (no new parallelism constraints required).
- Deterministic OCR and image evaluation achievable via in-memory stubs and generated bitmaps.

### Out of Scope

- Performance optimization of command execution engine.
- New trigger types or composite trigger logic.
- Refactoring production code beyond minor testability adaptations (e.g., clock abstraction introduction if needed).

### Dependencies

- Access to current command, trigger, and evaluation service domain models.
- Ability to introduce or leverage a time abstraction (if not already present) for delay/schedule testing.
- Existing isolation collection for config-sensitive tests.

### Risks & Mitigations

- Risk: Non-deterministic timing causing flakiness. Mitigation: replace direct wall-clock waits with simulated progression or fixed offsets.
- Risk: Over-coupling tests to internal implementation details. Mitigation: assert only public outcomes (status, reason) and observable side effects.
- Risk: Coverage target pressures fragile tests. Mitigation: prioritize meaningful scenario coverage over superficial line hits.

## Validation Summary

All mandatory sections completed; no [NEEDS CLARIFICATION] markers present. Specification focuses on observable behavior and measurable outcomes without prescribing implementation technologies.
