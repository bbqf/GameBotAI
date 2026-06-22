# Feature Specification: Authoring Backup & Restore

**Feature Branch**: `057-authoring-backup-restore`  
**Created**: 2026-06-06  
**Status**: Implemented
**Input**: User description: "I want to be able to backup up and restore commands and sequences, including all necessary images, etc. The functionality should be available via UI, but the actual composition should be done on the server side. I want the backup to be provided as a downloadable zip archive. When restoring the archive and objects with the same names already existing, I want to be asked, if I want to overwrite the existing objects (so probably some kind of dry-run would be needed on the server-side). The only way to restore if the objects already exist is to override them, no need to rename or anything else. I guess the backup/restore could be put under Authoring itself, as it's the area that is being affected."

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Select objects and download a backup archive (Priority: P1)

A user navigates to the Authoring section, selects the specific commands and/or sequences they want to back up, triggers the backup, and receives a downloadable zip file containing the selected objects and any images they reference.

**Why this priority**: Backup is the foundation of this feature; without it restore is impossible. It also delivers standalone value as a data portability and disaster-recovery tool.

**Independent Test**: Select a subset of commands and sequences in the Authoring UI, download the zip, unzip it, and verify it contains exactly the selected objects and their referenced images — and no others.

**Acceptance Scenarios**:

1. **Given** the user is in the Authoring section, **When** they select one or more commands and/or sequences and click "Download Backup", **Then** the browser downloads a zip archive containing only the selected objects and their referenced images.
2. **Given** the downloaded zip, **When** it is inspected, **Then** it contains exactly the selected commands, selected sequences, all images referenced by those objects, and any commands transitively referenced by selected sequences.
3. **Given** no commands or sequences exist, **When** the user opens the backup selector, **Then** the selector shows an empty state with a clear message and the backup action is disabled.

---

### User Story 2 - Restore a backup with no conflicts (Priority: P1)

A user uploads a previously downloaded backup zip. The server performs a dry-run and finds no name conflicts. The user confirms and all objects are imported successfully.

**Why this priority**: The core restore path (conflict-free) must work before conflict handling is introduced. Together with backup, it forms the minimum viable round-trip.

**Independent Test**: On a clean instance (no existing commands/sequences), upload a known backup archive and verify all objects are recreated with matching data and images.

**Acceptance Scenarios**:

1. **Given** no existing commands or sequences share names with objects in the backup, **When** the user uploads the archive and confirms, **Then** all commands, sequences, and images are restored and appear in their respective lists.
2. **Given** a successful restore, **When** the user browses commands and sequences, **Then** each restored object contains the same steps, references, and images as the original.

---

### User Story 3 - Restore a backup with name conflicts (Priority: P1)

A user uploads a backup archive. The server dry-run detects that one or more objects in the archive share names with existing objects. The UI presents a clear conflict report and asks whether to overwrite. If the user confirms, conflicting objects are overwritten; if the user cancels, nothing is changed.

**Why this priority**: Without conflict resolution the restore operation is unsafe — it could either silently corrupt existing work or fail with no explanation.

**Independent Test**: Restore a backup on an instance that already contains at least one command or sequence with the same name as an object in the archive. Verify the conflict is surfaced, overwrite confirmation is required, and after confirmation the existing object reflects the restored data.

**Acceptance Scenarios**:

1. **Given** the backup contains a command named "AttackSequence" and a command with that name already exists, **When** the user uploads the archive, **Then** the UI shows a conflict report listing the conflicting names before asking for confirmation.
2. **Given** the conflict report is shown, **When** the user cancels, **Then** no data is modified and the user is returned to the Authoring section unchanged.
3. **Given** the conflict report is shown, **When** the user confirms the overwrite, **Then** all conflicting objects are replaced with the versions from the archive, and non-conflicting objects are created fresh.
4. **Given** the user confirms overwrite, **When** an image referenced by a conflicting object already exists, **Then** it is also overwritten with the version from the archive.

---

### Edge Cases

- What happens if the user attempts to download a backup without selecting any objects? The backup action is disabled until at least one command or sequence is selected.
- What happens when the uploaded file is not a valid backup archive? The system rejects it with a clear error message and does not modify any data.
- What happens if the backup archive is from a different or incompatible version? The server reports a version mismatch error; no partial import occurs.
- What happens if the archive is very large? The system accepts it but may take longer; the UI indicates progress or a loading state.
- What happens if the server encounters an error mid-restore after overwrite begins? The entire restore operation is rolled back — no objects or images are modified — and the UI reports the failure with an explanatory message.
- What happens if images referenced in the manifest are missing from the archive? The server reports which images are missing; the restore is aborted with an explanatory error.

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: The Authoring section MUST expose a "Backup" action that allows the user to select specific commands and/or sequences to include; the server assembles a zip archive containing the selected objects, all images they reference, and any commands transitively referenced by selected sequences.
- **FR-002**: The server MUST compose the backup archive entirely on the server side; the browser only receives and downloads the completed archive.
- **FR-003**: The zip archive MUST include a manifest describing the archive contents and format version to support validation during restore.
- **FR-004**: The Authoring section MUST expose a "Restore" action that accepts a zip archive uploaded by the user.
- **FR-005**: Before applying any changes, the server MUST perform a dry-run that compares archive objects against existing objects (commands and sequences by name; images by identifier) and returns a conflict report listing any matches.
- **FR-006**: If the dry-run finds no conflicts, the UI MUST ask the user to confirm the restore before committing.
- **FR-007**: If the dry-run finds conflicts, the UI MUST present the conflict report (listing conflicting names) and ask the user to confirm an overwrite or cancel.
- **FR-008**: If the user cancels at the confirmation step, the server MUST NOT modify any existing data.
- **FR-009**: If the user confirms (with or without conflicts), the server MUST restore all objects from the archive, overwriting any existing objects that share a name.
- **FR-010**: Restored images MUST also overwrite existing images of the same identifier when conflicts are confirmed.
- **FR-011**: The server MUST validate the uploaded archive format before performing the dry-run; invalid or unrecognisable archives MUST be rejected with an explanatory error and no data changes.
- **FR-012**: Backup and Restore controls MUST be accessible via a dedicated "Backup & Restore" entry in the Authoring navigation menu, opening its own page or panel with enough space for the multi-step restore workflow.
- **FR-013**: The UI MUST provide clear feedback for each phase: backup generation in progress, download ready, restore upload in progress, conflict report, confirmation prompt, restore applying, and restore complete.
- **FR-014**: The restore operation MUST be as atomic as possible: if any error occurs during the apply phase, all changes are rolled back and the existing dataset is left unmodified. Due to file-based storage constraints without transactional support, image rollback is best-effort — the system re-saves pre-loaded originals and reports any rollback failure explicitly to the user.

### Key Entities

- **Backup Archive**: A zip file produced by the server containing a manifest, serialised definitions for the user-selected commands and sequences (plus any commands transitively referenced by selected sequences), and all referenced image binary files.
- **Backup Manifest**: A metadata document inside the archive listing format version, creation timestamp, and the names/counts of all included object types.
- **Conflict Report**: A server-generated list of object names from the archive that collide with names of existing objects; returned as part of the dry-run response.
- **Command**: An authoring object with a name and steps; may reference images.
- **Sequence**: An ordered collection of command references with a name.
- **Image**: A binary asset referenced by command steps (e.g., image-match or wait-for-image steps).

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: Users can download a backup archive for any selection of commands and sequences, including their referenced images, in under 30 seconds for a typical authoring dataset.
- **SC-002**: A conflict-free restore completes and all objects are visible in the Authoring lists within 30 seconds of the user confirming.
- **SC-003**: When conflicts exist, 100% of conflicting object names are surfaced in the conflict report before any data is modified.
- **SC-004**: Cancelling at the confirmation step results in zero changes to existing data (verified by comparing state before and after).
- **SC-005**: 95% of users can complete the full backup-and-restore round-trip without support assistance, guided solely by the UI. *(Post-launch metric — validated via user testing; no automated coverage.)*

## Clarifications

### Session 2026-06-06

- Q: If an error occurs mid-restore after overwrite has begun, should the restore roll back all changes (atomic) or apply as many objects as possible and report failures (best-effort)? → A: Fully atomic — all changes are rolled back on any failure; the dataset is left unmodified.
- Q: Should backup cover all commands and sequences always, or should the user be able to select specific objects to include? → A: Selective — the user picks which commands and/or sequences to include; referenced images and transitively referenced commands are included automatically.
- Q: Where in the UI should Backup & Restore controls be located within the Authoring section? → A: A dedicated "Backup & Restore" entry in the Authoring navigation menu, opening its own page/panel.

## Assumptions

- All images required by commands and sequences are already stored on the server and accessible for inclusion in the backup.
- "Same name" is the conflict-detection key for both commands and sequences; name comparison is case-sensitive and exact.
- Restore is as atomic as possible: either all objects and images from the archive are applied, or — if any error occurs — changes are rolled back. Due to file-based storage without transactions, command and sequence rollback uses temp-stage-then-rename (reliable); image rollback re-saves pre-loaded originals via the repository (best-effort). Any rollback failure is reported explicitly to the user.
- The backup covers commands and sequences only; other authoring objects (games, triggers, standalone actions) are out of scope for this feature.
- No access-control or permission gating is required beyond what the Authoring section already enforces.
- Archive size limits, if any, are enforced by existing infrastructure (e.g., upload size limits); this feature does not introduce new limits.
