# Data Model: Reorder Spec Folders

**Feature**: 043-reorder-spec-folders

## Entities

### Spec Folder

A directory under `specs/` whose name follows the pattern `NNN-short-name`.

| Field | Description |
|---|---|
| `numeric_prefix` | 3-digit zero-padded integer (001–043). Must be unique across all spec folders. |
| `short_name` | Kebab-case descriptive name (e.g., `save-config`, `image-storage`). Unchanged by this feature. |
| `first_commit_hash` | SHA of the earliest git commit that added any file inside this folder (before any renames). |
| `first_commit_date` | Author timestamp of `first_commit_hash`. The primary sort key. |

**Validation rules**:
- Prefix must be a gapless sequential integer starting from 001.
- No two folders may share the same prefix.
- Short name must be preserved verbatim across the rename.

### Version Override File

Represents `installer/versioning/version.override.json`.

| Field | Type | Constraint |
|---|---|---|
| `major` | string-encoded integer | Set by maintainer; unchanged in this feature |
| `minor` | string-encoded integer | Incremented from `"4"` to `"5"` by this feature |
| `patch` | string-encoded integer | Must be `"0"` after a minor bump |
| `updatedBy` | string | Maintainer identifier |
| `updatedAtUtc` | ISO 8601 string | Set to the date the bump is performed |

## Rename Operation

The rename operation transforms each Spec Folder's `numeric_prefix` from its current value to its correct chronological value, leaving `short_name` unchanged.

**Input**: 42 existing Spec Folders with current prefixes  
**Output**: 42 Spec Folders with new prefixes forming a gapless sequence 001–042  
**Side effects**: `spec.md` front-matter `Feature Branch` and `Spec Directory` fields updated where they reference the folder's own path; `.specify/feature.json` `feature_directory` updated to `specs/043-reorder-spec-folders`

## State Transitions

```
Spec Folder (current prefix) → rename → Spec Folder (correct prefix)
Version Override File (minor=4) → bump → Version Override File (minor=5)
```
