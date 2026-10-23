# Feature Specification: Fix the Second "Files in use" Dialog in an Interactive Upgrade

**Feature Branch**: `163-fix-files-in-use-dialog`
**Created**: 2026-10-23
**Status**: Draft
**Input**: User description: "Fix the second 'Files in use' dialog in an interactive upgrade. When the owner upgrades GameBot while the bot runs, the Windows Installer shows the 'Files in use' dialog two times. Both dialogs list GameBot.Service and show the same text."

## Background

The owner ran the GameBot installer (bundle `GameBotInstaller.exe`) to upgrade a production install from 1.8.0.243 to 1.11.16.263. The bot was running as a background program with no window. The installer showed two identical "Files in use" dialogs. The owner chose "Close the applications and attempt to restart them" in the first dialog and had to do it again in the second. After the second choice the upgrade ended with success, and the bot ran again with the new build on the same address.

Silent installs show no dialog. For this reason the silent tests did not find the fault.

The cause is not known. These are the possible causes:

- The change schedule of the major upgrade (feature 162, research item R13) makes the old product removal and the new install each check the locked files.
- The old bundle (1.8.0.243) is removed in the same run (the log says "related bundle ... type 'Upgrade'"). This removal can raise one of the dialogs. The owner upgraded one step at a time, so this can happen in any upgrade.
- The bundle starts the installer package two times, or the file list causes a second check.

## Clarifications

### Session 2026-10-23

- Q: If the old bundle removal alone causes the second dialog, can the feature end with a note and no change? → A: No. The owner upgraded one step at a time, so the fault can happen in any upgrade. The feature MUST fix it.
- Q: Does the bot run mode matter? → A: The fault can show only when the bot runs in the background with no console window. All checks MUST cover this mode.

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Find the cause of the second dialog (Priority: P1)

Before any change, the maintainer finds which step raises each "Files in use" dialog. The result decides what change is the smallest one that keeps the rollback.

**Why this priority**: A change without a known cause can break the rollback of feature 162 or have no effect.

**Independent Test**: Run an interactive upgrade of an isolated test copy with a running process that locks `GameBot.Service.exe`. Write a verbose installer log. The log names the step that raises each dialog.

**Acceptance Scenarios**:

1. **Given** an isolated test copy and a running process that locks a file of the test install, **When** the maintainer upgrades with the current schedule, **Then** the log shows which action raises each "Files in use" dialog.
2. **Given** the same setup, **When** the maintainer upgrades with each other schedule option that keeps the rollback, **Then** the log shows how many dialogs each option raises.
3. **Given** the log of the real production upgrade, **When** the maintainer reads the removal of the old bundle, **Then** the notes say if the removal raises one of the dialogs.
4. **Given** the bundle definition, **When** the maintainer reads it, **Then** the notes say if the bundle starts the installer package more than one time or passes a Restart Manager setting.
5. **Given** the old bundle removal raises a dialog, **When** the maintainer upgrades from 1.11.16.x to a newer build, **Then** the notes say how many dialogs show.
6. **Given** the bot runs in the background with no console window, **When** the maintainer compares it with a run that has a console window, **Then** the notes say if the run mode changes the number of dialogs.

---

### User Story 2 - One dialog, one choice (Priority: P1)

The owner upgrades while the bot runs. The owner sees the "Files in use" dialog one time at most. One choice of "Close the applications" closes the bot, finishes the upgrade, and starts the bot again.

**Why this priority**: This is the fault the owner reported.

**Independent Test**: On an isolated test copy, start a bot-like process that locks `GameBot.Service.exe`. Run the interactive upgrade. Count the dialogs. Choose "Close the applications" one time.

**Acceptance Scenarios**:

1. **Given** a running bot that locks its files, **When** the owner runs an interactive upgrade, **Then** the "Files in use" dialog shows at most one time.
2. **Given** the owner chose "Close the applications" in that dialog, **When** the upgrade ends, **Then** the upgrade shows success and the new bot runs again.
3. **Given** the saved host and port before the upgrade, **When** the new bot starts, **Then** it uses the same host and port.

---

### User Story 3 - A failed upgrade still rolls back (Priority: P1)

If the upgrade fails after the new files are in place, the old install comes back as it was. The fix for the dialog must not weaken this.

**Why this priority**: Feature 162 (FR-012) requires the rollback. The fix must keep it.

**Independent Test**: Run the forced-failure check of the test variant script (`-FailAfterInstallFiles`). Compare the state before and after.

**Acceptance Scenarios**:

1. **Given** an installed old product, **When** an upgrade fails after the install of the files, **Then** the old product, its shortcuts, its Run entry, and `network.json` come back.
2. **Given** the fix for the dialog is in place, **When** the same forced failure runs, **Then** the result is the same as before the fix.

---

### User Story 4 - Other upgrade paths keep working (Priority: P2)

A silent upgrade and an upgrade from the Web UI or the API work as before. The updater already waits for the bot to exit.

**Why this priority**: These paths work today. The fix must not break them.

**Independent Test**: Run the existing installer tests and a silent upgrade on an isolated test copy.

**Acceptance Scenarios**:

1. **Given** a running bot, **When** a silent upgrade runs, **Then** the upgrade ends with success and the bot runs again with the saved host and port.
2. **Given** an update started from the Web UI or the API, **When** the updater runs the installer, **Then** the update ends with success.

---

### Edge Cases

- The running process that locks the file is not the bot (for example, an editor or a scanner). The dialog can show more than one time for a process that the installer cannot close. The fix covers the bot only.
- The owner chooses "Do not close the applications" in the dialog. The installer must not show the same dialog again for the same files in the same run.
- The bot is not running. No dialog shows.
- The upgrade starts from an old bundle (for example, 1.8.0.243) or from a recent build. Only one dialog shows in both cases.
- The bot runs in the background with no console window, or with a console window. Only one dialog shows in both modes.
- The owner cancels in the dialog. The old product stays installed and works.

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: An interactive upgrade MUST show the "Files in use" dialog at most one time for one run.
- **FR-002**: A failed upgrade MUST roll back. The old product, its shortcuts, its Run entry, and `network.json` MUST come back (feature 162, FR-012). The change MUST NOT return to the default major-upgrade schedule if this breaks the rollback.
- **FR-003**: After one choice of "Close the applications", the bot MUST close and start again. The new bot MUST keep the saved host and port.
- **FR-004**: A silent upgrade and an upgrade from the Web UI or the API MUST keep working.
- **FR-005**: The feature MUST NOT change the saved binding, the shortcut address, or the dialog pages of feature 162.
- **FR-006**: The maintainer MUST find and record the cause of the second dialog before the change. The record MUST name the action that raises each dialog, and say if the bundle starts the package more than one time or passes a Restart Manager setting.
- **FR-007**: The change MUST be the smallest one that meets FR-001 and FR-002. The record MUST list the options that were checked and why the chosen option won.
- **FR-011**: FR-001 MUST hold for every upgrade, including an upgrade from a very old bundle and an upgrade between recent builds. FR-001 MUST hold when the bot runs in the background with no console window, and when the bot runs with a console window.
- **FR-008**: The feature MUST NOT add custom code that closes the bot in the Web UI update path.
- **FR-009**: The feature MUST NOT change the network page, the port check, or the shortcut rule.
- **FR-010**: All tests of the feature MUST use an isolated test copy of the installer (own upgrade code, temporary folder, temporary port). No test step MUST run the real installer over the production install. A real upgrade of the production install is a separate step that needs approval from the owner.

### Key Entities

- **Interactive upgrade**: An upgrade that the owner runs with the installer window. It can show the "Files in use" dialog.
- **Files in use dialog**: The Windows Installer dialog that lists the programs that lock files. The owner chooses to close them or not.
- **Isolated test copy**: A build of the installer with its own upgrade code, folder, and port. It does not touch the production install.
- **Locking process**: A bot-like process that holds `GameBot.Service.exe` open in the test install. It makes the dialog show.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: In the owner check on an isolated test copy with a running background process (no console window) that locks `GameBot.Service.exe`, an interactive upgrade shows exactly one "Files in use" dialog (not two). The same holds for an upgrade between two recent builds.
- **SC-002**: The forced-failure check (`new-test-variant.ps1 -FailAfterInstallFiles`) restores the old product, shortcuts, Run entry, and `network.json` in 100% of runs.
- **SC-003**: All existing installer tests pass.
- **SC-004**: After one choice of "Close the applications", the owner does not need a second choice or a manual start of the bot, and the new bot listens on the saved host and port.
- **SC-005**: A written record names the cause of the second dialog and the options that were checked.

## Assumptions

- The owner runs the interactive check on the isolated test copy. A silent run cannot show the dialog.
- The test bot runs from a published copy of the branch, not from `installer\wix\payload` (that payload is an old build).
- The Web UI and API update paths already wait for the bot to exit, so they show no dialog.
- The fault can happen in any upgrade, not only in a jump from an old bundle. The fix MUST NOT be limited to a note, even if the old bundle removal is the cause. The check of an upgrade from 1.11.16.x to a newer build shows if the fault also happens between recent builds.
- The fault can depend on the bot run mode. The bot in the background with no console window is the main case, and the test locking process MUST copy it (no window, same executable name).
- The method of the isolated tests follows the notes in `scripts/installer/new-test-variant.ps1` and the project memory notes about the isolated test variant and the registry view of the test shell.

## Out of Scope

- Custom code that closes the bot in the Web UI update path.
- Changes to the network page, the port check, or the shortcut rule.
- A real upgrade of the production install (needs approval from the owner).
