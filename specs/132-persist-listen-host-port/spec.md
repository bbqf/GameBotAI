# Feature Specification: Persist the Listen Host and Port

**Feature Branch**: `132-persist-listen-host-port`
**Created**: 2026-10-09
**Status**: Implemented
**Input**: User description: "Persist the listen host and port across upgrades and reinstalls (GitHub issue #65, 'Upgrade always resets the listen host:port to localhost:8080')."

## Clarifications

### Session 2026-10-09

- Q: Which value wins when the installer gets a host or port option and a saved value exists? -> A: The option wins and it is saved. Reason: FR-007 says an on-purpose choice replaces the saved value.
- Q: What happens when the saved port is now used by another program? -> A: Existing port conflict handling applies. The port is not changed silently. Reason: a silent change breaks clients that use the known address.
- Q: Does reinstall into a different data directory keep the values? -> A: No. Only the same data directory keeps them. Reason: the issue limits scope to the same data directory.
- Q: Are values from configuration or environment variables saved? -> A: No. Only values the user chose in the installer are saved. Reason: those sources are runtime overrides with higher priority.
- Q: Does the uninstall remove the saved setting? -> A: No, when the data directory is kept. Reason: scenario c needs the value after uninstall.

### Session 2026-10-09 (loop 1, from analyze)

- Q (F1): Must the saved setting survive a change of Windows account? -> A: Yes, when the install and data directory stay the same. The saved setting MUST be found by the data directory path, not by user account. The data directory MUST NOT depend on the account that runs the update. If the default data directory is per account, the update MUST use the data directory of the existing install. Reason: FR-004.
- Q (F8): What if only one of host and port is saved? -> A: Each value resolves on its own. A saved value for one does not affect the other. Reason: the two values are independent.
- Q (F5): Does the shortcut check apply to all three scenarios? -> A: Yes. FR-008 and SC-002 apply to all three scenarios, and each scenario has a check.
- Q (F4): Which proof is needed for SC-001, SC-002, SC-004? -> A: Automated tests prove the rules (file and registry read, option priority, no forwarding of runtime overrides). A manual run on a real MSI install proves the three scenarios and MUST be done before merge or recorded as not possible. Reason: no automated MSI run exists in CI.
- Q (F10): Where is the invalid-value log required? -> A: In the service log. The installer MAY write a debug line. Reason: the installer has limited logging.
- Q (F9): Is SC-003 a separate measure? -> A: No. It is met when SC-001 and SC-002 are met.
- Plan-level items F2 (data path available when the installer reads the file) and F3 (the write step must be safe on rollback or cancel: a cancelled or rolled-back install MUST NOT change the saved setting) are requirements for the plan. They are recorded as FR-016 and FR-017.

### Session 2026-10-09 (loop 2, from analyze 2)

- Q (U4): Does FR-008 need the same host text in the shortcut? -> A: No. The shortcut MUST use the same port and a host that reaches the service (`localhost` for `127.0.0.1` and `0.0.0.0`, as the installer does today). Reason: a shortcut cannot open `0.0.0.0`.
- Q (U1): What if the `config` folder does not exist in a new data directory? -> A: The write step MUST create it before it writes the file.
- Q (U2): What must rollback do after a partial write? -> A: Rollback MUST restore the old file or the old "no file" state and MUST remove any temporary file. Rollback and commit receive the data path.
- Q (U3): Does the installer log ignored values? -> A: It writes a debug line for each ignored value. It does not fail.
- Q (C1/T4): Is a source test enough for the installer logic? -> A: No. The installer script functions (read, validate, write, rollback, commit) MUST also have executable tests that run them against a temporary folder. Reason: source-text tests do not prove behavior.
- Q (D1): Are the project docs in scope? -> A: Yes. The change MUST update `docs/architecture.md` (persistence layout, address priority, updater no longer forwards overrides) with a new "Last reviewed" date, set this spec Status to Implemented when done, and add a row for feature 132 to `specs/STATUS.md`. Reason: constitution Principle V.
- Q (D2): Order of tests and code? -> A: For each fix, the failing test is written and shown to fail before the fix. Reason: constitution Principle II.
- Q (I1, T1-T3): Task ID and ordering errors? -> A: Tasks and plan MUST use correct task IDs, correct dependencies and parallel marks, and a quickstart order V-1 to V-9. V-9 MUST say how the failing test MSI is made or allow "not possible" with a reason. T001 (read documents) is removed or marked as a non-work step.

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Keep host and port after an update from the Web UI (Priority: P1)

An operator chose a listen host and port when they installed GameBot. Later the operator starts an update from the Web UI (feature 131). After the update, the service must listen on the same host and port as before. The operator does not need to set them again.

**Why this priority**: This is the path most users use. A reset makes the Web UI unreachable at the known address, and other devices that use the old address lose their connection.

**Independent Test**: Install with a non-default host and port. Run the update from the Web UI. Check that the service listens on the chosen host and port and that the Web UI shortcut opens the same port on a host that reaches the service.

**Acceptance Scenarios**:

1. **Given** an install with host `0.0.0.0` and port `9090`, **When** the operator completes an update from the Web UI, **Then** the service listens on `0.0.0.0:9090`.
2. **Given** the same install, **When** the update ends, **Then** the Web UI shortcut URL uses the same port as the service and a host that reaches it (`localhost` for `127.0.0.1` and `0.0.0.0`).
3. **Given** an install that uses the default values, **When** the operator completes an update from the Web UI, **Then** the service still listens on `127.0.0.1:8080`.

---

### User Story 2 - Keep host and port after a manual update (Priority: P1)

An operator runs a newer installer by hand over an existing install. The installer must find the host and port of the existing install and keep them.

**Why this priority**: Manual update is a normal supported path and it has the same defect as the update from the Web UI.

**Independent Test**: Install with a non-default host and port. Run the newer installer without any host or port option. Check the service address and the shortcut URL.

**Acceptance Scenarios**:

1. **Given** an install with a non-default host and port, **When** the operator runs a newer installer without host or port options, **Then** the service listens on the same host and port.
2. **Given** the same install, **When** the operator runs a newer installer and gives a new host or port option on purpose, **Then** the service uses the new value and keeps it for later updates.

---

### User Story 3 - Keep host and port after uninstall and install again (Priority: P2)

An operator removes GameBot and installs it again into the same data directory. The new install must use the host and port from before. The operator does not need to choose them again.

**Why this priority**: It is a less common path, but the data directory is kept, so the operator expects its settings to be kept too.

**Independent Test**: Install with a non-default host and port. Uninstall. Install again with the same data directory and no host or port option. Check the service address.

**Acceptance Scenarios**:

1. **Given** an uninstalled GameBot with a kept data directory that holds a non-default host and port, **When** the operator installs again into that data directory, **Then** the service listens on the saved host and port.
2. **Given** a new data directory with no saved values, **When** the operator installs, **Then** the first-install defaults and port-selection rules apply.

---

### Edge Cases

- The saved values exist only in the older storage place from a previous version (for example the installer registry values). The upgrade must still read them and keep them.
- The update runs under a different user account, or in an elevated context, than the account that did the first install. The saved values must still be found.
- The saved port is now in use by another program. The existing conflict handling for ports applies and the service reports a clear error. It does not silently change the port.
- The saved values are damaged or not valid (for example text in the port). The system ignores them, uses the next source in the priority order, and writes a log message.
- A host or port is set by configuration or by environment variable. These sources keep their higher priority than the saved values.
- The data directory is kept but the saved file is missing. The system falls back to the older storage place, then to the defaults.

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: The system MUST keep the listen host and port that the user set across an update started from the Web UI.
- **FR-002**: The system MUST keep the listen host and port across a manual install of a newer version over an existing install, when the user gives no host or port option.
- **FR-003**: The system MUST keep the listen host and port across uninstall followed by install into the same data directory, when the user gives no host or port option.
- **FR-004**: The system MUST store the chosen host and port in a place that is not removed by update, by uninstall, or by a change of the Windows user or elevation context. The plan chooses the place.
- **FR-005**: The system MUST still read the older installer registry values as a fallback when the new storage place has no value.
- **FR-006**: The system MUST keep the existing priority order for a value: configuration first, then environment variable, then the saved value, then the default.
- **FR-007**: A host or port that the user gives on purpose to the installer MUST replace the saved value and MUST be saved for later updates.
- **FR-008**: After each of the three scenarios, the Web UI shortcut URL MUST use the same port as the running service and a host that reaches it.
- **FR-009**: The system MUST ignore a saved value that is not valid, use the next source, and write a log message that names the problem. The service writes the log message. The installer MAY write a debug line for each ignored value and MUST NOT fail.
- **FR-010**: The default values (`127.0.0.1` and `8080`) for a first install MUST NOT change.
- **FR-011**: The port-selection rules for a first install MUST NOT change.
- **FR-012**: The change MUST NOT add a new user interface for the host and port.
- **FR-013**: The change to the update feature (feature 131) MUST be limited to what is needed to keep the host and port.
- **FR-014**: The system MUST have automated tests for each of the three scenarios where the code allows it.
- **FR-015**: User documentation (`INSTALL.md`) MUST be updated only if user-visible behavior changes.
- **FR-016**: The installer MUST know the data directory path of the existing or target install before it reads the saved setting, so a reinstall into a custom folder finds the saved setting.
- **FR-017**: A cancelled or rolled-back install or update MUST NOT change the saved setting.
- **FR-018**: The saved host and the saved port MUST resolve on their own. A missing or invalid value for one MUST NOT block the other.
- **FR-019**: The installer script functions that read, validate, write, roll back, and commit the saved setting MUST have executable tests that run the functions against a temporary folder. Tests that only check source text are not enough.
- **FR-020**: The change MUST update `docs/architecture.md` (persistence layout, address priority, and that the updater no longer forwards overrides) with a new "Last reviewed" date. When the work is done, the Status of this spec MUST be set to Implemented, and a row for feature 132 MUST be added to `specs/STATUS.md`.
- **FR-021**: The write step MUST create the `config` folder in the data directory when it does not exist. The rollback step MUST restore the old file or the old "no file" state and MUST remove any temporary file. Rollback and commit MUST receive the data path.
- **FR-022**: For each fix, the failing test MUST be written and shown to fail before the fix is made.

### Key Entities

- **Persisted network setting**: The pair of listen host and listen port that the user chose. It has a source (saved file, older registry value, configuration, environment variable, or default) and it survives update, uninstall, and reinstall into the same data directory.
- **Installation context**: The user account, elevation level, and data directory that were used for an install or update. The saved setting must be found independent of the account and elevation level.

## Assumptions

- The data directory is the same before and after an update, and the user selects the same directory on a reinstall.
- The service runs with enough rights to read the data directory that holds the saved setting.
- The storage place and the exact root cause for each scenario are confirmed in the plan.
- Values given by configuration or by environment variable are not saved by this feature.

## Planning Notes

These notes are rules for the plan and for the task list. They do not add new requirements.

- **Task rules (F6, F7)**: Tasks that edit the same file MUST NOT be marked parallel. Parallel examples in the task list MUST respect the listed dependencies. A task MUST NOT be shown as parallel with a task it depends on.
- **Data path at read time (F2, F3)**: The plan MUST name the exact MSI property that gives the data path when the installer reads the saved file. The plan MUST name the action sequence position that makes this property valid. The property APPLICATIONFOLDER is resolved by CostFinalize, so a read before CostFinalize is not valid. Research and plan MUST use one property name for the data path in all places.
- **Write step (F3)**: The write of the saved setting MUST be a deferred custom action. It MUST use CustomActionData to get its values. It MUST be safe on rollback and cancel (see FR-017).
- **Account change (F1)**: The plan MUST say clearly when the saved file survives a change of account: it survives when the data path is the same. The default data root is under %LOCALAPPDATA%, which is different for each account. For this reason, the update MUST target the data directory of the existing install. The quickstart MUST include a step for the case where the update runs under a different account.
- **Manual MSI check (F4)**: The task list MUST include the manual check on a real MSI install as a required task before merge. If the check is not possible, the result MUST be recorded as not possible, with the reason.
- **IDs and order (I1, T1-T4)**: The plan and the task list MUST use correct task IDs, correct dependencies, and correct parallel marks. The quickstart MUST use the order V-1 to V-9. V-9 (the failing test MSI) MUST say how the MSI is made, or MUST allow "not possible" with a reason. The read-documents step (T001) MUST be removed or marked as a non-work step.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: In 100% of test runs of the three scenarios (update from the Web UI, manual update, uninstall and reinstall) with a non-default host and port, the service listens on the same host and port after the scenario.
- **SC-002**: In 100% of those runs, the Web UI shortcut URL uses the same port as the running service and a host that reaches it (see FR-008).
- **SC-003**: An operator does not need to do any manual step to restore the host and port after an update or a reinstall.
- **SC-004**: Installs from older versions, which have only the older saved values, keep their host and port after an update in 100% of test runs.
- **SC-005**: A first install with no saved values and no options still listens on `127.0.0.1:8080` and follows the existing port-selection rules.
