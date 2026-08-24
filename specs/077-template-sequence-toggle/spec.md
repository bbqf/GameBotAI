# Feature Specification: Enable/Disable Template Sequences

**Feature Branch**: `077-template-sequence-toggle`
**Created**: 2026-07-30
**Status**: Implemented
**Input**: User description: "I want to be able to edit template and to turn the sequences on and off, so that when they're turned off, they don't disappear from the template, but are ignored during the queue run. The feature has to be available on the UI with a on/off switch and this has to be persisted to the template."

## Clarifications

### Session 2026-07-30

Resolved autonomously (pipeline runs without manual review); each answer is the most reasonable option given the spec and the existing codebase, with a one-line rationale.

- Q: Where does the on/off switch live? → A: On each sequence entry (card) in the template editing surface (the scheduling-areas editor where entries are ordered and scheduled) — not a separate screen and not on the read-only runtime monitor. *Rationale*: the request says "edit template," and that editor already owns per-entry state (order, schedule); runtime entries are derived from the template at start.
- Q: What persists the on/off state? → A: The existing template save action — the state is part of the template save payload alongside order and schedule, so it is written to durable template storage. *Rationale*: matches the established save flow (`SaveQueueTemplateRequest` entries) and the requirement to persist "to the template."
- Q: When does disabling take effect relative to a run? → A: At run build time (queue start), when runtime entries are loaded from the template; a change made mid-run applies to the next run. *Rationale*: a queue already reloads its runtime from the template on start, so excluding disabled entries there is the natural, least-surprising integration point.

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Temporarily disable a sequence without removing it (Priority: P1)

While editing a queue template, an operator wants to stop one of its sequences from running for a while — for example a task that is broken, seasonal, or being tested — without losing its position, schedule, or the fact that it belongs to this template. The operator flips an on/off switch on that entry to "off"; the entry stays visible in the template with its schedule intact, and the next queue run ignores it. Later the operator flips it back "on" and it runs again on the next run.

**Why this priority**: This is the entire feature. Every part of the request — the switch, persistence, and "ignored during the run but not removed" behavior — is exercised by this single journey. It delivers a complete, usable MVP on its own.

**Independent Test**: Open a template that has at least one sequence entry, toggle an entry off, reload the page (to prove persistence), start the linked queue, and confirm that the disabled sequence never executes while the still-enabled sequences do; then toggle it back on and confirm it runs on the following run.

**Acceptance Scenarios**:

1. **Given** a template with an enabled sequence entry, **When** the operator toggles that entry off and saves, **Then** the entry remains listed in the template in the same position with its schedule unchanged and is shown in a visibly "off" state.
2. **Given** an entry that was toggled off and persisted, **When** the operator reloads the template editor, **Then** the entry is still shown as off (the state survives reload and service restart).
3. **Given** a linked queue whose template has a disabled entry, **When** the queue run starts and executes, **Then** the disabled sequence is skipped for the entire run while all enabled entries run according to their schedule.
4. **Given** a disabled entry, **When** the operator toggles it back on and saves, **Then** the sequence is included again and runs on the next queue run.
5. **Given** a template where every entry is disabled, **When** the queue starts, **Then** the run is valid and simply executes no sequences (no error).

---

### Edge Cases

- **Duplicate sequence in a template**: The same sequence id may appear more than once in a template. Each entry's on/off state is independent — disabling one occurrence does not disable the others.
- **Toggling while the queue is running**: A queue loads its runtime entries from the template when it starts. Changing an entry's on/off state while a run is in progress takes effect on the **next** run start, not mid-run; the current run is unaffected. (Consistent with existing template-vs-runtime behavior.)
- **Legacy templates**: Templates saved before this feature have no stored on/off state; every existing entry MUST be treated as enabled (on) so behavior is unchanged after upgrade.
- **Disabled entry with a Timer/relative schedule**: A disabled entry keeps its schedule fields but neither fires nor participates in any schedule accounting for the run.
- **All-disabled or empty template**: Starting the queue is allowed and results in an idle run rather than an error.

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: Each sequence entry in a queue template MUST carry an enabled/disabled state that is independent of the entry's position, schedule type, and schedule timing.
- **FR-002**: The template editor UI MUST present an on/off switch (toggle control) on each sequence entry that reflects and changes that entry's enabled/disabled state.
- **FR-003**: Disabling an entry MUST NOT remove it from the template; the entry MUST remain listed, in the same order, retaining its sequence reference and schedule configuration.
- **FR-004**: The enabled/disabled state MUST be persisted to the template so that it survives page reload and service restart.
- **FR-005**: When a queue run loads its entries from the linked template, entries whose state is "off" MUST be excluded from execution for that run; entries whose state is "on" MUST be executed as they are today.
- **FR-006**: A disabled entry MUST NOT fire, MUST NOT be scheduled, and MUST NOT affect the scheduling or execution of any other (enabled) entry.
- **FR-007**: The system MUST default any entry with no stored enabled/disabled state to "on" (enabled), preserving the behavior of templates created before this feature.
- **FR-008**: Toggling an entry's state MUST NOT alter its schedule type or timer values, and toggling it back on MUST restore its participation in runs exactly as before it was disabled.
- **FR-009**: A queue whose template contains only disabled entries (or no entries) MUST start successfully and simply run no sequences, without raising an error.
- **FR-010**: The on/off state MUST be visible at a glance in the template editor so the operator can tell which entries are active without opening each one.
- **FR-011**: The on/off state MUST be carried in the template save request alongside each entry's order and schedule, so it is persisted by the same save action that persists other entry edits (no separate save step).
- **FR-012**: The on/off state MUST be returned when a template is read back, so the editor renders each entry in its last-saved on/off state after reload.

### Key Entities *(include if feature involves data)*

- **Queue Template Entry**: A positional reference to a sequence within a template, already carrying a sequence reference and schedule configuration. This feature adds an **enabled** state (on/off) to it. The state is persisted with the template, defaults to on when absent, and is independent per entry (including duplicate references to the same sequence).
- **Queue Run**: The execution of a queue that loads its runtime entries from the linked template at start. This feature makes the run consider only the enabled entries of the template.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: An operator can disable a sequence in a template and confirm — via reload — that it stays in the template but no longer runs, in under 30 seconds and without editing or deleting the entry.
- **SC-002**: 100% of entries disabled in a template are skipped during the subsequent queue run, and 100% of enabled entries continue to run as before.
- **SC-003**: After a service restart, the enabled/disabled state of every template entry matches what the operator last saved (0% state loss).
- **SC-004**: Existing templates created before the feature run identically after the upgrade — every previously-existing entry is treated as enabled with no operator action required.
- **SC-005**: Re-enabling a previously disabled entry restores its execution on the next run with its original schedule and position intact (0 schedule/position drift).

## Assumptions

- The on/off state applies at the **template-entry** level (not globally per sequence), so the same sequence can be enabled in one template and disabled in another, and enabled/disabled independently across duplicate entries in the same template.
- Because a queue loads its runtime from the template at start (established existing behavior), "ignored during the queue run" is realized by excluding disabled entries when the run is built at start; changes mid-run apply to the next run.
- The switch lives in the existing template editing surface in the web UI (the same place entries are ordered and scheduled); no separate screen is introduced.
- No bulk enable/disable-all control is required for the MVP; per-entry toggling satisfies the request.
- Persistence uses the existing template storage mechanism; no new storage system is introduced.
