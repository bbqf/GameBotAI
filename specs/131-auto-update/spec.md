# Feature Specification: Auto-Update

**Feature Branch**: `131-auto-update`
**Created**: 2026-10-09
**Status**: Draft
**Input**: User description: "I want an auto-update feature for the bot. The packages will be built in the github. Suggest a way and ask me questions how to get what I want. The usual path should be: User downloads installer for the first time, then user clicks the "Check for Update" button in the UI, then the update gets installed. Note, Windows loves to ask questions about the unsigned code, so these queries should be avoided if possible."

## Suggested Approach *(summary for review)*

- The GitHub build publishes each installer as a release. The release has a checksum and a version number.
- The installed bot reads that release list when the user clicks "Check for Update".
- If a newer version exists, the user confirms. The bot downloads the installer and checks the checksum. Then it runs the installer silent.
- The bot downloads the file itself, not through a web browser. Windows does not mark the file as "from the internet". This avoids most "unknown publisher" prompts.
- The bot restarts on the new version. Games, sequences, queues, images and settings stay as they are.

## Clarifications

### Session 2026-10-09

- Q: Who may start an update? → A: Only requests from the same PC can install an update. Other users can check for an update but not install.
- Q: What happens to active queues? → A: The user confirms after a warning. The bot stops all queues at once, updates, and restarts. Queues with "resume after service start" turned on start again.
- Q: What happens when the bot does not run from an installed folder? → A: The bot refuses to install. "Check for Update" still works. The UI says "Update works only for an installed bot".
- Q: Does the UI show the release notes? → A: Yes. The UI shows the release notes as plain text next to the new version number. If the release has no notes, the UI shows nothing extra.
- Q: Which error comes first when a request is remote and the bot is not installed? → A: The remote error comes first. A remote user learns nothing about the install state.

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Update from the UI in a few clicks (Priority: P1)

A user has the bot installed and a newer version exists. The user opens the UI and clicks "Check for Update". The user sees the new version number and confirms. The bot updates and comes back on its own.

**Why this priority**: This is the full path that the user asked for. Without it, the feature has no value.

**Independent Test**: Install an older build. Publish a newer build. Click "Check for Update" and confirm. The UI shows the new version afterwards. The data from before is still there.

**Acceptance Scenarios**:

1. **Given** an older version is installed and a newer release exists, **When** the user clicks "Check for Update", **Then** the UI shows both versions. It shows the release notes if the release has notes. It also shows an "Install update" action.
2. **Given** the user confirms the update, **When** the update runs, **Then** the UI shows progress (download, install, restart). The bot returns on the new version with no other manual step.
3. **Given** the update is done, **When** the user opens the UI, **Then** all games, sequences, queues, images and settings from before the update are present.
4. **Given** the update runs, **When** it downloads and installs, **Then** the user sees no Windows "unknown publisher" or SmartScreen prompt from the update step.

---

### User Story 2 - Already up to date and offline cases (Priority: P2)

A user clicks "Check for Update" when no newer version exists. Or the release list is not reachable.

**Why this priority**: The button must give a clear answer in every state. The happy path is not enough.

**Independent Test**: Click the button on the newest version. Then click it again with no network.

**Acceptance Scenarios**:

1. **Given** the newest version is installed, **When** the user clicks "Check for Update", **Then** the UI says the bot is up to date. It shows the version.
2. **Given** the release list is not reachable, **When** the user clicks the button, **Then** the UI shows a clear error. The bot does not change.

---

### User Story 3 - Safe update when queues run (Priority: P2)

A user updates while queues or a sequence are active.

**Why this priority**: The bot automates games. An update in the middle of a run must not damage state. It must not lose scheduled work.

**Independent Test**: Start a queue, click update, and confirm. Check that the queue state is correct after the restart.

**Acceptance Scenarios**:

1. **Given** a queue is active, **When** the user clicks "Install update", **Then** the UI warns that all active queues stop at once. The update starts only after the user accepts. The update does not wait for queues to become idle.
2. **Given** a queue has "resume after service start" turned on, **When** the update is done, **Then** that queue resumes, as after a normal restart.

---

### User Story 4 - A failed update does not break the bot (Priority: P3)

The download is damaged, the checksum does not match, or the install fails.

**Why this priority**: A broken update that leaves no bot that works is worse than no update feature.

**Independent Test**: Serve an installer with a wrong checksum. The update must stop. The old version must still work.

**Acceptance Scenarios**:

1. **Given** the downloaded file does not match its published checksum, **When** the bot checks the file, **Then** the bot does not run the file. The UI shows an error. The old version still works.
2. **Given** the install fails part way, **When** the bot detects the failure, **Then** the previous version works again. The UI or a log tells the user what happened.

---

### Edge Cases

- The user clicks "Check for Update" twice quickly: only one check or update runs.
- The release list has a pre-release or an older version: the bot does not offer a downgrade.
- The disk has too little free space for the installer: the UI shows a clear error before any change.
- The network stops during the download: the bot deletes the partial file and shows a clear error. The old version still works.
- Security software or a Windows policy blocks the installer: the update fails and the old version still works. The UI shows the exit code and the log path. The installer is per-user, so it does not need administrator rights.
- A developer starts the bot from source, so it does not run from an installed folder. The bot refuses "Install update" with a clear message. "Check for Update" still works.
- The user closes the UI during the update: the update still finishes.
- A user on another PC clicks "Install update": the bot refuses the request. Nothing changes on the bot PC.
- The user downloads the installer with a web browser for the first install: Windows can show a SmartScreen warning. See FR-010.

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: The UI MUST show a "Check for Update" button and the installed version.
- **FR-002**: The system MUST compare the installed version with the newest published version. The system MUST show the result to the user.
- **FR-003**: The system MUST NOT install an update without an explicit user confirmation.
- **FR-004**: The system MUST download the installer itself. The system MUST check the installer against a published checksum before it runs the installer.
- **FR-005**: The system MUST run the installer silent, with no user input. The update step MUST NOT cause Windows "unknown publisher" or SmartScreen prompts.
- **FR-006**: The system MUST show update progress and the final result in the UI. The UI MUST show the result also after the bot restarts.
- **FR-007**: The system MUST keep all user data through an update. User data means games, sequences, queues, images, settings, and logs.
- **FR-008**: The system MUST NOT offer a version that is older than or equal to the installed version.
- **FR-009**: On any failure (network, checksum, install), the system MUST leave a bot that works. The system MUST show a clear error.
- **FR-010**: For each update-eligible build, the GitHub build MUST publish three items. These are the installer, its version number, and its checksum. The installed bot MUST be able to read them. The first download of the installer in a web browser is outside the update step. The installer stays unsigned. The user accepts one Windows SmartScreen warning at the first install ("More info", then "Run anyway"). The documentation MUST describe this step.
- **FR-011**: Only builds that the owner marks as a release are update-eligible. The owner can use a version tag or a manual workflow run. A normal merge to master MUST NOT make a build visible to "Check for Update". There is one channel only.
- **FR-012**: The installed bot MUST read the release list from the public GitHub Releases of the project repository. It MUST NOT need a login token.
- **FR-013**: Before the update starts, the system MUST warn the user that all active queues and sequences stop at once. The system MUST NOT wait for them to become idle. After the restart, queues with "resume after service start" turned on MUST start again.
- **FR-014**: The system MUST accept an "Install update" request only from the PC that runs the bot. The system MUST refuse a request from another PC with the message "Install from the bot PC". "Check for Update" stays open to all UI users.
- **FR-015**: The system MUST refuse "Install update" when the bot does not run from an installed folder. The refusal MUST show the message "Update works only for an installed bot". "Check for Update" MUST still work in this case. If a request is remote and the bot is not installed, the remote refusal (FR-014) comes first.
- **FR-016**: When an update is available, the UI MUST show the release notes of the new version as plain text. The UI MUST NOT run or render markup from the notes. If the release has no notes, the UI MUST NOT show an empty notes area.

### Key Entities

- **Release**: A published build. It has a version number, release date, installer file, checksum, and optional notes.
- **Update Check Result**: The installed version, the newest version, and one status. The status is up to date, update available, or check failed.
- **Update Attempt**: One run of an update. It has a state (download, verify, install, restart, succeeded, failed). It has an error message if it failed.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: A user with an installed older version reaches the newest version with no more than 3 clicks after the UI opens. The clicks are check, install, and confirm.
- **SC-002**: The update step shows 0 Windows security prompts on a standard PC that has the bot installed.
- **SC-003**: After an update, 100% of the games, sequences, queues, images and settings from before are present.
- **SC-004**: The update finishes and the UI works again within 5 minutes on a normal home network.
- **SC-005**: In 100% of tested failure cases, the old version still works afterwards. The cases are bad checksum, no network, and install error.

## Assumptions

- The bot is installed with the current Windows installer. It runs as a background app with a web UI.
- The current GitHub installer build is the source of update packages. No new build host is added.
- Windows only. One update channel (no beta channel) in the first version.
- Automatic background updates with no user click are out of scope for the first version. The user starts every update.
- The GitHub repository is public.
- Code signing is not available now. The update step does not need a signature.
