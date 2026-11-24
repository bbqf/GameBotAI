# Feature Specification: Device Recovery Option in the Queue Form

**Feature Branch**: `199-device-recovery-ui`  
**Created**: 2026-11-24  
**Status**: Implemented  
**Input**: User description: "I need an option in the ui for the feature 121"

## Background

Feature 121 (`specs\121-device-not-live-alert-recovery`) added the optional queue field `deviceRecovery`. When it is on, the service reboots the emulator instance of a queue whose device stays not live. The field has the members `action` (`none` or `reboot-instance`), `afterMs`, `maxAttempts`, and `cooldownMs`. Feature 121 also added the read-only members `alertSent`, `recoveryAttempts`, and `recoveryState` to `health.deviceLiveness`.

Today, the operator can set `deviceRecovery` only through the API. The queue form of the web UI has no control for it. There is also a defect: on an update, an absent `deviceRecovery` sets the field to null. The web UI does not send the field. Thus, each save of a queue in the web UI turns off a recovery that the operator set through the API.

## Clarifications

### Session 2026-11-24

- Q: Where is the fix for the lost recovery settings on save (FR-005)? → A: In both places. The form always sends the full `deviceRecovery`. The service, on update, keeps the saved value when `deviceRecovery` is absent, and clears it only for an explicit `null`. The contract of spec 199 replaces the update rule of the feature 121 contract (analyze G1: the 121 contract stays as history). The OpenAPI text changes to match.
- Q: Where does the web UI show the device state? → A: The full detail is in the edit view of a running queue. The Monitoring page and the phone view of the queues show a short "Not live" mark for a queue whose device is not live.
- Q: When does a recovery change apply to a running queue? → A: At once. The running queue uses the saved recovery settings from its next liveness check (1 minute or less). The current not-live episode keeps its attempt count. The form note tells the operator this.

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Turn on device recovery in the queue form (Priority: P1)

The operator opens the create or edit form of a queue. The operator sets the option "Reboot the instance when the device is not live", and sets the wait time, the number of attempts, and the time between attempts. The operator saves. The service keeps these settings. A running queue uses them from its next liveness check, with no stop and start.

**Why this priority**: This is the request. Without it, the operator must use the API to turn on the recovery.

**Independent Test**: Open the edit form of a queue that has an emulator instance name. Turn on the option, set the wait time to 3 minutes, and save. Read the queue through the API: `deviceRecovery.action` is `reboot-instance` and `afterMs` is 180000.

**Acceptance Scenarios**:

1. **Given** the create or edit form of a queue, **When** the operator looks at the form, **Then** the form shows the device recovery option, off by default for a new queue.
2. **Given** the operator turns on the option, **When** the form shows the option on, **Then** the form shows three fields: wait time before the first reboot (minutes, default 5), maximum attempts (default 2), and time between attempts (minutes, default 3).
3. **Given** the option is on and all values are valid, **When** the operator saves, **Then** the service keeps `deviceRecovery` with `action` = `reboot-instance` and the values the operator set.
4. **Given** the operator turns off the option, **When** the operator saves, **Then** the service keeps `deviceRecovery` with `action` = `none`.
5. **Given** a queue with saved recovery settings, **When** the operator opens its edit form, **Then** the form shows the saved values.
6. **Given** a running queue whose device is not live for longer than the wait time, and recovery is off, **When** the operator turns on the option and saves, **Then** the first recovery attempt starts at the next liveness check (1 minute or less), with no stop and start of the queue.

---

### User Story 2 - A save keeps the recovery settings (Priority: P1)

The operator changes a different setting of a queue (for example, the name) in the web UI and saves. The recovery settings of the queue do not change.

**Why this priority**: Today a save from the web UI turns off the recovery without a message. The operator then thinks that the device is protected, but it is not.

**Independent Test**: Set `deviceRecovery` on a queue through the API. Open the edit form, change the name, and save. Read the queue through the API: `deviceRecovery` has the same values as before.

**Acceptance Scenarios**:

1. **Given** a queue with recovery on, **When** the operator changes only the name and saves, **Then** `deviceRecovery` keeps all its values.
2. **Given** a queue with no `deviceRecovery`, **When** the operator saves without a change to the option, **Then** the queue still has no recovery (it behaves as before feature 121).

---

### User Story 3 - Clear validation in the form (Priority: P2)

The form tells the operator about a value that is not permitted before the save, at the field.

**Why this priority**: It prevents a failed save, but the service rejects bad values in all cases.

**Independent Test**: Turn on the option with an empty emulator instance name. The form shows a message that the recovery needs an emulator instance name, and the save does not go to the service.

**Acceptance Scenarios**:

1. **Given** the option is on and the emulator instance name is empty, **When** the operator tries to save, **Then** the form shows "Device recovery needs an emulator instance name." and does not save.
2. **Given** the option is on, **When** the wait time is less than 1 minute, **Then** the form shows a message at that field and does not save.
3. **Given** the option is on, **When** the maximum attempts is not a whole number from 1 to 5, **Then** the form shows a message at that field and does not save.
4. **Given** the option is on, **When** the time between attempts is less than 0, **Then** the form shows a message at that field and does not save.
5. **Given** the service rejects the save with a validation error, **When** the form gets the error, **Then** the form shows the text of the error from the service.

---

### User Story 4 - See the recovery state of a running queue (Priority: P3)

In the edit view of a running queue, the operator sees whether the device is live, whether an alert was sent, the number of recovery attempts in the current episode, and the recovery state (`idle`, `running`, or `exhausted`).

**Why this priority**: It helps the operator to see the effect of the option. The setting works without it.

**Independent Test**: Block the capture of a device of a running queue with recovery on. Open its edit view. The view shows "Not live", and after the alert time it shows that the alert was sent and the recovery state.

**Acceptance Scenarios**:

1. **Given** a running queue with a live device, **When** the operator opens its edit view, **Then** the view shows that the device is live.
2. **Given** a running queue with a not-live device, **When** the operator opens its edit view, **Then** the view shows "Not live", the reason, whether the alert was sent, the recovery attempts, and the recovery state.
3. **Given** a stopped queue, **When** the operator opens its edit view, **Then** the view shows no device state.
4. **Given** a running queue with a not-live device, **When** the operator looks at the Monitoring page or the phone view of the queues, **Then** the queue has a short "Not live" mark. A queue with a live device or a stopped queue has no mark.

---

### Edge Cases

- The saved `afterMs` or `cooldownMs` is not a whole number of minutes (it was set through the API): the form shows the value in minutes with up to one decimal place. If the operator does not change that field, the save sends the saved value without a change.
- The operator clears the emulator instance name while the option is on: the form shows the validation message of User Story 3.
- The operator turns off the option: the form keeps the other values in the form, so that the operator can turn it on again with the same values. The save sends `action` = `none` with these values.
- A queue is duplicated: the copy gets the same recovery settings as the source.
- The queue settings export and import already carry `deviceRecovery` (feature 121). This feature does not change them.
- The queue runs when the operator saves: the recovery change applies from the next liveness check of the running queue (FR-012a). The other settings still apply at the next start.
- The operator turns on the recovery while the device of a running queue is already not live and the episode is older than the new wait time: the first attempt starts at the next liveness check (with the one-at-a-time rule of feature 121).
- The operator turns off the recovery while an attempt runs: the attempt that runs ends normally. No new attempt starts.
- The operator lowers the maximum attempts below the attempt count of the current episode: no new attempt starts, and the recovery state is `exhausted`.
- The phone layout: the phone view of the queues has no queue settings form (the Queues area has the phone support level `reduced`). The option is not on the phone. The "Not live" mark is on the phone queue card and in the phone queue detail.

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: The create form and the edit form of a queue MUST show an option to turn device recovery on or off. The option MUST be off for a new queue.
- **FR-002**: When the option is on, the form MUST show three fields: wait time before the first reboot (minutes, default 5, minimum 1), maximum attempts (whole number, default 2, from 1 to 5), and time between attempts (minutes, default 3, minimum 0).
- **FR-003**: The form MUST show a short help text that tells the operator what the option does: it reboots the emulator instance when the device stays not live, and it needs the emulator instance name.
- **FR-004**: On save (create and update), the form MUST send `deviceRecovery` by this table. The values MUST go to the service in milliseconds.

  | Option | The queue had `deviceRecovery` | The form sends |
  |---|---|---|
  | on | any | `action` = `reboot-instance` with the three values |
  | off | yes | `action` = `none` with the three values |
  | off | no (or a new queue) | no `deviceRecovery` member |

- **FR-005**: A save from the web UI MUST NOT remove or change the recovery settings of a queue when the operator did not change them. The form follows the table of FR-004.
- **FR-005a**: On update, the service MUST keep the saved `deviceRecovery` when the request has no `deviceRecovery` member. Only an explicit `null` MUST clear it. The contract of this spec (`contracts\queue-recovery-ui-api.md`, section 1) replaces the update rule of the feature 121 contract, which does not change (Principle V). The OpenAPI text MUST state this rule. Create does not change: an absent member on create still means no recovery.
- **FR-006**: For a queue with no `deviceRecovery`, a save with the option off MUST keep the queue with no `deviceRecovery`.
- **FR-007**: The edit form MUST show the saved recovery settings of the queue.
- **FR-008**: The form MUST check the values before the save and show a message at the field for each value that is not permitted (FR-002), and for the option on with an empty emulator instance name.
- **FR-009**: When the service rejects the save with a validation error, the form MUST show the text of the error.
- **FR-010**: The duplicate action MUST copy the recovery settings of the source queue.
- **FR-011**: The edit view of a running queue MUST show the device state from `health.deviceLiveness`: live or not live, the reason, whether the alert was sent, the recovery attempts, and the recovery state.
- **FR-011a**: The Monitoring page and the phone view of the queues MUST show a short "Not live" mark for each running queue whose device is not live, and no mark for other queues. The mark MUST update when these views poll the queues. The mark can be up to one liveness check interval (1 minute) old.
- **FR-012**: Phone support level: the recovery option is `desktop-only`, as the rest of the queue settings form (the Queues area stays `reduced`). The "Not live" mark is `full`: it MUST show on the phone queue card and in the phone queue detail, with no horizontal scroll.
- **FR-012a**: A saved change of `deviceRecovery` MUST apply to a running queue from its next liveness check (1 minute or less after the save), with no stop and start. The current not-live episode MUST keep its attempt count. When the queue runs, the note near Save MUST tell the operator that the recovery settings apply at once and the other settings apply at the next start.
- **FR-013**: The work MUST keep the living documents in step: `docs\architecture.md` (with a refreshed "Last reviewed" date), `docs\mobile-ui-principles.md`, `CHANGELOG.md`, `specs\STATUS.md` (spec 199), and the Status line of spec 121, which MUST point to spec 199 as the spec that iterates it.

### Key Entities

- **Device recovery settings (`deviceRecovery`)**: The current queue field of feature 121. This feature adds no new member. The form shows the times in minutes, and the service keeps them in milliseconds.
- **Device state (`health.deviceLiveness`)**: The current read-only health block of a running queue. This feature only shows it.

## Assumptions

- The alert time and the stagger time are service settings (feature 121). They are not in the queue form and are out of scope.
- The option is in the queue form near the emulator instance name, because it needs that name.
- The form uses minutes for the times, as the smart scheduling window does. The service API does not change its units.
- The fix for FR-005 is in the web UI and in the service (see Clarifications and FR-005a). Other API clients that send an update with no `deviceRecovery` also keep the saved value after this change.
- The device state view in the edit view reads the same queue detail that the edit view reads now. No new endpoint is necessary. The queue list and the monitoring session list each get one small read-only member for the "Not live" mark (plan, research R-004).
- Plan correction (2026-11-24): the phone view has no queue settings form, so the earlier phone requirement for the option changed to the phone support level in FR-012.
- The labels are in English, as the rest of the web UI.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: The operator can turn on device recovery for a queue in the web UI in less than 1 minute, with no API call.
- **SC-002**: In 100% of saves from the web UI with no change to the option, the recovery settings of the queue stay the same.
- **SC-003**: Each value that is not permitted gets a message at its field before the save; no such value causes a failed save with no message.
- **SC-004**: For a running queue with a not-live device, the operator sees the device state and the recovery state in the edit view with no manual API call.
- **SC-005**: The "Not live" mark shows with no horizontal scroll on a phone screen 360 pixels wide.
- **SC-006**: A recovery change on a running queue takes effect within 1 minute of the save, with no stop and start of the queue.
