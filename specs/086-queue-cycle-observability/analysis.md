# Cross-Artifact Analysis: Queue Cycle Observability

**Feature**: 086-queue-cycle-observability | **Date**: 2026-09-14
**Artifacts**: spec.md, plan.md, research.md, data-model.md, contracts/queue-cycles.md, tasks.md

## Round 1

### Coverage

All 21 functional requirements (FR-001 – FR-020 plus FR-008a) and all 7 success criteria are
referenced by at least one downstream artifact, and every task maps to a requirement. No orphan tasks,
no unreferenced requirements.

### Findings

| # | Severity | Finding | Resolution |
|---|---|---|---|
| F1 | **High** | **Self-contradictory responses were possible.** The design keyed the `health` block off `IQueueRunRegistry` alone. But `status` and the run handle live in two stores updated at different moments: `QueueStartAsync` calls `_registry.TryAdd` well before `_runtime.SetStatus(Running)`, and the run's `finally` calls `SetStatus(Stopped)` *before* `_registry.Remove`. A read landing in either window would return `status: "Stopped"` together with a populated `health` — precisely the confusion FR-008 exists to prevent, and worse than omitting health, because the response disagrees with itself. | Added **FR-008a** (a response must not contradict itself; both read paths must agree). Both paths now gate on the conjunction `GetStatus(id) == Running` **and** a registered handle, through one shared helper (T015) that T022 is required to reuse. Documented in plan (Design → liveness gate), data-model, contracts, and research **R8**. New regression test T028a. |
| F2 | Medium | **Undeclared field.** `runStartedAt` appeared in data-model.md and the contract with no functional requirement behind it — unrequested scope by the letter of the spec. | It is genuinely useful ("running since when") and free, so FR-001 was widened to cover the run's start instant rather than dropping the field. T012 now names it explicitly. |
| F3 | Medium | **Uncovered edge case.** The spec's "two queues run concurrently on different devices" edge case had no task or test; T028 only re-ran the existing concurrency suite unchanged, which cannot assert anything about the new fields. | Added **T028b** asserting each concurrent run reports its own health and cycles with no cross-talk. |
| F4 | Low | **Unverified assumption.** The spec's service-restart edge case asserted that a restarted service reports no current-run health, but nothing had checked that queue status is not persisted. | Verified: `QueueRuntimeStore` is an in-memory singleton whose own class comment states statuses are lost on restart by design. Recorded as research **R9**; no code work needed. |

### Consistency checks that passed

- **Terminology** is stable across artifacts: "cycle" always means one once-per-run roster pass; no
  competing definition appears anywhere.
- **The cycle boundary** in plan/tasks matches the engine's own `cycles++` site, so the published count
  cannot disagree with the terminating summary's "across N cycles".
- **Status vocabulary** — `"success"` / `"failure"` — is reused from the execution log rather than
  invented, satisfying Constitution III. No artifact uses a boolean spelling.
- **Out-of-scope boundaries hold**: no requirement, design element or task acts on
  `consecutiveFailedCycles`, notifies anyone, touches the cancelled-vs-failed distinction, or changes
  `/subtree`. Issue #181's territory is untouched.
- **FR-018 (log untouched)** is satisfiable by construction — the feature writes nothing to the
  execution log at all — rather than by inspection.
- **Analyzer constraint** (research R5) is reflected as a hard rule in Phase 2's header and in every
  run-loop task.

## Round 2

Re-ran after applying all four resolutions. **No remaining findings.**

- FR-008a is now referenced by spec, plan, research (R8), data-model, contracts (twice) and tasks
  (T015, T022, T028a), with a single implementation point.
- FR-001 and T012 agree on `runStartedAt`.
- Every spec Edge Case now maps to a task or to an explicit by-construction argument:

| Edge case | Covered by |
|---|---|
| Cycle interrupted by a stop | By construction — an open cycle is never published (plan Design; T003) |
| Run restart resets health | T015a / FR-009; ledger is per-handle |
| Service restart | Research R9 — verified, no work needed |
| Cycle with no sequences | T010 (`RecordEmptyCycle`) + T016 (entry-less cycle succeeds) |
| Mixed success/failure in a cycle | T003 outcome derivation + T016 |
| Very fast cycling | T003 ring trim + T016 retention assertion + SC-005 |
| Two concurrent queues | **T028b** (added this round) |
| Out-of-range `limit` | T023 + T024 |

**Analyze result: clean after one fix round.** Proceeding to commit and implement.
