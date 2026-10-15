# Data Model: Export and import queue settings by name

## SettingsDocument (export body and import body)

| Field | Type | Rule |
|-------|------|------|
| formatVersion | integer | Required. The value is `1`. Other values are refused (FR-009, FR-014). |
| exportedAtUtc | datetime | Export only. Ignored on import. |
| queues | QueueSettings[] | Optional on import, default empty. Names unique (no case). |
| queueTemplates | QueueTemplateDoc[] | Optional, default empty. Names unique (no case). |
| sequences | CommandSequence (no ids)[] | Optional, default empty. Names unique (no case). |
| commands | Command (no ids)[] | Optional, default empty. Names unique (no case). |
| references | NameReference[] | Export fills it. Import ignores it for decisions; it uses it only for the text of the report. |

The document has no `id` field on any object and no `createdAt` or `updatedAt` (FR-004).

## QueueSettings

| Field | Type | Note |
|-------|------|------|
| name | string | Key. Required. |
| emulatorSerial, emulatorInstanceName, emulatorInstanceIndex | string, string, int | Machine-specific. Exported as stored, imported as given, not checked. |
| cycleExecution, pauseWhenIdle, idleThresholdSeconds, resumeOnServiceStart, notificationLevel | | As stored. |
| failurePolicy, deviceRecovery | objects | As stored. |
| linkedTemplate | string or null | Template name. |
| linkedGame | string or null | Game name. Games are not exported. A missing game is an unresolved reference. |
| entries | string[] | Queue membership: sequence names in order (the runtime entries). Entry ids are not exported. |

## QueueTemplateDoc

| Field | Type | Note |
|-------|------|------|
| name | string | Key. |
| entries | QueueTemplateEntryDoc[] | In order. |

### QueueTemplateEntryDoc

| Field | Type | Note |
|-------|------|------|
| sequence | string | Sequence name. May name a gamepack-owned sequence. |
| enabled | bool | The disabled switch. |
| scheduleType, timerTimeOfDay, timerRelativeOffset | | As stored. |
| parameterValues | ParameterBinding[] | As stored. |

## NameReference

| Field | Type | Note |
|-------|------|------|
| type | `sequence`, `command`, `image`, `queueTemplate`, `game` | |
| name | string | |
| gamepack | string or null | Owner name, when the object is gamepack-owned. |
| version | string or null | Owner version. |
| usedBy | string | Text form `<type>:<name>` of the user, for example `queueTemplate:Daily`. |

## Normalized form

The one projection used by export and by the "unchanged" test of the import: the document form of an object with names in place of ids, sorted in a fixed key order, and with no time stamps. The import compares the normalized target with the normalized document object. Same input gives same output (FR-011).

## ImportReport (response)

| Field | Type | Note |
|-------|------|------|
| items | ImportItem[] | One per document object. |
| unresolvedReferences | UnresolvedReference[] | Empty when all targets exist (US3). |
| summary | counts | created, updated, unchanged, skipped. |
| failed | ImportItem or null | Set only when a repository fails (HTTP 500, FR-018). The item that threw, with a short STE reason. |
| notApplied | ImportItem[] | Set only on HTTP 500. The items that the import did not try. Empty on 200. |

On HTTP 500, `items` holds the items that were applied before the failure (the `applied` list).

### ImportItem

`{ type, name, outcome: created|updated|unchanged|skipped, reason: string|null, restartRequired: bool }`. `reason` is set for `skipped` (`owned_by_gamepack`) and may hold a short STE text for others. `restartRequired` is true only for an updated queue that is not stopped (FR-016).

### UnresolvedReference

`{ type, name, usedBy, gamepack: string|null, version: string|null }` (FR-008). The gamepack fields come from the document `references` list when it has the name.

## State changes

- Import create: new object with a new id from the target repository. A new id is made by the repository, never taken from the document.
- Import update: the target id stays. The fields are replaced by the document values. Queue runtime status is not changed.
- Import skip: no write at all.
- No delete.
- Queue membership: `SetEntries` replaces the runtime entries with new entry ids for an updated or created queue, only when the resolved list differs from the current list (by sequence id order). This keeps a second import a no-op.

## Validation rules

- A "reference" is an entry that points to an object by type and name, with no content. An "unresolved reference" is a reference, or a link inside an item, whose target is missing on the instance at import.
- `formatVersion` is 1. The body is a JSON object. Each named object has a non-empty name. Names are unique per type (queues, queue templates, sequences, commands) inside the document, and name matching ignores case. A duplicate name is a document error that names the duplicate.
- Sequences and commands use the same shape validation as their create routes. An invalid object is refused as a document error (nothing is written), not skipped.
- A document object with the type and name of a gamepack-owned object on the target is skipped (FR-006).
