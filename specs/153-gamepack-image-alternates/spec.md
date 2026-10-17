# Feature Specification: Gamepack image carries its alternates list
<!-- Term rule: use "alternates list" for the list. `alternates` (code font) is only the member name. -->


**Feature Branch**: `153-gamepack-image-alternates`
**Created**: 2026-10-17
**Status**: Draft
**Input**: GitHub issue #324 (FR-030): a gamepack image must carry its `alternates` list, or the platform must refuse an unknown member of an image.

## Clarifications

### Session 2026-10-17

- Q: Does the list limit of 8 apply? -> A: Yes. The same rules apply, so a list with more than 8 ids is an error at dry run. Reason: FR-003 reuses the existing rules.
- Q: Does the unknown-member refusal apply to other object types (sequences, commands)? -> A: No. It applies to images only. Reason: the issue and the non-goals limit the scope to images.
- Q: How does the file report an unknown member when the file holds a known member with a wrong type? -> A: Wrong type for `alternates` is an error that names the image. Other known members keep their current checks. Reason: no change to current behaviour.
- Q: What happens if an alternates id names an image that the same install removes or replaces? -> A: The id is valid if the image exists after the install. An id of an image that the install removes is unknown. Reason: the list must be valid after install.
- Q: Does a re-install of a gamepack with changed alternates update the stored list? -> A: Yes. The install writes the declared list, also when the image bytes are unchanged. An image with no `alternates` member keeps its stored list. Reason: the declared list is the source of truth when present; absence is not a request to clear.
- Q (loop 1, I1): Is the "declared whole placeholder image ids" of PR #328 a member of the image? -> A: No. It is a rule for sequence parameters, not an image member. The known image members are `id`, `contentType`, `filename`, `dataBase64`, and `alternates`. Reason: the current image model has no placeholder member.
- Q (loop 1, A1): What does an empty `alternates` list do at re-install? -> A: It clears the stored list. Reason: an empty list is a declared list; only an absent member keeps the stored list.
- Q (loop 1, U1): Can an alternates id name an installed image that the install removes? -> A: No. FR-004 is valid only for images that exist after the install.
- Q (loop 1, U2): What does the message name when the image entry has no id or is not an object? -> A: It uses the existing fallback name of the image (its position in the list), and the unknown member or the type error is still reported.
- Q (loop 2, C1): How is FR-007 proven? -> A: A test installs a file with no `alternates` member and checks that the stored list does not change and that the file bytes of the image do not change. Reason: this shows no behaviour change for old files.
- Q (loop 2, A1): What text does an alternates list error use? -> A: The error names the image id and the offending ids, and uses the message and hint of the existing alternates rules. Reason: one message source for the API and the gamepack.
- Q: What is the error category for these problems? -> A: Structure problems of type Image, like the existing image checks. Reason: consistent reporting.

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Install an image with its alternates (Priority: P1)

A gamepack author lists, for an image, other image ids that also match (for example the Alliance button of a farm account and the Faction button of an event account). The buyer installs the gamepack. After install, the image has the same alternates list that the gamepack declared.


**Why this priority**: Without the list, the match of an event account fails after install, and nothing warns the buyer. This is the main goal of the issue.

**Independent Test**: Dry run and install a gamepack with one image that has `"alternates":["<other-image-id>"]`. Then read the alternates list of the image and compare.

**Acceptance Scenarios**:

1. **Given** a gamepack with an image that has an alternates list of known image ids, **When** the user runs the dry run, **Then** the dry run is valid.
2. **Given** the same gamepack, **When** the user installs it, **Then** the alternates list of the installed image is the same list that the gamepack declared.
3. **Given** a gamepack image with no `alternates` member, **When** the user installs it, **Then** the install works as before and the image has an empty alternates list.

---

### User Story 2 - Refuse an unknown image member (Priority: P1)

A gamepack image has a member that the platform does not know (for example `"bogusMember":1`). The platform must not accept the member and drop it without a message.

**Why this priority**: A silent drop hides data loss from the buyer. The issue states that this refusal is required.

**Independent Test**: Run the dry run on a gamepack with an image that has `"bogusMember":1`. Check the result and check that install is refused.

**Acceptance Scenarios**:

1. **Given** a gamepack image with an unknown member, **When** the user runs the dry run, **Then** the result is not valid and an error names the image id and the member name.
2. **Given** the same gamepack, **When** the user tries to install it, **Then** the install is refused and no image, and no other object of the gamepack, is created.

---

### User Story 3 - Report a bad alternates list at dry run (Priority: P2)

A gamepack image has an `alternates` list that breaks the existing alternates rules (for example an unknown image id, or a reference to the image itself). The dry run reports the problem.

**Why this priority**: The author finds the error before install. The rules are the same as for the existing alternates API, so the behaviour is consistent.

**Independent Test**: Run the dry run on gamepacks with a self reference, an unknown id, and a duplicate id in the list. Check that each is reported as an error with the image id.

**Acceptance Scenarios**:

1. **Given** a gamepack image whose alternates list holds its own id, **When** the user runs the dry run, **Then** the result is not valid and an error names the image and the problem.
2. **Given** a gamepack image whose alternates list holds an image id that is not in the gamepack and not installed, **When** the user runs the dry run, **Then** the result is not valid and an error names the image and the unknown id.
3. **Given** a gamepack file with an invalid alternates list (for example an unknown id or a self reference), **When** the user tries to install it, **Then** the install is refused and zero objects of the gamepack are created.

---

### Edge Cases

- An id in an alternates list points to another image in the same gamepack: this is valid, because both images exist after install.
- An id in an alternates list points to an image that is already installed on the platform: this is valid.
- The `alternates` member is not a list, or has a non-text item: the dry run reports an error that names the image.
- An empty `alternates` list is valid and gives an empty list.
- Install of an image whose alternates list is stored must not change the existing `PUT /api/images/{id}/alternates` behaviour.
- The known members of an image (`id`, `contentType`, `filename`, `dataBase64`, `alternates`) stay accepted.
- An empty `alternates` list at re-install clears the stored list. An absent `alternates` member keeps the stored list.

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: A gamepack image MUST be able to carry an `alternates` list of image ids.
- **FR-002**: On install, the system MUST store the declared alternates list for the image, so that reading the alternates list of the image returns the same list.
- **FR-003**: The system MUST validate the declared alternates list with the same rules as the existing alternates API (for example unknown reference, self reference, duplicate). The dry run MUST report each violation as an error that names the image.
- **FR-004**: An id in an alternates list MUST be valid if it names an image in the same gamepack or an installed image that the install does not remove. An id of an image that the install removes is unknown.
- **FR-005**: The dry run MUST report each unknown image member as an error, and the result MUST NOT be valid. The message MUST name the image id and the member name.
- **FR-006**: The install MUST refuse a gamepack that has an unknown image member or an invalid alternates list. It MUST NOT create a partial result.
- **FR-007**: The system MUST NOT change the behaviour of the existing alternates API, other image members, image matching in detection, or already installed images.
- **FR-008**: Tests MUST cover: alternates list carried through install, unknown member refused at dry run, invalid alternates list reported at dry run, install refused with zero objects created for an unknown member, and install refused with zero objects created for an invalid alternates list.

### Key Entities

- **Gamepack image**: an image that a gamepack declares. It has an id, its image data, and now an optional `alternates` list.
- **Alternates list**: a list of other image ids that also match for the image. It follows the existing alternates rules.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: For a gamepack with an image that has N alternates, the alternates list of the installed image has the same N ids in 100% of installs.
- **SC-002**: 100% of gamepack images with an unknown member give a not-valid dry run with a message that names the image id and the member.
- **SC-003**: 100% of invalid alternates lists (unknown id, self reference, duplicate) are reported at dry run, before any install.
- **SC-004**: A refused install (unknown member or invalid alternates list) leaves zero new objects on the platform.

## Assumptions

- The alternates list rules are the ones in the existing alternates validation; this feature reuses them and adds no new rules.
- An id in an alternates list can name an image of the same gamepack because the install creates all images before the alternates lists are stored.
- A gamepack with no `alternates` member is valid, and the stored alternates list is empty.
- The web UI does not change; there is no UI requirement.
- The change touches the Domain gamepack files and the one DI registration in the Service project (`GamepackServiceCollectionExtensions.cs`). The API endpoints do not change.
- The take-over path (`GamepackApplier.TakeOver.cs`) needs alternates handling. A take-over of an image writes the new alternates list to the live image. The take-over backup and the undo snapshot of an image today hold only the bytes, content type and file name. They must also hold the old alternates list, and a restore or undo must put it back. If they do not, a restore leaves the alternates list of the gamepack on an image that is the user's again.
