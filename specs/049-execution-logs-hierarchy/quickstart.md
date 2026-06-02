# Quickstart: Execution Logs Reflect What Was Actually Executed

**Feature**: 049-execution-logs-hierarchy

This guide validates the feature end-to-end after implementation.

## Prerequisites

- Service running (`GameBot.Service`) and web-ui dev server (or built bundle).
- At least one game + ADB device/session available.
- A **sequence** that invokes **two or more commands** (each command with at least one primitive tap / wait), plus one **stand-alone command** for the control case.

## Scenario A — Sequence shows as one top-level entry with nested sub-elements (FR-001/002/003/003a)

1. Open the **Execution** page; start a session.
2. Execute the multi-command sequence.
3. Open **Execution Logs**.
4. **Expect**: exactly **one new top-level row** for the sequence run. No standalone rows for the commands the sequence invoked.
5. Expand the sequence row.
6. **Expect**: nested tree rows for each step in order — command-backed steps show the command name; conditions show their result; loops show iterations; waits show wait details; each shows applied delay. Expanding a command-backed step reveals that command's own primitive/tap/wait outcomes.
7. **Expect**: 100% of the detail visible today (taps, reasons, condition traces, wait timeouts, deep links) is reachable through expansion (SC-002, SC-007).

## Scenario B — Stand-alone command still appears as its own entry (FR-004)

1. From the Execution page, execute the stand-alone command directly.
2. Open **Execution Logs**.
3. **Expect**: a single top-level row for that command with full details, exactly as before. It is a leaf (no expand affordance, `childCount = 0`).

## Scenario C — Live updates during a running sequence (FR-007/008, SC-005)

1. Execute a longer-running sequence (multiple commands / waits).
2. Open **Execution Logs** while it is still running (or keep the page open from before starting).
3. **Expect**: a single **in-progress** top-level entry (status `running`) for the run — and **no** standalone child-command rows.
4. Expand it.
5. **Expect**: sub-elements appear/update as steps complete, without a manual page reload (polling ~2s).
6. When the sequence finishes:
7. **Expect**: the **same** top-level entry settles into its final state (`success`/`failure`) with all nested sub-elements and the correct aggregate status (FR-005).

## Scenario D — Nested sequences (FR-009)

1. Execute a sequence that invokes another sequence.
2. **Expect**: the nested sequence appears as an expandable node **under** the parent sequence, with its own sub-elements — never as a separate top-level row for that run.

## Scenario E — Filtering / sorting over roots only (FR-010, SC-006)

1. In Execution Logs, sort by Object Name and by Status; apply object-name and status filters.
2. **Expect**: results contain only top-level entries; no child-command rows leak into results; counts/order are correct.

## Scenario F — Historical logs (FR-012)

1. Confirm logs recorded before this change still appear in the list and open without error (rendered as completed leaf roots).

## API checks (optional, via curl/REST client)

```bash
# Roots only — should NOT include commands invoked by a sequence run
GET /api/execution-logs

# Subtree of a sequence run — root + nested sub-elements
GET /api/execution-logs/{sequenceExecutionId}/subtree
```

- `GET /api/execution-logs` items each include `status` and `childCount`.
- `GET /api/execution-logs/{id}/subtree` returns `{ executionId, status, root: { ...children[] } }`.
- A child command's own execution detail is still retrievable via `GET /api/execution-logs/{childId}` (kept, just filtered from the list).

## Automated test verification

```powershell
dotnet test                      # backend unit/integration/contract (ExecutionLogs/*)
npm --prefix src/web-ui test     # web-ui Jest (ExecutionLogs tree + polling)
```

**Expect**: all green, including new tests for roots-only listing, child linkage, subtree projection, in-progress→final upsert, and the UI expandable-tree + polling behavior. Coverage on touched areas ≥80% line / ≥70% branch.
