# Feature Specification: Duplicate Queues

**Feature Branch**: `083-duplicate-queues`
**Created**: 2026-09-11
**Status**: Implemented
**Input**: User description: "I want to be able to duplicate queues. The duplicated queue should be an 1:1 copy of the original, using the same template, just the name must be different"

**Amendment (2026-09-12)**: The original spec required the emulator target to be copied verbatim
like every other field, which made it impossible to duplicate a queue onto a different emulator —
the entire practical reason to duplicate a queue configuration is often to run it on another
device. The emulator fields (serial, instance name, instance index) are now, like the name,
resubmitted by the caller at duplication time and MAY be changed — but unlike the name, they are
also allowed to come back unchanged. See FR-003b and Acceptance Scenario 6.

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Duplicate an existing queue (Priority: P1)

A user managing execution queues has an existing queue configured the way they want (emulator target, template link, run options) and wants to create another queue with the same configuration, differing only in name, without re-entering every setting by hand.

**Why this priority**: This is the entire feature. Without it, users must manually recreate every field of a queue (emulator serial/instance, cycle execution, pause-when-idle, idle threshold, linked template, linked game) whenever they want a near-identical queue — slow and error-prone.

**Independent Test**: Can be fully tested by duplicating a queue that has a linked template and linked game, providing a new name, and verifying the resulting queue's configuration matches the source field-for-field except for name and identifier.

**Acceptance Scenarios**:

1. **Given** an existing queue "Daily Farming" linked to template "Farm Template" with a linked game and specific emulator settings, **When** the user duplicates it, supplies the name "Daily Farming 2", and leaves the pre-filled emulator fields unchanged, **Then** a new queue is created with the same emulator serial, instance name/index, cycle-execution flag, pause-when-idle flag, idle threshold, linked template reference, and linked game reference as "Daily Farming", but with the name "Daily Farming 2" and its own identifier, and its currently loaded entries (sequences) match "Daily Farming"'s entries at the time of duplication.
2. **Given** the newly duplicated queue, **When** the user inspects it, **Then** it is stopped (not running) and has no execution history, runtime schedule progress, or status carried over from the source queue, even if the source queue was running at the time of duplication.
3. **Given** a queue with no linked template and no linked game, **When** the user duplicates it, **Then** the new queue is also created with no linked template and no linked game (the absence is copied faithfully).
4. **Given** the user is duplicating a queue, **When** they submit the same name as the source queue, **Then** the system rejects the duplication with a clear error and no new queue is created.
5. **Given** a queue is successfully duplicated, **When** the user inspects the original source queue afterward, **Then** it is completely unchanged (same name, settings, links, and runtime status).
6. **Given** an existing queue bound to emulator "emu-1", **When** the user duplicates it, supplies a new name, and changes the emulator field to "emu-2" (and/or a different instance name/index) before confirming, **Then** the new queue is created bound to "emu-2" (and the supplied instance name/index) while every other field is still copied from the source, and the source queue's own emulator binding is unaffected.

### Edge Cases

- Duplicating a queue while it is actively running: the action is still allowed; the new queue is created in a stopped state (see FR-005) and the source queue's running status is unaffected.
- Duplicating a queue whose linked template or linked game has since been deleted/is missing: the duplicate copies the reference as-is (same behavior as the source queue already exhibits when displaying a broken link).
- Submitting an empty name for the duplicate: rejected the same way an empty name is rejected when creating any queue.
- Duplicating the same source queue multiple times in a row: each duplication is independent and only needs to differ in name from the immediate source queue, not from other previously created duplicates.

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: Users MUST be able to trigger duplication of an existing queue from the queue management view.
- **FR-002**: When duplicating, the system MUST prompt the user for a name for the new queue, pre-filled with a suggested name derived from the source queue's name (e.g. "<source name> (copy)"), which the user can accept or edit before confirming.
- **FR-003**: The system MUST create a new queue that copies every configuration field from the source queue as-is: cycle-execution flag, pause-when-idle flag, idle threshold, linked template reference, and linked game reference.
- **FR-003a**: The system MUST also copy the source queue's currently loaded entries (its ordered list of sequences), so the duplicate matches the source's actual current state immediately, even if those entries have not been saved into the linked template.
- **FR-003b**: The emulator fields (emulator serial, emulator instance name, emulator instance index) MUST be presented to the user pre-filled with the source queue's current values, editable before confirming, and applied to the new queue as submitted. Unlike the name (FR-006), the emulator fields MAY be submitted unchanged from the source's values — duplication is the only way to create a new queue with a source queue's non-emulator configuration but a different emulator target.
- **FR-004**: The duplicated queue MUST reference the same template as the source queue (a link to the existing template), not a new copy of the template.
- **FR-005**: The system MUST assign the duplicated queue a new, distinct identifier and MUST NOT copy the source queue's runtime state — the duplicate is created stopped, with no execution history and no in-progress schedule/runtime entries, regardless of the source queue's state at duplication time.
- **FR-006**: The system MUST reject duplication when the submitted name for the new queue is identical to the source queue's current name, returning a clear validation error and creating no new queue.
- **FR-007**: The system MUST NOT modify the source queue in any way as a result of duplication.
- **FR-008**: Duplication MUST be available regardless of whether the source queue is currently running, stopped, or idle.

### Key Entities

- **Queue**: A user-configured, persisted execution target (emulator binding, run behavior flags, and a link to a template and optionally a game). Duplication acts on this entity, copying its configuration and its currently loaded entries (sequences) into a new queue with a new identity and a different name.
- **Queue Template**: A named, reusable ordered set of scheduled sequence entries that a queue links to. Not duplicated by this feature — the new queue links to the same template instance as the source.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: A user can create a fully-configured duplicate of an existing queue in a single action plus entering one new name, instead of manually re-entering every setting field.
- **SC-002**: 100% of configuration fields and currently loaded entries on a duplicated queue (excluding name, identifier, and the emulator fields when the user chose to change them) match the source queue immediately after duplication.
- **SC-003**: Duplicating a queue never alters the source queue's configuration or runtime status.
- **SC-004**: Attempting to duplicate a queue with an unchanged name is rejected before any new queue is persisted.

## Assumptions

- Queue names are not required to be globally unique (confirmed by existing queue validation behavior), so the only naming constraint duplication introduces is that the duplicate's name must differ from the immediate source queue's name at the time of duplication — not from all other queue names in the system.
- "1:1 copy... using the same template" means the duplicate links to the same template record as the source; it does not create an independent copy of the template itself.
- Runtime state (running/stopped status, in-progress schedule position, execution history) is scoped to a queue's own identity and is intentionally never copied — a duplicate always starts stopped/idle, consistent with how a newly created queue behaves today.
- The emulator fields are the one part of the configuration that is not a strict 1:1 copy: they are pre-filled from the source but resubmitted by the caller, so they can be left as-is or changed. This mirrors how `PUT /api/queues/{id}` already lets an operator change a queue's emulator instance name/index after creation, except duplication also allows changing the emulator serial itself, which a normal edit does not permit.
