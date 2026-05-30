# Feature Specification: Reorder Spec Folders

**Feature Branch**: `038-reorder-spec-folders`  
**Spec Directory**: `043-reorder-spec-folders`  
**Created**: 2026-05-30  
**Status**: Draft  
**Input**: User description: "This is a maintenance feature, don't change anything in the source code itself. The features that have been created up till now are not ordered properly, there're some features that have all got the number 001. Please find the proper chronological order and rename the folders accordingly. Make sure this feature itself has the correct number AFTER the reordering. Also bump the minor number."

## Context

The `specs/` directory currently contains 42 feature specification folders. Multiple folders share the prefix `001-` and one pair shares the prefix `004-` due to inconsistent numbering over time. A prior renaming effort (PR #45) corrected part of the sequence, but subsequent features were again added with duplicate prefixes. Gaps also exist in the current sequence (014–016 are absent). This feature eliminates all duplicates and gaps by renumbering every folder in strict chronological order of first git commit, and then bumps the application installer minor version to reflect the new feature line.

No source code files (`.cs`, `.ts`, `.vue`, configuration files consumed at runtime) may be modified as part of this feature. Only spec folder renames and the installer version override file are in scope.

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Sequential spec navigation (Priority: P1)

As a developer browsing the `specs/` directory, I can read feature history in chronological order by scanning folder names from `001-` through `043-` with no gaps or duplicate numbers.

**Why this priority**: The primary deliverable. Without correct numbering the entire maintenance effort has no value.

**Independent Test**: List `specs/` and verify every folder name begins with a unique 3-digit prefix, the prefixes form a gapless sequence from `001` to `043`, and the order matches the chronological git creation dates of those folders.

**Acceptance Scenarios**:

1. **Given** the renaming task is complete, **When** a developer runs `ls specs/`, **Then** every entry starts with a unique sequential 3-digit number and no two folders share the same number.
2. **Given** the renaming task is complete, **When** the prefixes are sorted numerically, **Then** the resulting order matches the git-log first-commit dates for each folder (oldest first).
3. **Given** 42 existing specs and 1 new maintenance spec, **When** the renaming is complete, **Then** the sequence runs from `001` to `043` with no gaps.

---

### User Story 2 - Correct self-referential number (Priority: P2)

As a developer reviewing this maintenance spec, I can confirm that its folder `043-reorder-spec-folders` is the 43rd entry in the final sequence, consistent with the 42 pre-existing specs renumbered 001–042.

**Why this priority**: Validates that the numbering logic accounts for all pre-existing specs rather than only the highest visible prefix.

**Independent Test**: Count all spec folders after renaming and confirm this spec's prefix equals that count.

**Acceptance Scenarios**:

1. **Given** all pre-existing spec folders renumbered, **When** they are counted, **Then** the count is 42 and this spec folder carries prefix `043`.
2. **Given** the feature directory entry in `.specify/feature.json`, **When** it is read, **Then** the `feature_directory` field is `specs/043-reorder-spec-folders`.

---

### User Story 3 - Minor version bump (Priority: P2)

As a maintainer, I can confirm the application installer minor version has been incremented to reflect this new feature line.

**Why this priority**: Keeps the release versioning aligned with the feature count; every new feature line warrants a minor bump.

**Independent Test**: Read `installer/versioning/version.override.json` and confirm `minor` is `"5"` and `patch` is `"0"`.

**Acceptance Scenarios**:

1. **Given** the version override file before this feature, **When** the minor version bump task is applied, **Then** `minor` changes from `"4"` to `"5"` and `patch` remains `"0"`.
2. **Given** the bumped version file, **When** a build is produced, **Then** the reported application version is `0.5.0.<Build>`.

---

### Edge Cases

- What if two spec folders have first-commit timestamps that fall in the same git commit (e.g., both files were added together)? → Folders committed together are ordered alphabetically within that commit as a tie-break.
- What if `.specify/feature.json` points to a folder being renamed? → It must be updated to the new path as part of the renaming task.
- What if a spec folder's internal documents cross-reference its own folder name (e.g., in `spec.md` front-matter)? → Internal cross-references are spec metadata, not source code, so updating them is in scope and should be done.

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: All 42 pre-existing spec folders MUST be renamed so their 3-digit numeric prefix reflects strict chronological order of first git commit, starting from `001`.
- **FR-002**: Folders that currently share a numeric prefix MUST be disambiguated: the folder whose first commit is older receives the lower number.
- **FR-003**: The final sequence MUST be gapless from `001` through `043`, with `043-reorder-spec-folders` as the last entry.
- **FR-004**: The `.specify/feature.json` file MUST be updated so `feature_directory` references the final resolved path of this spec (`specs/043-reorder-spec-folders`).
- **FR-005**: The installer minor version in `installer/versioning/version.override.json` MUST be incremented from `"4"` to `"5"`; `patch` MUST remain `"0"`.
- **FR-006**: No `.cs`, `.ts`, `.vue`, or runtime-consumed configuration files MUST be modified.
- **FR-007**: Internal spec metadata that references a folder's own path (e.g., `Feature Branch` or `Spec Directory` fields in `spec.md` front-matter) SHOULD be updated to reflect the new folder name.

### Key Entities

- **Spec Folder**: A directory under `specs/` whose name follows the pattern `NNN-short-name`. Identified by its numeric prefix and short descriptive name.
- **First Commit**: The earliest git commit (by author date) that introduced any file within a given spec folder. Used as the sole ordering key.
- **Version Override File**: `installer/versioning/version.override.json` — the checked-in file that sets `Major`, `Minor`, and `Patch` for the installer build.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: After the renaming task completes, `ls specs/` returns exactly 43 entries (excluding non-folder files such as `openapi.json`), each with a unique numeric prefix in the range `001`–`043`.
- **SC-002**: The order of folder prefixes matches the chronological sequence of first git commits with 100% accuracy (zero out-of-order folders).
- **SC-003**: The installer version override reports `minor = 5` and `patch = 0` after the bump task completes.
- **SC-004**: No build, lint, or test failures are introduced; the change set contains only renamed spec directories, updated spec metadata within those directories, and the version override file.

## Assumptions

- **A-001**: The 42 spec folders counted at the time of spec creation (2026-05-30) constitute the complete set of pre-existing specs. Any spec folder added after this date is out of scope.
- **A-002**: "First commit" is determined using `git log --diff-filter=A -- specs/<folder>/` and taking the oldest result by commit date.
- **A-003**: Alphabetical order within the same commit is an acceptable tie-breaking rule.
- **A-004**: The branch name (`038-reorder-spec-folders`) was auto-generated before the correct count was known; it need not match the spec directory number (`043`), as branch name and spec directory are independent.
