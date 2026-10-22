# Feature Specification: Gamepacks Page Take-Over Option

**Feature Branch**: `160-gamepacks-page-take-over`  
**Created**: 2026-10-22  
**Status**: Draft  
**Input**: User description: "Issue 336 (FR-033): the Gamepacks page offers the take-over option for an install or update. Closes #336."

## Clarifications

### Session 2026-10-22

- Q: After the operator changes the check box, does the page run the dry run again by itself? → A: No. The page clears the old dry run result. The operator starts the dry run again. Rationale: the issue says "run the dry run again" as an operator action. This also avoids hidden requests.
- Q: Does the page show the take-over count when the check box is off? → A: No. The count shows only when the check box was on for that dry run. Rationale: with take-over off, the conflicts are the important information.
- Q: Does the page keep the take-over choice between visits? → A: No. The choice is page state. It is off each time the page loads. Rationale: take-over is a risky choice. The operator must decide each time.

### Session 2026-10-22 (loop 1, from analyze findings)

- Q: The Gamepacks page has no dialogs and no file install. It calls only the catalog endpoints. How does the spec treat "dialog" and the file install (I1, I8)? → A: The spec uses "page" in place of "dialog". The page has one take-over check box next to the existing Dry run check box. The check box applies to the Install button and the Update button of each catalog version. The file install has no UI on this page. So User Story 3 and the file part of the issue criterion 6 are not applicable. A file install is a new install option and is out of scope (FR-011). The PR text records this gap.
- Q: Must every install run a dry run first (I2, I3)? → A: No. Today the Dry run check box is optional. An install with Dry run off installs at once. The page keeps this when take-over is off. When take-over is on, a real install needs a valid dry run of the same version made with take-over on. Rationale: take-over replaces user objects, so the operator must see the count first. A forced dry run for all installs would change the current behaviour.
- Q: What does the page show when the check box changes after a dry run (I4)? → A: The page clears the dry run notice. While take-over is on, the Install or Update button of that version stays disabled. It stays disabled until a new valid dry run runs.
- Q: What exact text shows the count (I6)? → A: `Take over N.` inside the dry run notice.
- Q: What exact label and hint text does the check box have (I5)? → A: Label: `Take over objects of the user with the same name`. Hint: `The gamepack owns these objects and replaces them. The old content goes to a backup.` The backup statement is true. Feature 149 writes a take-over backup.

### Session 2026-10-22 (loop 2, from analyze findings)

- Q: Does the count show for an invalid dry run with take-over on (A1)? → A: Yes. The count shows in every dry run notice made with take-over on. The conflict and problem lines stay visible.
- Q: What is the exact hint text for a disabled button, and where does it show (A2)? → A: Text: `Run the dry run with take-over first.` It shows in the row of the version whose button is disabled.
- Q: How does the operator use the Dry run box with take-over on (A3)? → A: The operator turns Dry run on. The operator presses the button to run the dry run. Then the operator turns Dry run off. The operator presses the button for the real install. A change of the Dry run box keeps the last dry run result. Only a change of the take-over box clears it.

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Update from the catalog with take-over (Priority: P1)

An operator builds a task and applies its objects to the instance with a script before release. The objects are user-owned. The new gamepack version has objects with the same names. The operator opens the Gamepacks page and turns on Dry run. The dry run shows a conflict for each same-named user object. The operator turns on the take-over check box and runs the dry run again. The page shows "Take over N." The operator presses Update and the update succeeds.

**Why this priority**: This is the failure that the issue reports. Without it, the operator must call the API by hand for each release.

**Independent Test**: Make a catalog gamepack that holds N objects with the same names as user-owned objects. Open the page. Check that the dry run shows N conflicts. Turn on take-over and run the dry run again. Check that the page shows "Take over N." Press Update. Check that the install succeeds and the objects are owned by the gamepack.

**Acceptance Scenarios**:

1. **Given** the Gamepacks page is loaded, **When** the page shows, **Then** the take-over check box is visible and it is off.
2. **Given** the check box is off and the dry run finds conflicts, **When** the page shows the dry run result, **Then** the page lists the conflicts and the operator can turn on the check box.
3. **Given** the check box is on, **When** the dry run runs, **Then** the dry run request uses take-over and the page shows the number of objects to take over (for example "Take over 20.").
4. **Given** the check box is on and the dry run for that version is valid, **When** the operator presses Install or Update, **Then** the page sends the install with take-over.
5. **Given** the check box is off, **When** the operator presses Install or Update, **Then** the page sends the install with take-over false, and the install works with or without a dry run as it does today.
6. **Given** the check box is on and no valid dry run exists for that version and check box state, **When** the page shows the button, **Then** the button is disabled and a hint says to run the dry run with take-over first.

---

### User Story 2 - Install from the catalog with take-over (Priority: P1)

An operator installs a gamepack from the catalog for the first time. User-owned objects with the same names exist. The Install button of a version has the same take-over check box and the same behaviour as the Update button.

**Why this priority**: The issue names the install and the update. Both must behave the same.

**Independent Test**: Use a catalog gamepack that is not installed and conflicts with user objects. Do the same checks as in User Story 1.

**Acceptance Scenarios**:

1. **Given** a catalog gamepack that is not installed, **When** the page shows, **Then** the take-over check box applies to its Install button and it is off.
2. **Given** the check box is on and the dry run is valid, **When** the operator presses Install, **Then** the page sends the install with take-over.

---

### Edge Cases

- The operator changes the check box after a dry run. The page clears the old result. With take-over on, the button stays disabled until a new valid dry run. The page does not start the dry run by itself.
- A valid dry run exists for version A with take-over on. The button of version B stays disabled. A dry run result is valid only for its own version.
- The dry run with take-over finds no same-named user objects. The page shows "Take over 0." and the install can continue.
- The dry run has other problems that take-over does not solve. The page shows them and the install stays blocked.
- The operator loads the page again. The check box is off again.
- The dry run request fails (network or server error). The page shows the error and the operator can try again.
- The page has no file install. A file install is not part of this feature.

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: The Gamepacks page MUST show one take-over check box next to the Dry run check box. It applies to the Install button and to the Update button of each catalog version. The check box MUST be off each time the page loads.
- **FR-002**: The check box label MUST be `Take over objects of the user with the same name`. A hint MUST say that the gamepack owns these objects and replaces them, and that the old content goes to a backup.
- **FR-003**: When Dry run is on, the page MUST send the dry run with the state of the check box.
- **FR-004**: When the check box was on for the dry run (valid or not), the page MUST show the number of objects that the install would take over, as `Take over N.`
- **FR-005**: When the check box is off and the dry run finds conflicts, the page MUST show the conflicts and MUST NOT hide them. The operator MUST be able to turn on take-over and run the dry run again.
- **FR-006**: When the operator presses Install or Update, the page MUST send the install with take-over true if the check box is on, and with take-over false if it is off.
- **FR-007**: FR-001 to FR-006 MUST apply to the catalog install and the catalog update. The file install has no UI on the page and is not part of this feature.
- **FR-008**: When the check box is on, the page MUST allow a real install only after a valid dry run of the same version made with take-over on. When the check box is off, the install keeps its current behaviour (a dry run is optional). A change of the check box MUST clear the dry run result.
- **FR-009**: The feature MUST NOT change the take-over rules in the API or the backend.
- **FR-010**: The feature MUST NOT change the explanation text for failed installs and dry runs (issue 337).
- **FR-011**: The feature MUST NOT add other install options.
- **FR-012** *(web UI change)*: The phone support level of the feature is full. The check box and its text follow docs/mobile-ui-principles.md, with a touch target of the required size and no horizontal scroll.

### Key Entities

- **Take-over choice**: A page setting. It is on or off. It is off by default. It applies to the dry run and to the install on the page.
- **Dry run result**: The result that the API returns before an install. It has a valid flag, a list of problems (including conflicts with an owner), and a count of objects to take over. The page keeps the last result with its version and its take-over choice.

## Assumptions

- The catalog install API already accepts the take-over choice and already returns the take-over count. The page only needs to use them.
- The existing text for conflicts and failures stays as it is.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: An operator can finish an update that has user-owned name conflicts from the Gamepacks page alone, with no manual API call.
- **SC-002**: In 100% of page loads, the take-over check box is shown and is off.
- **SC-003**: In 100% of installs started from the page, the take-over choice sent to the API is the same as the check box state when the operator presses the button. With the check box on, it is also the same as the state of the last valid dry run for that version.
- **SC-004**: When the check box is on, the operator sees the number of objects to take over before a real install can start.

## Testing and Verification Notes

- Tests MUST cover FR-005: the check box is off, the dry run finds conflicts, the page shows the conflicts, and the check box is still available.
- Tests MUST cover the edge case "Take over 0.": the dry run with take-over on finds no same-named user objects, the page shows "Take over 0.", and the install can continue.
- A test MUST show that a valid dry run of version A with take-over on does not enable the Install or Update button of version B.
- The phone check is a manual check: 375 px width, a touch target of 44 px, and no horizontal scroll. The PR text records the result.
- Task IDs MUST be in order, with no gap. One task records the gap (the page has no file install) in the PR text and in docs/gamepacks.md.
- The docs task MUST run a grep for "Gamepacks" in docs/architecture.md. If the grep finds a match that this change affects, the task updates the "Last reviewed" line in that file.
- A final task MUST set the Status line of this spec to Implemented. It MUST also add the row for feature 160 in specs/STATUS.md. (A4)
- A test MUST check that the Update button and the Install button both disable after a change of the take-over box. (A5)
- A test MUST check that the dry run request sends takeOver false when the box is off. (A6)
- The test runner is Jest, not Vitest. Test files use Jest APIs. (A7)
- The service test file is src/web-ui/src/services/__tests__/gamepackCatalog.test.ts. The setup task creates it if it is absent, and the setup task lists it. (A9)
- The task that adds the page logic MUST replace any temporary false value for takeOver that an earlier task passes to the service. (A10)
