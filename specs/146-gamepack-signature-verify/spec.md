# Feature Specification: Verify the signature of a gamepack before install

**Feature Branch**: `146-gamepack-signature-verify`  
**Created**: 2026-10-16  
**Status**: Draft  
**Input**: User description: "Verify the detached signature of a gamepack before install. GitHub issue #307 (FR-024). Closes #307."

## Clarifications

### Session 2026-10-16

- Q: Which status wins when a gamepack has more than one fault? → A: The order is: missing, invalid, content mismatch, untrusted. The first one that applies is the status. (Rationale: a deterministic result; FR-010.)
- Q: Does the policy apply to a gamepack that is already installed? → A: No. It applies only to a new install and to a dry run. An installed gamepack is not checked again. (Rationale: matches Non-goals; rollback uses data the instance already holds.)
- Q: Who may change the policy and the keys? → A: Any caller that may change instance settings today. No new role. (Rationale: Assumptions.)
- Q: How does the operator change the settings? → A: Through the existing configuration and the API of the instance. The change takes effect for the next install, without a restart. (Rationale: SC-005; no UI in scope.)
- Q: Is a gamepack with a valid signature but a version or compatibility problem rejected by this feature? → A: No. The existing version checks stay as they are and run in their own order. (Rationale: FR-015.)

### Session 2026-10-16 (loop 1, from analyze findings)

- Q (A1): What does "read a signature" mean under "off"? → A: The signature text is loaded or parsed. Under "off" the install route does not pass the signature text to the service, and no verifier runs. (Rationale: SC-004 is testable at route level.)
- Q (A2): Who may change the policy and keys? → A: The existing API token rule for all other API routes. No new role. A test checks each settings route against this rule. (Rationale: no "settings permission" exists.)
- Q (A4): What happens when the gamepack cannot be parsed under "required"? → A: The existing parse error is returned, as without this feature. The signature check starts after parse. A dry run that the signature gate rejects returns only the signature reason. (Rationale: FR-015.)
- Q (A5): How are the status names spelled? → A: In the API and logs the names are snake_case: valid, missing, invalid, untrusted, content_mismatch, not_checked. The display words in FR-010 map one to one. (Rationale: one spelling in contracts.)
- Q (A6): What is the "log of the install" in FR-016? → A: The application log. One message per check. (Rationale: matches the plan.)
- Q (A10): What is "instance state" in SC-002? → A: The gamepack registry, sequences, commands, images, and the signature settings file. (Rationale: testable comparison.)
- Q (A11): Does canonical JSON need string and number rules? → A: Yes. The canonicalizer uses one fixed form for string escapes and numbers, and a test covers it. (Rationale: equal content must give equal hashes.)
- Q (A12): Is rollback or list checked? → A: No. A test shows that rollback and list run no signature check under "required". (Rationale: FR-015.)

### Session 2026-10-16 (loop 2, from analyze findings)

- Q (F1): When does the "off" skip of the signature part apply? → A: From the first version of the install route. The route never loads the signature part under "off"; the route-level test is owned by User Story 3. (Rationale: FR-009 holds in every story.)
- Q (U1): What does the dry-run gate check produce? → A: The rule in FR-014 only: wouldReject is true under "required" with a status other than valid. No extra "check and fix" step. (Rationale: concrete output.)
- Q (C1): Is a route-level test for "add key, then install, no restart" needed? → A: Yes, in the trust contract test. (Rationale: SC-005.)

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Reject a changed gamepack (Priority: P1)

An operator sets the signature policy of the instance to "required" and adds the trusted public key of the builder. The operator installs a gamepack. The system checks the detached signature over the manifest and the content hashes. If the check does not pass, the system rejects the gamepack before it changes anything. The working automations stay as they were.

**Why this priority**: This is the core value. A gamepack can be altered on the way from the builder to the instance. The install replaces working automations, so the check must stop a bad gamepack first.

**Independent Test**: Set policy "required" with one trusted key. Install a correctly signed gamepack: it installs. Install gamepacks with a missing signature, a broken signature, a signature from an untrusted key, and changed content: each is rejected and the instance state is the same as before.

**Acceptance Scenarios**:

1. **Given** policy "required" and a trusted key, **When** the operator installs a gamepack with a valid signature, **Then** the install succeeds and the result reports the signature status "valid".
2. **Given** policy "required", **When** the operator installs a gamepack with no signature, **Then** the install is rejected with a clear reason and nothing changes on the instance.
3. **Given** policy "required", **When** the operator installs a gamepack with a signature that is not valid, **Then** the install is rejected and nothing changes.
4. **Given** policy "required", **When** the operator installs a gamepack signed with a key that is not trusted, **Then** the install is rejected and nothing changes.
5. **Given** policy "required", **When** the content of the gamepack does not match the signed hashes, **Then** the install is rejected and nothing changes.

---

### User Story 2 - Report the signature status without blocking (Priority: P2)

An operator sets the policy to "optional". The install proceeds for every gamepack, and the install result tells the operator the signature status. The operator can use this mode to start with signatures before the policy is made strict.

**Why this priority**: It gives a safe step toward "required" and shows what "required" would do.

**Independent Test**: Set policy "optional". Install a valid, a missing, an invalid, an untrusted, and a changed-content gamepack. Each installs. Each result shows its own status.

**Acceptance Scenarios**:

1. **Given** policy "optional", **When** the operator installs a gamepack with a valid signature, **Then** it installs and the result reports "valid".
2. **Given** policy "optional", **When** the operator installs a gamepack with no signature, **Then** it installs and the result reports "missing".
3. **Given** policy "optional", **When** the operator installs a gamepack with a bad signature, an untrusted key, or changed content, **Then** it installs and the result reports the matching failure status.

---

### User Story 3 - Configure the policy and the trusted keys (Priority: P2)

An operator sets the policy (required, optional, or off) and manages the list of trusted public keys on the instance. With policy "off" the system does not check any signature.

**Why this priority**: The two other stories need these settings.

**Independent Test**: Change the policy and the keys through the instance settings. Check that the next install uses the new values. With "off", check that an install does not read any signature.

**Acceptance Scenarios**:

1. **Given** policy "off", **When** the operator installs any gamepack, **Then** no signature check runs and the install follows the existing install behavior.
2. **Given** no trusted key and policy "required", **When** the operator installs a signed gamepack, **Then** the install is rejected because no key is trusted.
3. **Given** two trusted keys, **When** a gamepack is signed with either key, **Then** the signature is valid.
4. **Given** a new instance with no setting, **When** the operator installs a gamepack, **Then** the default policy applies (see Assumptions).

---

### Edge Cases

- A dry run of an install (existing feature) reports the signature status and the decision, and does not change the instance.
- A rollback does not run a signature check (it restores data that the instance already holds).
- The signature file is present but empty, or not in a known format: the status is "invalid".
- A gamepack contains a file that the signed hashes do not list, or the hashes list a file that is not in the gamepack: the status is "content mismatch".
- A trusted key entry is malformed: the system rejects it when the operator saves it, and does not trust it silently.
- Two checks of the same gamepack with the same keys give the same status.

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: A gamepack MAY carry one detached signature. The signature covers the manifest and the hash of each content file of the gamepack.
- **FR-002**: The system MUST check the signature before it changes any state of the instance during an install.
- **FR-003**: The system MUST compute the hash of each content file and compare it with the signed hashes. A difference, an extra file, or a missing file MUST give the status "content mismatch".
- **FR-004**: The instance MUST hold zero or more trusted public keys. The operator MUST be able to add, list, and remove them.
- **FR-005**: The signature is valid only when it verifies with at least one trusted key. A signature that verifies only with a key that is not trusted MUST give the status "untrusted".
- **FR-006**: The instance MUST have one signature policy setting with the values "required", "optional", and "off".
- **FR-007**: With policy "required", the system MUST reject a gamepack whose status is not "valid", and MUST leave the instance unchanged (no partial install).
- **FR-008**: With policy "optional", the system MUST install the gamepack in the same way as without this feature, whatever the status, and MUST report the status in the install result.
- **FR-009**: With policy "off", the system MUST NOT read or check any signature. The install result MUST state that no check ran.
- **FR-010**: The signature status MUST be one of: `valid`, `missing`, `invalid`, `untrusted`, `content_mismatch`, `not_checked`. When more than one fault applies, the order in the Clarifications decides the status.
- **FR-011**: A rejection MUST give a reason that names the status, and MUST NOT give details that help a forger.
- **FR-012**: The check MUST NOT depend on who signed. The system MUST NOT use a name, a publisher, a certificate, or an authority to decide trust. Trust is only "the key is on the trusted list".
- **FR-013**: The trusted keys and the policy MUST be stored on the instance and stay after a restart.
- **FR-014**: A dry run MUST report the signature status and whether the policy would reject the gamepack, and MUST NOT change the instance. The field "wouldReject" is true only when the policy is "required" and the status is not `valid`. Under "optional" and "off" it is always false.
- **FR-015**: Apart from the signature gate, the install, list, rollback, and dry-run behavior MUST NOT change.
- **FR-016**: The system MUST record each signature check result (status and policy, no key secret) in the application log, one message per check. A test MUST be able to read the log record and MUST show that it holds the status and the policy and does not hold any key secret.
- **FR-017**: The documentation MUST describe the signature format, how to sign a gamepack, how to add a trusted key, and the three policies.

### Key Entities

- **Signature**: A detached statement from a builder over the manifest and the content hashes of one gamepack. It is separate from the content files.
- **Trusted key**: A public key that the operator added to the instance. It has an operator-given label and a fingerprint. The label is for display only and has no effect on trust.
- **Signature policy**: A setting of the instance with the values required, optional, off.
- **Signature status**: The result of the check for one gamepack: valid, missing, invalid, untrusted, content mismatch, or not checked.

## Assumptions

- The default policy is "off", so existing instances and existing installs behave as before until an operator changes the setting.
- The signing algorithm and file format are free choices of the platform. The plan selects a common, well-reviewed public-key signature algorithm that the platform runtime supports without a new dependency.
- A gamepack has a manifest and a list of content hashes already (feature "gamepack install"). If the existing manifest has no hash list, the plan adds a signed hash list in the signature file.
- The settings are changed by an operator who is already allowed to change instance settings. No new role is added.
- No change to the web UI is in scope. The settings are available through the existing configuration and API surface.
- The gamepack service depends on the signature verifier and the settings store. The existing gamepack tests and the dependency-injection wiring MUST keep working without change.
- The new service is registered before the gamepack service uses it. The dependency-injection wiring goes in the existing service-setup file.
- The request and response types (settings, key, status, dry-run result) go in the existing endpoint DTO folder, and they exist before the routes that use them.
- Each requirement has one test owner. The checks for FR-009 and SC-004 (no signature read under "off") are covered under User Story 3 only. User Stories 1 and 2 do not repeat them.

## Non-goals

- No trust model that depends on who signed (no identity or publisher rules, no certificate authority).
- No remote catalog and no download of gamepacks (issue #308).
- No change to install, rollback, or dry-run behavior other than the signature gate.
- No signing tool for the web UI. A command-line or script aid for builders is only in scope if the plan finds it needed to test and document the format.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: A gamepack with a valid signature from a trusted key installs under all three policies in 100% of test runs.
- **SC-002**: Under "required", each of the four bad cases (missing, invalid, untrusted, changed content) is rejected in 100% of test runs, and the instance state before and after is identical.
- **SC-003**: Under "optional", each of the same bad cases installs, and the result shows the correct status in 100% of test runs.
- **SC-004**: Under "off", a test that counts signature reads finds zero reads.
- **SC-005**: An operator can change the policy and add a trusted key, and the next install uses them, without a restart.
- **SC-006**: A test with the same content signed by two different unrelated keys, both trusted, gives the same status "valid". The status does not change with the signer name.
- **SC-007**: The signature check adds less than 2 seconds to the install of a gamepack of 50 MB.
