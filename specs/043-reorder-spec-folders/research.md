# Research: Reorder Spec Folders

**Feature**: 043-reorder-spec-folders  
**Branch**: 038-reorder-spec-folders  
**Date**: 2026-05-30

## Decision: Chronological Order Source

- **Decision**: Use `git log --diff-filter=A -- specs/<old-folder>/` to find each folder's first-add commit, then sort by author date.
- **Rationale**: Git history is the authoritative record of when each feature was started. First-add commit (A filter) captures the moment the folder was introduced, not subsequent amendments.
- **Alternatives considered**: PR numbers (used in commit messages) are a proxy but can be out-of-order; commit timestamps on `spec.md` alone miss folders where the first commit only added `plan.md` or `research.md`.

## Decision: Tie-Breaking Rule

- **Decision**: Folders committed in the same git commit are ordered alphabetically by their short name (the part after the numeric prefix).
- **Rationale**: A deterministic, reproducible rule with no subjective judgment. Confirmed two tie groups exist (see below); alphabetical order is verifiable by any contributor.
- **Alternatives considered**: Reverse-alphabetical (arbitrary); by sub-file count (fragile); by spec completeness (subjective).

## Decision: Installer Minor Version Bump

- **Decision**: Increment `installer/versioning/version.override.json` `minor` from `"4"` to `"5"`, keep `patch` at `"0"`.
- **Rationale**: The versioning spec (026-installer-semver-upgrade) states minor increments with every new feature/branch. This maintenance feature constitutes a new feature line.
- **Alternatives considered**: Skip bump (violates versioning policy); bump patch instead (wrong per policy — patch resets on minor bump).

## Discovery: Two Tie Groups

**Group A — same commit `7103938` (PR #45, 2025-12-27 18:01:46):**
All ten folders were renamed/added together in the "Renumbered Feature Branches" commit. Their ORIGINAL creation dates (found via pre-rename paths) are all distinct, so no true tie exists in this group. Correct dates were retrieved by querying `git log --diff-filter=A -- specs/<original-001-name>/`.

**Group B — same commit `a6a79f0` (2026-03-11 09:02:18):**
- `031-sequence-conditional-steps`
- `032-per-step-conditions`

These were genuinely created in the same commit. Alphabetical tie-break: `per-step-conditions` < `sequence-conditional-steps` → per-step-conditions gets the lower number.

**Group C — same commit `8f4c8bd` (2026-02-18 08:56:24):**
- `024-backend-webui-installer`
- `025-standalone-windows-installer`

Alphabetical tie-break: `backend-webui-installer` < `standalone-windows-installer` → backend-webui-installer keeps the lower number. No swap needed.

## Complete Renaming Map

Sorted by first-commit author date. `→` indicates the new folder name.

| # | First Commit Date | Current Folder | New Folder |
|---|---|---|---|
| 001 | 2025-11-14 14:35 | `001-save-config` | `001-save-config` *(no change)* |
| 002 | 2025-11-14 21:36 | `002-config-logging-hardening` | `002-config-logging-hardening` *(no change)* |
| 003 | 2025-11-19 20:26 | `001-action-command-refactor` | `003-action-command-refactor` |
| 004 | 2025-11-21 12:17 | `003-command-trigger-tests` | `004-command-trigger-tests` |
| 005 | 2025-11-25 13:11 | `004-logging-config-refresh` | `005-logging-config-refresh` |
| 006 | 2025-11-25 13:11 | `004-tesseract-logging` | `006-tesseract-logging` |
| 007 | 2025-11-25 15:57 | `001-runtime-logging-control` | `007-runtime-logging-control` |
| 008 | 2025-11-26 13:10 | `005-fix-trigger-evaluate` | `008-fix-trigger-evaluate` |
| 009 | 2025-11-26 15:37 | `006-ocr-confidence-refactor` | `009-ocr-confidence-refactor` |
| 010 | 2025-11-27 23:37 | `007-image-storage` | `010-image-storage` |
| 011 | 2025-12-02 10:01 | `008-image-match-detections` | `011-image-match-detections` |
| 012 | 2025-12-05 22:17 | `009-image-detect-command` | `012-image-detect-command` |
| 013 | 2025-12-10 09:56 | `010-command-sequences` | `013-command-sequences` |
| 014 | 2025-12-17 17:45 | `011-sequence-logic` | `014-sequence-logic` |
| 015 | 2025-12-26 13:20 | `012-web-ui-authoring` | `015-web-ui-authoring` |
| 016 | 2025-12-26 21:55 | `001-authoring-crud-ui` | `016-authoring-crud-ui` |
| 017 | 2025-12-27 17:21 | `013-semantic-actions-ui` | `017-semantic-actions-ui` |
| 018 | 2025-12-27 23:42 | `017-unify-authoring-ui` | `018-unify-authoring-ui` |
| 019 | 2025-12-29 01:34 | `018-api-refactor` | `019-api-refactor` |
| 020 | 2025-12-30 01:03 | `019-web-ui-nav` | `020-web-ui-nav` |
| 021 | 2025-12-30 12:51 | `020-connect-game-action` | `021-connect-game-action` |
| 022 | 2025-12-30 23:34 | `021-images-authoring-ui` | `022-images-authoring-ui` |
| 023 | 2026-01-28 14:38 | `022-emulator-image-crop` | `023-emulator-image-crop` |
| 024 | 2026-02-13 09:11 | `023-authoring-execution-ui` | `024-authoring-execution-ui` |
| 025 | 2026-02-18 08:56 | `024-backend-webui-installer` | `025-backend-webui-installer` |
| 026 | 2026-02-18 08:56 | `025-standalone-windows-installer` | `026-standalone-windows-installer` |
| 027 | 2026-02-19 17:46 | `026-installer-semver-upgrade` | `027-installer-semver-upgrade` |
| 028 | 2026-02-27 14:34 | `027-add-primitive-tap-action` | `028-add-primitive-tap-action` |
| 029 | 2026-03-02 09:25 | `028-execution-log` | `029-execution-log` |
| 030 | 2026-03-02 18:35 | `029-execution-logs-tab` | `030-execution-logs-tab` |
| 031 | 2026-03-03 16:00 | `030-sequence-conditional-logic` | `031-sequence-conditional-logic` |
| 032 | 2026-03-11 09:02 | `032-per-step-conditions` | `032-per-step-conditions` *(no change)* |
| 033 | 2026-03-11 09:02 | `031-sequence-conditional-steps` | `033-sequence-conditional-steps` |
| 034 | 2026-04-13 16:36 | `033-command-loops` | `034-command-loops` |
| 035 | 2026-04-14 11:05 | `034-background-screenshot-service` | `035-background-screenshot-service` |
| 036 | 2026-04-14 18:01 | `035-ui-config-editor` | `036-ui-config-editor` |
| 037 | 2026-04-15 15:30 | `036-tap-wait-retry` | `037-tap-wait-retry` |
| 038 | 2026-05-25 16:31 | `001-sequence-random-delay` | `038-sequence-random-delay` |
| 039 | 2026-05-27 16:24 | `001-primitive-actions-refactor` | `039-primitive-actions-refactor` |
| 040 | 2026-05-29 08:30 | `001-wait-for-image` | `040-wait-for-image` |
| 041 | 2026-05-29 21:06 | `001-fix-sequence-step-names` | `041-fix-sequence-step-names` |
| 042 | 2026-05-30 19:28 | `037-loop-step-management` | `042-loop-step-management` |
| 043 | 2026-05-30 (new) | `043-reorder-spec-folders` | `043-reorder-spec-folders` *(this spec, no change)* |

## Folders With No Change Needed

- `001-save-config` — already #1 chronologically
- `002-config-logging-hardening` — already #2
- `032-per-step-conditions` — already correct position after tie-break
- `043-reorder-spec-folders` — new spec, created with correct number

## Rename Conflict Analysis

All new numbers are ≥ current numbers except for `031-sequence-conditional-steps` → `033` (stays ≥). To avoid transient conflicts when renaming, process renames **from highest current number downward** within the "current-numbered" block (i.e., 037 → 042, then 036 → 037, …), then handle all `001-*` and low-number renames last.

Concrete safe ordering:
1. Rename `037-loop-step-management` → `042-loop-step-management`
2. Rename `036-tap-wait-retry` → `037-tap-wait-retry`
3. Continue down: `035-ui-config-editor` → `036`, `034-background-screenshot-service` → `035`, etc.
4. Handle tie-swap: rename `031-sequence-conditional-steps` → `033-sequence-conditional-steps` (033 will be free by then)
5. Rename `032-per-step-conditions` → `032-per-step-conditions` — no-op, skip
6. Rename remaining mid-range: `033-command-loops` → `034`, etc.
7. Handle low-range: `030` → `031`, `029` → `030`, …, `003` → `004`
8. Handle `001-*` renames last (their targets 038–041 are already free): `001-sequence-random-delay` → `038`, etc.
9. Rename `001-authoring-crud-ui` → `016-authoring-crud-ui`
10. Rename `001-runtime-logging-control` → `007-runtime-logging-control`
11. Rename `001-action-command-refactor` → `003-action-command-refactor`

## Version Bump Details

**File**: `installer/versioning/version.override.json`  
**Current state**:
```json
{ "major": "0", "minor": "4", "patch": "0", "updatedBy": "bbqf", "updatedAtUtc": "2026-04-15T00:00:00Z" }
```
**Target state**:
```json
{ "major": "0", "minor": "5", "patch": "0", "updatedBy": "bbqf", "updatedAtUtc": "2026-05-30T00:00:00Z" }
```
