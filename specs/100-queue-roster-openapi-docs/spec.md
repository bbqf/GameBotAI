# Feature Specification: Make a Queue's Live Roster Discoverable

**Feature Branch**: `100-queue-roster-openapi-docs`  
**Created**: 2026-09-17  
**Status**: Draft  
**Input**: GitHub issue #179 (https://github.com/bbqf/GameBotAI/issues/179) — "B-002: QueueDetailResponse.entries is undocumented, and there is no GET /api/queues/{id}/entries". Closes #179.

## Background

An API consumer wanting to read a queue's current roster (the ordered list of sequences the queue runs) looked for a dedicated "list a queue's entries" route. None exists: the entries path of a queue publishes only "append an entry" and "replace the entries". The roster **is** readable — the single-queue read (get a queue by id) returns an `entries` list — but the consumer only found it by inspecting a real response, not from the published API document. The queue-template read returns the *template's* entries, which is a different list: a queue's own entries can differ from its linked template's (a running queue keeps the entries it had when it started).

The issue's own suggested fix: document `entries` on the single-queue read so the read path is visible without probing; a dedicated entries-list route is "nice-to-have but not needed if the detail response is documented".

### Verified current state (master fea4cad and the live published document, 2026-09-17)

The issue's headline claim does not fully reproduce: the published example for the single-queue read (and for "replace a queue's entries") already shows an `entries` list. The gaps that remain:

1. The `entries` field of the single-queue response has **no description** in the published document, and is published as *nullable*, although the service always returns a list (empty when the queue has no entries) and never null.
2. The fields of an entry (`entryId`, `sequenceId`, `sequenceName`, `stale`) have no descriptions — e.g. that `sequenceName` is null and `stale` is true when the referenced sequence no longer exists, or that `entryId` is the id used to remove that one entry.
3. Nothing says that `entries` is the queue's **own live roster** (as opposed to its linked template's entries) or that the single-queue read is **the** way to read it.
4. The "append an entry" and "replace the entries" operations — where a consumer looking for the "obvious route" lands — carry no description pointing to where the roster is read.
5. The single-queue read's operation description discusses only the live `health` block, not `entries`.

## Clarifications

### Session 2026-09-17

- Q: Should a dedicated `GET /api/queues/{id}/entries` route be added? → A: No. The issue states it is not needed once the detail response is documented; the read path is made discoverable by documentation instead. (Rationale: avoids new API surface the issue explicitly does not require.)
- Q: Should `entries` be declared as always present (required) in the published shape, beyond dropping "nullable"? → A: No — drop "nullable" and state "always present, never null" in the description, but do not add a schema-level required list. (Rationale: no queue shape in this document declares required fields — not even `id` — so marking only `entries` required would be inconsistent within the queue contracts and exceed the issue.)
- Q: Should the entry-field descriptions apply wherever the entry shape is published (e.g. the "append an entry" 201 response too), or only inside the single-queue read? → A: Wherever the entry shape is published — the shape is shared, so its field descriptions travel with it. (Rationale: descriptions attach to the shared shape; the append response returns the same shape.)
- Q: When `entries` stops being published as nullable, should its read-only marking stay? → A: Yes, keep it read-only. (Rationale: it is accurate — the field appears only in responses and the roster is changed through the entries operations — and removing it would be an unrequested contract change.)

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Find how to read a queue's roster from the published document (Priority: P1)

An API consumer (human or automation client) wants to inspect which sequences a queue will run. Reading the published API document — whether they start at the queue's entries path or at the single-queue read — they learn that the roster is the `entries` list returned by the single-queue read, that it is the queue's own live roster rather than its template's, and what each entry field means, without issuing an exploratory request.

**Why this priority**: This is the whole of the reported defect.

**Independent Test**: Fetch the published API document; from the entries path's operations and from the single-queue read, the read path and the meaning of `entries` and its fields are stated.

**Acceptance Scenarios**:

1. **Given** the published API document, **When** a consumer reads the single-queue response shape, **Then** the `entries` field has a description saying it is the queue's current ordered roster, always a list (empty when there are no entries, never null), the queue's own entries rather than its linked template's, and the way to read the roster (there is no separate entries-list route).
2. **Given** the published API document, **When** a consumer reads the single-queue response shape, **Then** `entries` is not marked nullable.
3. **Given** the published API document, **When** a consumer reads the entry shape, **Then** `entryId`, `sequenceId`, `sequenceName` and `stale` each have a description, including that `entryId` identifies the entry for removal, that `sequenceName` is null when the referenced sequence no longer exists, and that `stale` is true in that case.
4. **Given** the published API document, **When** a consumer reads the "append an entry" or "replace the entries" operation, **Then** its description tells them the roster is read from the single-queue read's `entries`.
5. **Given** the published API document, **When** a consumer reads the single-queue read operation, **Then** its description mentions that `entries` is the queue's roster, and the existing `health` explanation is still present.
6. **Given** the published API document, **When** a consumer reads the single-queue read's success example, **Then** it still shows an `entries` list.

---

### User Story 2 - The roster documentation cannot silently regress (Priority: P2)

A maintainer changing the queue contract or its documentation is warned by an automated test if the roster documentation disappears from the published API document.

**Why this priority**: Protects the fix; secondary to publishing it.

**Independent Test**: Run the automated test suite; a test fails if any of the statements in User Story 1 is missing from the published API document.

**Acceptance Scenarios**:

1. **Given** the roster documentation is published, **When** the automated tests run, **Then** tests confirm each statement from User Story 1's scenarios is present.
2. **Given** the documentation changes, **When** a client reads a queue by id, appends an entry, or replaces the entries, **Then** the response bodies and status codes are exactly as before.

### Edge Cases

- A queue with no entries: the description must make clear the consumer receives an empty list, not null or an absent field.
- A queue whose entry references a deleted sequence: `sequenceName` is null and `stale` is true — both descriptions must say so consistently.
- A running queue whose linked template was edited after start: the description must not imply `entries` mirrors the template.
- The entry shape is also the "append an entry" success response: its field descriptions must read correctly in that context too (no wording that only makes sense inside the single-queue read).
- The single-queue read's existing `health` description must be preserved verbatim in meaning; the roster sentence is additive.

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: The published API document MUST describe the single-queue response's `entries` field as the queue's current ordered roster, always a list (empty when the queue has no entries, never null), the queue's own entries rather than its linked template's entries, and the way to read the roster (no separate entries-list read route exists).
- **FR-002**: The published API document MUST NOT mark the single-queue response's `entries` field as nullable; its existing read-only marking MUST be kept.
- **FR-003**: The published entry shape MUST describe `entryId` (identifies this entry; used to remove it via the remove-entry operation), `sequenceId` (the sequence this entry runs), `sequenceName` (the referenced sequence's current name; null when that sequence no longer exists) and `stale` (true when the referenced sequence no longer exists).
- **FR-004**: The single-queue read operation's description MUST state that the response's `entries` is the queue's roster, while keeping the existing `health` explanation.
- **FR-005**: The "append an entry" and "replace the entries" operations MUST each carry a description directing readers to the single-queue read's `entries` to read the roster.
- **FR-006**: The single-queue read's published success example MUST continue to include an `entries` list.
- **FR-007**: Automated tests MUST fail if any of FR-001 through FR-006 stops holding in the published API document.
- **FR-008**: No route may be added or removed, and no response body, status code or runtime behaviour may change.
- **FR-009**: Every statement in the new descriptions MUST match the service's actual behaviour at the time of change (re-verified against the code, not copied from the issue).

### Key Entities

- **Queue roster (entries)**: the ordered list of entries a queue runs; each entry references one sequence. Owned by the queue itself; may differ from the linked template's entries.
- **Queue entry**: one roster item — an entry id, a referenced sequence id, the referenced sequence's resolved name, and a stale flag for a missing sequence.
- **Published API document**: the machine-readable API description the service serves to clients.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: A consumer can identify the read path for a queue's roster from the published API document alone, starting from either the queue's entries path or the single-queue read, with zero exploratory requests.
- **SC-002**: 100% of the fields of the entry shape (4 of 4) and the `entries` field carry a description in the published document.
- **SC-003**: Removing any of the new descriptions, re-marking `entries` nullable, or dropping `entries` from the single-queue example causes at least one automated test to fail.
- **SC-004**: All existing queue API tests pass unchanged (no behavioural change).

## Assumptions

- The published API document is the service's generated OpenAPI document; the service does not feed code comments into it, so descriptions are added the same way recent contract documentation (queue health, image alternates, sequence nesting rules) was published.
- Removing "nullable" from `entries` is a documentation correction, not a contract change: the service has never returned null there.
- "Remove an entry" refers to the existing remove-entry operation on a queue's entries path.

## Out of Scope

- Adding a dedicated entries-list read route for a queue.
- Changing queue-template reads or template/queue synchronisation semantics.
- Filling other fields (`health`, `failurePolicy`, etc.) into the queue examples, or documenting unrelated queue fields.
- Web UI changes.
