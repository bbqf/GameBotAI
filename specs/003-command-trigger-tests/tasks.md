# Task Plan: Command & Trigger Test Confidence

**Branch**: 003-command-trigger-tests
**Spec**: ./spec.md
**Checklist**: ./checklists/requirements.md

## Strategy Overview
Focus on deterministic, isolated evaluation of command execution gating and trigger statuses across boundary conditions (confidence/similarity/time). Leverage existing evaluators; introduce minimal abstractions only where nondeterminism exists (time, OCR/image input).

## Fixture & Abstraction Plan
- Time Control: Introduce `ITestClock` abstraction (wrapper over `DateTimeOffset.UtcNow`) if not present. Inject into evaluators needing time (Delay, Schedule) via factory or adapter used only in tests.
- OCR Stub: Implement in tests a `StubTextOcr` (implements `ITextOcr`) returning configurable `OcrResult(Text, Confidence)` sequences.
- Screen Source Stub: Implement `StubScreenSource : IScreenSource` producing deterministic `Bitmap` for cropping logic and text/image evaluators.
- Reference Image Store Stub: Implement `StubReferenceImageStore : IReferenceImageStore` returning controlled bitmaps to exercise similarity thresholds.
- Bitmap Fixtures: Generate small synthetic images (e.g., 8x8 grayscale patterns) to exercise NCC path, constant image path, and region mismatch path.

## Test Grouping
1. Unit Tests: Pure evaluator logic (TextMatch, ImageMatch, Delay, Schedule) with stubs.
2. Integration Tests: API endpoints `/commands/{id}/evaluate-and-execute` and `/commands/{id}/force-execute` verifying stored trigger state transitions & command execution outcomes.
3. Repeatability Harness: A single test or script looping evaluator test suite 30x to detect flakiness.

## Required New Test Files (Proposed)
- tests/unit/Triggers/TextMatchEvaluatorBoundaryTests.cs
- tests/unit/Triggers/ImageMatchEvaluatorSimilarityTests.cs
- tests/unit/Triggers/DelayTriggerEvaluatorBoundaryTests.cs
- tests/unit/Triggers/ScheduleTriggerEvaluatorBoundaryTests.cs
- tests/unit/Commands/CommandEvaluationFlowTests.cs (covers satisfied, pending, failed, cycle)
- tests/integration/Commands/CommandForceExecuteBypassTests.cs
- tests/integration/Triggers/TriggerStatePersistenceTests.cs
- tests/integration/Determinism/RepeatabilityRunTests.cs (optional)

## Extensions to Existing Tests
- Augment `CommandEvaluateAndExecuteTests` with not-found text trigger scenarios.
- Extend `TriggerEvaluationTests` to include new reason code assertions (text_absent, similarity_below_threshold, delay_elapsed, waiting_delay).

## Scenario Matrix (Representative)
| Trigger Type | Condition | Inputs | Expected Status | Reason |
|--------------|-----------|--------|-----------------|--------|
| Text(found) | present+>=threshold | Text=Target,Conf=Thr | Satisfied | text_found |
| Text(found) | present<threshold | Text=Target,Conf=Thr-ε | Pending | text_not_found |
| Text(not-found) | absent | Text=Other | Satisfied | text_absent |
| Text(not-found) | present+>=threshold | Text=Target | Pending | text_present |
| Image | similarity=threshold | Sim=Thr | Satisfied | similarity_met |
| Image | similarity<threshold | Sim=Thr-ε | Pending | similarity_below_threshold |
| Delay | before threshold | Elapsed<thr | Pending | waiting_delay |
| Delay | at/after threshold | Elapsed>=thr | Satisfied | delay_elapsed |
| Schedule | before timestamp | now<p.ts | Pending | waiting_for_time |
| Schedule | after timestamp | now>=p.ts | Satisfied | time_reached |

## Cycle Detection Tests
- Direct self-reference command (A→A) returns cycle_detected and no execution.
- Indirect cycle (A→B→C→A) enumerated via evaluation call returning cycle_detected.

## Force-Execute Tests
- Pending trigger bypass: executes steps even when trigger pending, but still rejects cycles.
- Disabled trigger bypass: executes steps and records reason that trigger gating skipped (assert via response meta if available).

## Determinism Controls
- Eliminate actual system clock dependencies: pass injected `now` values.
- Avoid randomization; explicitly set confidence/similarity numerics.
- No sleeps: simulate elapsed by incrementing `now` in test sequence.

## Coverage Targets Alignment
Map FR requirements to proposed tests ensuring each FR-001..FR-015 has at least one test file or scenario.

## Implementation Phases
1. Add test stubs & helper abstractions (clock, OCR, screen, image store) inside test projects.
2. Add unit evaluator boundary tests.
3. Add integration command execution tests with persisted triggers.
4. Implement repeatability harness (loop test or script) and run locally.
5. Refine assertions for reason codes.
6. Measure coverage; add missing edge tests.

## Risks & Mitigation
- Bitmap disposal leaks: Use `using` or test helper ensuring disposal.
- Confidence threshold off-by-one: Add tests for exactly threshold and threshold - 0.0001.

## Completion Criteria
- All matrix scenarios green.
- Repeatability harness zero flake failures across 30 loops.
- Coverage report meets ≥90% for target areas.

