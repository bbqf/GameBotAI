# Data Model: Gamepack Install Failure Explanation

The feature stores no data. It adds fields to the answer of the install check.

## Problem (existing: `GamepackProblem`, answer: `GamepackProblemDto`)

| Field | Type | Status | Note |
|---|---|---|---|
| category | string | existing | `structure`, `signature`, `reference`, `conflict` |
| objectType | string | existing | manifest, sequence, command, image, seedTemplate |
| name | string or null | existing | |
| owner | string or null | existing | Conflict only: `user` or `<name> <version>` |
| message | string | existing | |
| code | string | existing, optional | `signature_<status>`, `update_in_progress` |
| takeOverResolves | bool | NEW, always present | True only for a conflict with owner `user`, no code, and take-over off |
| remedy | string | NEW, always present | One or two STE sentences. Says what the caller does. |

Rules: `takeOverResolves` and `remedy` are computed from the other fields. They are not stored. For a request with take-over on,
`takeOverResolves` is false for each remaining problem. A second user-owned object with the same name is a conflict that
take-over resolves, so it gives true when take-over is off.

## Failure summary (NEW: `GamepackInstallSummaryDto`)

| Field | Type | Note |
|---|---|---|
| headline | string | Example: `20 conflicts.` or `3 problems in 2 categories.` For no problem: `No problems.` |
| entries | list of entry | One entry for each category that has a problem. Order: structure, signature, reference, conflict. |

### Summary entry (`GamepackInstallSummaryEntryDto`)

| Field | Type | Note |
|---|---|---|
| category | string | One of the four categories |
| count | int | Number of problems in the category. At least 1. |
| label | string | Example: `conflicts: objects owned by the user`. Singular form for count 1. |
| remedy | string | Example: `Install with take-over to replace them, or rename or delete them.` |
| resolvedByTakeOver | bool | True only if every problem in the entry has `takeOverResolves` true. False for all categories but conflict. |

## Install answer (existing: `GamepackInstallResponse`)

New members: `summary` (placed before `problems`) and `wouldInstallWithTakeOver` (bool, always present).
All existing members stay as they are.

### State of `wouldInstallWithTakeOver`

| Case | Value |
|---|---|
| Valid check | true |
| Invalid check, take-over on in the request | false |
| Invalid check, take-over off, second validator run with take-over is valid | true |
| Invalid check, take-over off, second run has problems | false |
| Invalid JSON | false |
| Signature rejected | false |

## Domain outcome (`GamepackInstallOutcome`)

New property `WouldInstallWithTakeOver` (bool). The service sets it. The endpoint copies it to the answer.

## Explainer (NEW: `GamepackFailureExplainer`, static, pure)

- `Explain(problem, takeOverRequested)` gives the `takeOverResolves` fact and the remedy for one problem.
- `Summarize(problems, takeOverRequested)` gives the summary: grouped by category, in the fixed order, with the label,
  the remedy, the count, and the flag.

## Web UI types (`CatalogInstallResult`)

New optional members: `summary`, `wouldInstallWithTakeOver`, and per problem `takeOverResolves` and `remedy`. They are optional so the
page works with an older server. New class `CatalogInstallRejectedError` holds the `CatalogInstallResult` of a 422 answer.
