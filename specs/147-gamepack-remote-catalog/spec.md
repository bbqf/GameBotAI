# Feature Specification: Browse and install gamepacks from a remote catalog

**Feature Branch**: `147-gamepack-remote-catalog`  
**Created**: 2026-10-16  
**Status**: Draft  
**Input**: User description: "Browse and install gamepacks from a configurable remote catalog. GitHub issue #308 (FR-025). Closes #308."

## Clarifications

### Session 2026-10-16

- Q: Where is the index document? → A: For a web address, the index is at the catalog location plus "index.json". For a folder, it is the file "index.json" in the folder. File locations in the index are relative to the catalog location. (Rationale: one fixed rule; FR-004, FR-006.)
- Q: Where does the platform keep the token? → A: In the same settings store as the other instance settings. The settings read route returns only a flag "tokenSet", never the token. An empty value on write clears the token. (Rationale: FR-002.)
- Q: Does the platform follow redirects? → A: Yes, up to 3 redirects, only to the same host and the same scheme. The token goes only to that host. Other redirects fail with a clear reason. (Rationale: FR-011.)
- Q: Which versions show an Update button? → A: Each version that is newer than the installed version, or every version when no version is installed (the button is then named "Install"). Older versions have no button. (Rationale: FR-016; rollback stays on its own route.)
- Q: Which web addresses are allowed? → A: "https" always. "http" only when the host is a loopback address or the operator sets an explicit "allow insecure" flag in the catalog setting. (Rationale: the token must not travel in clear text by default.)
- Q: Is the catalog list cached? → A: No. Each list request reads the index. The platform keeps no copy of the index. (Rationale: simple; the operator sees current data; FR-015.)
- Q: Which settings permission is needed? → A: The existing API token rule. No new role. (Rationale: FR-018.)

### Session 2026-10-16 (loop 1, from analyze findings)

- Q (C1): What does the list return when "index.json" is missing? → A: The reason "index_invalid" (the catalog is reachable but has no usable index), with a clear message. (Rationale: FR-013 needs one named cause.)
- Q (C2): What are the exact redirect rules? → A: At most 3 redirects, same host, same scheme. The Edge Cases text follows this rule. (Rationale: one rule in all places.)
- Q (C3): Do the token and the "allow insecure" flag apply to a folder catalog? → A: No. They apply to web addresses only. A folder source ignores them. (Rationale: FR-005, FR-006.)

### Session 2026-10-16 (loop 2, from analyze findings)

- Q (F1): Where does the signature for the feature 146 check come from? → A: Each index entry has an optional detached signature file location, next to the gamepack file location. The platform downloads it with the same rules as the gamepack file (same host, token rule, 1 MB size limit, request time limit) and passes it to the signature check. When it is absent, the signature check sees "missing". (Rationale: FR-008 needs a source for the signature.)
- Q (F2): Which reason is used for an unlisted version? → A: One reason name, "entry_not_listed". The reason "not_found" applies only to a listed file that the catalog answers with "not found". (Rationale: one name per case.)
- Q (F3): Does the page have a dry-run option and a refresh action? → A: Yes. The page has a "dry run" check box and a "Refresh" button. Both start only on operator action. (Rationale: FR-009, FR-015.)

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Browse the catalog (Priority: P1)

An operator sets the location of a catalog (a web address or a local folder) and, if the catalog needs it, an access token. The operator then opens the catalog list. For each gamepack, the list shows the versions that the catalog offers, the changelog text of each version, and which version is installed on the instance now.

**Why this priority**: The operator must see what is offered before a choice to install. The list is also the base for the other stories.

**Independent Test**: Set the location to a catalog with two gamepacks. Install one version of the first gamepack. Open the list. The list shows all offered versions with changelog text, and marks the installed version.

**Acceptance Scenarios**:

1. **Given** a catalog location that holds two gamepacks with two versions each, **When** the operator requests the list, **Then** the list shows four versions grouped by gamepack, each with its changelog text.
2. **Given** version 1.0.0 of a gamepack is installed, **When** the operator requests the list, **Then** version 1.0.0 is marked as installed and the other versions are not.
3. **Given** the catalog needs a token and the operator sets one, **When** the operator requests the list, **Then** the platform sends the token with the request and shows what the catalog returns.
4. **Given** no catalog location is set, **When** the operator requests the list, **Then** the platform returns a clear "catalog not configured" reason.
5. **Given** the catalog cannot be reached, refuses the token, or returns an index that is not valid, **When** the operator requests the list, **Then** the platform returns a clear reason that names the cause, and the instance does not change.

---

### User Story 2 - Install a version from the catalog (Priority: P1)

The operator picks one version of a gamepack from the list and starts the install. The platform downloads the gamepack file from the catalog, checks the signature with the existing signature check (feature 146), and installs it with the existing install flow (feature 143). The operator can ask for a dry run first. A dry run does everything except the change to the instance.

**Why this priority**: This is the core value. The operator gets an update without copy of files by hand.

**Independent Test**: Start an install of a listed version. The gamepack is installed and shows in the installed list. Start the same install as a dry run on a clean instance. The result shows what would change, and the instance stays the same.

**Acceptance Scenarios**:

1. **Given** a listed version, **When** the operator starts the install, **Then** the platform downloads the file, checks the signature, installs the gamepack, and returns the install result.
2. **Given** a listed version, **When** the operator starts the install as a dry run, **Then** the result shows the effect of the install and nothing changes on the instance.
3. **Given** the signature policy is "required" and the downloaded file has no valid signature, **When** the operator starts the install, **Then** the install is rejected with the signature reason and nothing changes.
4. **Given** a name or version that the catalog does not list, **When** the operator starts the install, **Then** the platform returns the reason "entry_not_listed" and nothing is downloaded.
5. **Given** the download fails or is cut off, **When** the operator starts the install, **Then** the platform returns a clear reason and nothing changes.

---

### User Story 3 - Review and update in the web interface (Priority: P2)

The operator opens a "Gamepacks" page in the web interface. The page shows the same list as the catalog list, with the changelog text of each version and the installed marker. Each version that is newer than the installed version has an "Update" button. The operator presses the button to install. The page has a "dry run" check box and a "Refresh" button. The page shows the result or the reason for a failure.

**Why this priority**: It makes the feature usable without direct API calls. The API is complete without it.

**Independent Test**: Open the page with a catalog that offers a newer version. The page shows the changelog and the Update button. Press the button. The page shows the install result and the new installed marker.

**Acceptance Scenarios**:

1. **Given** a catalog with a newer version than the installed one, **When** the operator opens the page, **Then** the page shows the version, its changelog text, and an Update button.
2. **Given** the page is open, **When** the operator presses Update, **Then** the install runs once and the page shows the result.
3. **Given** the catalog is not configured or cannot be reached, **When** the operator opens the page, **Then** the page shows the reason in plain words.

---

### User Story 4 - No install without a user action (Priority: P1)

The platform never installs a gamepack from the catalog by itself. It does not check for updates in a loop to install them, and it does not install when the operator only opens the list or the page.

**Why this priority**: An install replaces working automations. The operator must stay in control.

**Independent Test**: Offer a newer version in the catalog. Open the list and the page many times and wait. The installed version does not change until the operator starts an install.

**Acceptance Scenarios**:

1. **Given** a newer version in the catalog, **When** the operator requests the list or opens the page, **Then** no install starts.
2. **Given** a newer version in the catalog, **When** time passes with no operator action, **Then** the installed version does not change.

---

### Edge Cases

- The catalog location is a local folder that does not exist or cannot be read: the platform returns a clear reason.
- The catalog location is a web address that redirects to a different host: the platform follows at most 3 redirects, and only when they stay on the same host and the same scheme. It never sends the token to a different host.
- The index lists a file name that tries to leave the catalog (for example with "..", an absolute path, or a different host): the platform rejects the entry.
- The downloaded file is larger than the existing gamepack size limit: the platform stops the download and returns a clear reason.
- The index lists a version more than once: the platform shows the version once and uses the first entry.
- The changelog text is missing for a version: the list shows the version with an empty changelog.
- The access token must not appear in any response, log message, or error text, and it must not be shown again in full after it is saved.
- Two installs for the same gamepack start at the same time: the existing install flow rules apply; the platform does not add a second rule. A test MUST start two parallel catalog installs of one name and show that the result follows the install flow rules (feature 143).
- The version string in the index is not a valid version: the platform skips that entry and shows the others.

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: The platform MUST let the operator set a catalog location. The location is a web address or a path to a local folder.
- **FR-002**: The platform MUST let the operator set an optional access token for the catalog. The platform MUST NOT return the saved token in any response, and MUST NOT write it to a log.
- **FR-002a**: The platform MUST accept a web address with "https". It MUST accept "http" only for a loopback host, or when the operator sets the "allow insecure" flag. The settings read route MUST return only a flag that tells if a token is set.
- **FR-003**: A change to the catalog location or token MUST take effect for the next catalog request without a restart.
- **FR-004**: The catalog protocol MUST use one index document and file downloads. The index lists each gamepack name, and for each name the versions with the file location, the changelog text, the file hash when the catalog gives it, and an optional detached signature file location.
- **FR-005**: For a web address catalog, the platform MUST send the token, when set, in an Authorization bearer header on every request to the catalog. The platform MUST NOT decide what a token may see. It reports what the catalog returns.
- **FR-006**: For a local folder catalog, the platform MUST read the same index document and files from the folder, and MUST NOT read any file outside the folder.
- **FR-007**: The platform MUST provide a catalog list operation. For each gamepack it returns the offered versions, the changelog text of each version, and a flag that marks the installed version.
- **FR-008**: The platform MUST provide a catalog install operation for one gamepack name and one version. It downloads the file, and downloads the detached signature file when the entry lists one. It runs the signature check of feature 146 under the current signature policy, and passes the signature to it. When the entry has no signature file location, the signature check sees "missing". The platform downloads the signature file with the same rules as the gamepack file (same host, token rule, size limit, and request time limit). Then it installs the gamepack with the install flow of feature 143.
- **FR-009**: The catalog install operation MUST support a dry run. A dry run downloads and checks the file as in a real install, and MUST NOT change the instance.
- **FR-010**: The platform MUST reject an install for a name or version that the index does not list, before any file download. The reason name is "entry_not_listed". The reason "not_found" applies only to a listed file that the catalog answers with "not found".
- **FR-011**: The platform MUST reject an index entry whose file location leaves the catalog base location, and MUST NOT send the token to a host other than the host of the catalog location.
- **FR-012**: The platform MUST limit the size of a downloaded file to the existing gamepack size limit, and MUST limit the size of a detached signature file to 1 MB. The platform MUST also limit the size of the index document and the time of each catalog request, including each file download. Tests MUST cover an oversize signature file, an oversize gamepack file, and a per-request timeout on a file download.
- **FR-013**: When the catalog fails (not set, not reachable, token refused, index not valid, file missing), the platform MUST return a clear reason that names the cause, and MUST NOT change the instance.
- **FR-014**: When an index entry gives a file hash, the platform MUST compare it with the hash of the downloaded file and reject the install on a mismatch. A test MUST show that a correct file hash lets the install pass, and a wrong hash rejects the install.
- **FR-015**: The platform MUST NOT install a gamepack from the catalog unless an operator action starts the install. It MUST NOT poll the catalog to install updates. A test MUST show that no background service or timer type depends on the catalog types. The test MUST be a dependency-injection registration check, not a scan of source text. The check MUST follow the registered constructor graph transitively, so an indirect dependency on the catalog types also fails the test.
- **FR-016**: The web interface MUST have a "Gamepacks" page that shows the catalog list, the changelog text, the installed marker, and an Update button for each version newer than the installed version. The button starts one install and shows the result. The page MUST also have a "dry run" check box and a "Refresh" button. Both start only on operator action. When the check box is on, the install button runs a dry run.
- **FR-017** *(web UI)*: The "Gamepacks" page MUST state its phone support level (full) and follow docs/mobile-ui-principles.md.
- **FR-018**: The catalog operations MUST follow the existing API token rule for all other API routes. No new role is added.
- **FR-019**: The platform MUST write one application log message for each catalog list and each catalog install, with the gamepack name, version, and outcome, and without the token.

### Key Entities

- **Catalog setting**: The catalog location (web address or folder path) and the optional access token. One setting for the instance.
- **Catalog index**: The document that the catalog serves. It holds gamepack entries.
- **Catalog entry**: One version of one gamepack: name, version, changelog text, file location, optional file hash, and optional detached signature file location (same download rules as the gamepack file, 1 MB size limit).
- **Catalog list item**: A gamepack name with its offered versions and the installed marker.

## Assumptions

- The signature policy, trusted keys, install flow, and size limit already exist (features 143 and 146). This feature adds no new rule to them.
- The exact format of the index document is a design choice for the plan. It is plain JSON over HTTP.
- The "installed" marker uses the gamepack registry of the instance.
- A "newer" version uses the same version order as the existing gamepack version check (feature 144).
- The platform does not define who runs a catalog or how the catalog grants access.
- The token and the "allow insecure" flag apply to web addresses only. A folder source ignores them. The docs MUST say this.
- The "Gamepacks" page is added to the existing web UI router and navigation, next to the existing pages. The plan MUST name the actual route and the actual router and navigation files.

## Out of Scope

- Automatic install or auto-update.
- A service that runs a catalog, and rules for access to a catalog.
- A change to the signature check or the install flow.
- A rollback from the catalog page (rollback stays on its own route).

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: An operator can set a catalog, view the list, and install one version in under 3 minutes with no file copy by hand. The manual quickstart walk MUST record the elapsed time and compare it with the 3-minute target.
- **SC-002**: For a catalog of 50 gamepacks with 10 versions each, the list shows in under 5 seconds on a normal connection.
- **SC-003**: In every test of a failing catalog (not set, not reachable, token refused, bad index, missing file, hash mismatch), the instance state is the same as before the request.
- **SC-004**: The access token appears in 0 responses and 0 log messages in all tests.
- **SC-005**: In 100% of tests, no install runs unless an operator action starts it.
- **SC-006**: Every install from the catalog passes the same signature policy as a manual install; a file that a manual install rejects is also rejected from the catalog.
