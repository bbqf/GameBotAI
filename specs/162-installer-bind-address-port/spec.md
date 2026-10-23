# Feature Specification: Installer Bind Address and Port Control

**Feature Branch**: `162-installer-bind-address-port`
**Created**: 2026-10-23
**Status**: Implemented (owner dialog checklist pending)
**Input**: User description: "Fix the installer. Every local install or upgrade must show an option to set the interface and port. New installs default to localhost and the port from the current selection mechanism. Upgrades keep the interface and port by default. Start-Menu shortcuts must point to the host and port in use. Upgrades started from the UI or API must keep the binding and the Start-Menu shortcuts." Extension: "Clean up the variables used for the host and port in the configuration, as there are duplicates now."

## Clarifications

### Session 2026-10-23

- Q: Saved port is in use during a silent upgrade? → A: The upgrade succeeds, keeps the saved port, logs a warning; the bot reports the bind failure.
- Q: Shortcut host for a non-loopback binding? → A: `localhost` for loopback; the computer name for all other values.
- Q: Runtime override against the saved value? → A: The override wins for the bot; the installer ignores and does not save it; the bot status shows the source.
- Q: Which store is the one saved location? → A: The file `config\network.json`; the registry copy is migrated once and removed.
- Q: What does the bot report when it cannot bind the address? → A: An error in its log (host, port, source) and a non-zero exit code. A status page is not possible.
- Q: What does the port check count as "in use"? → A: A port that a program listens on, except a port held by the installed GameBot service itself.

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Choose interface and port on every local install or upgrade (Priority: P1)

An operator runs the installer on their PC. The installer always shows the interface and port choice, for a new install and for an upgrade. The operator can accept the shown values or change them.

**Why this priority**: Today the choice is not always shown. The operator cannot set the binding when they need to. All other stories depend on this choice.

**Independent Test**: Run the installer in interactive mode on a clean machine and again on a machine with a current install. Check that the choice shows both times.

**Acceptance Scenarios**:

1. **Given** no GameBot is installed, **When** the operator runs the installer interactively, **Then** the installer shows an interface and port choice.
2. **Given** GameBot is installed, **When** the operator runs the installer interactively to upgrade, **Then** the installer shows the same choice.
3. **Given** the operator enters a port that is not valid or is in use, **When** they continue, **Then** the installer shows a clear message. The installer does not continue until the value is valid.
4. **Given** the operator changes the interface or port during an upgrade, **When** the upgrade ends, **Then** the bot uses the new values.

---

### User Story 2 - Sensible defaults for a new install (Priority: P1)

For a new install, the installer pre-fills the interface as localhost and the port as the first free port from the current port selection mechanism.

**Why this priority**: A new user must get a safe setup that works, with no extra steps.

**Independent Test**: Run a new install and accept all defaults. Check that the bot listens on localhost only, on the port the selection mechanism chose.

**Acceptance Scenarios**:

1. **Given** a new install and a free first preferred port, **When** the operator accepts the defaults, **Then** the bot binds to localhost on that port.
2. **Given** the first preferred port is in use, **When** the choice shows, **Then** the pre-filled port is the next free port from the selection mechanism.

---

### User Story 3 - Upgrade keeps interface and port (Priority: P1)

When an operator upgrades a current install, the choice shows the saved interface and port as defaults. If the operator makes no change, the bot comes back on the same interface and port.

**Why this priority**: Users lose access (phone, bookmarks, scripts) if an upgrade changes the binding. This is the main annoyance.

**Independent Test**: Install with a non-default interface and port. Upgrade and accept the defaults. Check that the binding is the same.

**Acceptance Scenarios**:

1. **Given** an install with a non-default binding, **When** the operator upgrades and accepts the defaults, **Then** the bot uses the same binding.
2. **Given** an install with a saved binding, **When** the upgrade runs in silent mode with no binding given, **Then** the saved binding is kept.
3. **Given** a saved binding is absent or not valid, **When** the upgrade runs, **Then** the installer uses the new-install defaults and tells the operator.

---

### User Story 4 - Start-Menu shortcuts show the real address (Priority: P1)

The Start-Menu shortcut opens the address the bot really uses. The installer does not recalculate the address or fall back to the defaults.

**Why this priority**: A shortcut that opens a wrong address makes the bot look broken.

**Independent Test**: Install with a custom interface and port. Read the shortcut target. Check that it holds the chosen host and port.

**Acceptance Scenarios**:

1. **Given** the operator chose a custom port, **When** the install ends, **Then** the shortcut opens the address with that port.
2. **Given** the operator chose a non-loopback interface, **When** the install ends, **Then** the shortcut opens the computer name of this PC with the chosen port.
3. **Given** an upgrade keeps the binding, **When** the upgrade ends, **Then** the shortcut still shows the same address as before.

---

### User Story 5 - Upgrade from the web UI or API keeps binding and shortcuts (Priority: P1)

When an upgrade starts from the bot web UI or API, the binding and the Start-Menu shortcuts stay the same. The upgrade needs no input from the operator.

**Why this priority**: This path is silent. A mistake here breaks access to the bot with no warning.

**Independent Test**: Start an upgrade from the UI or API on a bot with a custom binding. Check the binding and the shortcut after the bot restarts.

**Acceptance Scenarios**:

1. **Given** a bot with a custom binding, **When** an upgrade starts from the UI or API, **Then** the bot restarts with the same binding.
2. **Given** the same upgrade, **When** it ends, **Then** all Start-Menu shortcuts exist and point to the same address as before.
3. **Given** an upgrade from the UI or API fails, **When** the installer rolls back, **Then** the binding and the shortcuts stay as they were.

---

### User Story 6 - One source for the saved host and port (Priority: P2)

Today the host and port exist in more than one place. The installer saves them in a settings file and in the registry. The installer copies them into separate helper variables. The bot also reads runtime overrides under other names. These copies can differ, and a difference gives a wrong binding or a wrong shortcut. A maintainer needs one saved value for the host, one for the port, and one clear rule that says which value wins.

**Why this priority**: The duplicates are the root cause of the binding and shortcut faults in stories 3 to 5. Stories 3 to 5 need the cleanup to stay correct. The cleanup alone adds no new user feature, so it is P2.

**Independent Test**: List every place that stores or reads the host and port. Check that each value has one saved location and one main name. Run an install, an upgrade, and a UI upgrade. Check that the binding is the same in every step.

**Acceptance Scenarios**:

1. **Given** the current code and installer, **When** a maintainer lists the stores and names of the host and port, **Then** each value has one saved location and one main name. No store duplicates another.
2. **Given** a current install with old stores, **When** the operator upgrades, **Then** the installer reads the old value once. It saves the value in the one store that stays. The binding does not change.
3. **Given** the cleanup is done, **When** any install or upgrade runs, **Then** the bot, the installer, and the shortcuts use the same host and port.
4. **Given** the cleanup is done, **When** an operator uses `BIND_HOST` or `PORT` in a silent install, **Then** the installer accepts the values as before.

---

### Edge Cases

- Old and new stores hold different values at upgrade time. The rule in FR-013 decides, and the installer removes or replaces the old copy.
- The saved port is now in use by another program at upgrade time. The installer must tell the operator in interactive mode. In silent mode the upgrade must end with success, keep the saved port, and write a warning that names the port to the installer log. It must not pick a new port. The bot must write the bind failure to its own log and exit with a non-zero code (FR-022).
- The operator sets the interface to all interfaces. The shortcut must use the computer name of this PC, as for any other non-loopback value (FR-023).
- A silent install gives only the port, or only the interface. The absent value must follow the rules for new install or upgrade.
- The install folder is a custom folder, not the default.
- Same-build reinstall: the choice must still show and the binding must not change unless the operator changes it.

## Requirements *(mandatory)*

### Functional Requirements

*Note: FR-014 is not used. Other documents cite these numbers, so each requirement keeps its ID.*

- **FR-001**: Every interactive local install and every interactive local upgrade MUST show a choice of bind interface and port.
- **FR-002**: For a new install, the default interface MUST be localhost.
- **FR-003**: For a new install, the default port MUST come from the current port selection mechanism.
- **FR-004**: For an upgrade, the default interface and port MUST be the values saved by the current install.
- **FR-005**: A value that the operator or a silent-mode argument gives MUST override a default.
- **FR-006**: The installer MUST check that the port is a valid number and tell the operator when it is not valid or is in use. A port that the installed GameBot service itself holds MUST NOT count as in use. The check MUST count only ports that a program listens on.
- **FR-007**: A silent install or upgrade MUST apply the same rules as FR-002 to FR-005, with no dialog.
- **FR-008**: The Start-Menu shortcuts MUST open the address made from the interface and port that the bot really uses after the install or upgrade.
- **FR-023**: The shortcut host MUST be `localhost` when the interface is the loopback address `127.0.0.1`. For all other interface values, the shortcut host MUST be the computer name of the PC. This covers `0.0.0.0` and a specific network address.
- **FR-009**: The installer MUST NOT recalculate the shortcut address from other sources, or fall back to a default address, when a binding is known.
- **FR-010**: An upgrade started from the web UI or API MUST keep the saved interface and port.
- **FR-011**: An upgrade started from the web UI or API MUST keep all Start-Menu shortcuts and their addresses.
- **FR-012**: If an upgrade fails and rolls back, the binding and the shortcuts MUST be the same as before the upgrade.
- **FR-013**: One order of priority MUST decide the binding value when more than one source exists: explicit value, then saved settings, then new-install default.
- **FR-015**: The host and port MUST be saved in one location only: the file `config\network.json` in the data directory. Only the installer writes the file. The installer and the bot read it. The registry copy (`HKCU\Software\GameBot\Network`) MUST NOT be written. If the file has no value on an upgrade, the installer MUST read an old registry value once. It MUST move the value to the file and remove the registry copy.
- **FR-016**: The host and port MUST have one main name each in the installer and in the settings of the bot. Duplicate names for the same value MUST be removed or become a documented alias that maps to the main name.
- **FR-017**: The shortcut address MUST come from the host and port in FR-015. It MUST NOT use a separate stored copy.
- **FR-018**: An upgrade from an install with an old store MUST read the old value once. It MUST move the value to the one saved location and keep the binding the same. The upgrade MUST NOT lose the value.
- **FR-019**: A runtime override of the host or port (environment variable or runtime setting) MUST win over the saved value for the bot. The installer MUST ignore it and MUST NOT save it. The bot status MUST show the active host and port and their source (override or saved).
- **FR-020**: The cleanup MUST NOT break the silent-mode arguments that are in use now: `BIND_HOST`, `PORT`, and the updater command line. A property that was never public needs no handling.
- **FR-022**: In silent mode, if the saved port is in use at upgrade time, the upgrade MUST end with success. It MUST keep the saved port. It MUST write a warning that names the port to the installer log. If the bot cannot bind the address, it MUST write an error to its log. The error MUST name the host, the port, and the source of each. The bot MUST then exit with a non-zero code.
- **FR-021**: Documentation of install and upgrade MUST describe the choice, the defaults, the silent-mode arguments, and the one saved location of the host and port.

### Key Entities

- **Binding**: The pair of bind interface and port on which the bot listens. It is saved so that an upgrade can read it.
- **Start-Menu shortcut**: A link that opens the bot web UI at the address made from the binding.
- **Upgrade request**: An upgrade that starts from the installer, the web UI, or the API. It carries an optional binding.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: In 100% of interactive installs and upgrades, the operator sees the interface and port choice.
- **SC-002**: In 100% of upgrades with no change by the operator, the interface and port stay the same. This covers the installer, the UI, and the API.
- **SC-003**: In 100% of installs and upgrades, each Start-Menu shortcut opens the web UI of the active bot with no manual edit.
- **SC-004**: A new install with all defaults gives a bot that only the local PC can reach. The web UI opens from the shortcut on the first try.
- **SC-005**: A failed upgrade leaves the previous binding and shortcuts unchanged in 100% of tested cases.
- **SC-006**: The host and port each have exactly one saved location and one main name per component. A review of the code and installer finds no other stored copy.
- **SC-007**: An upgrade from an install that uses the old stores keeps the binding in 100% of tested cases.

## Assumptions

- "Local installation" means the installer that runs on the same PC as the bot. It covers interactive and silent modes.
- The current port selection mechanism (first free port from the preferred list) stays as is.
- The settings file in the data directory is the one saved location (see Clarifications). The registry copy and the extra helper copies in the installer are the duplicates to remove.
- Runtime overrides stay allowed for the bot (for example for tests). They are not saved by the installer.
- Tests use a temporary install folder and a temporary port. No test overwrites the active production install. If this becomes necessary, the owner must approve it first.
- The MSI shares its upgrade code, registry key, and Start-Menu folder with the production install. A temporary folder alone does not isolate a test. Tests use an isolated test variant of the installer (see plan, research R12).
- Terms: "interface" is the choice that the user makes in the dialog. "Host" is the saved value (`bindHost`). "Binding" is the pair of host and port.
- The web UI for the update flow does not change. Only the data it passes to the installer is in scope.
