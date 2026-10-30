# Data Model: Apply template changes to a running queue

## Stored data

### QueueTemplateEntry (changed)

| Field | Type | Rule |
|-------|------|------|
| `entryId` | string | New. A GUID in format `N`. Omitted from JSON when empty. Created once. Never changes. Unique in the template (compared without case). |
| (all other fields) | | No change. |

### QueueTemplate (changed)

| Member | Type | Rule |
|--------|------|------|
| `NeedsUpgrade` | bool | Computed, not stored. True when at least one entry has an empty `entryId`. An empty template does not need an upgrade. |

An old file loads with empty IDs. Its stored text does not change until the operator upgrades it or saves it
after the upgrade.

### Settings document (feature 145)

Each template entry in the export gets `entryId`. A document without it imports as a new template with new IDs.
An import over a stored template that needs an upgrade creates no ID. The importer rejects a duplicate ID inside
one template. The canonical text ignores the IDs.

## Run state (memory only, as all run state today)

### QueueRunPlan (new, immutable)

| Field | Meaning |
|-------|---------|
| `Entries` | Ordered enabled entries of the run, each with its `entryId`. |
| `AtQueueStart`, `OncePerRun`, `EveryStep`, `BeforeEachRun`, `Timers` | The partitions that the loop keeps in locals today. Each item keeps its `entryId`. |
| `Version` | Integer. Increases on each swap. |

### QueueRunSchedule (changed)

| Register | Old key | New key |
|----------|---------|---------|
| time-of-day fired on (date) | list index | `entryId` |
| daily retry (due time, attempt) | list index | `entryId` |
| relative fired | list index | `entryId` |
| once-per-run done this cycle | list index | `entryId` |

New members: `Plan` (current `QueueRunPlan`), `SwapPlan(newPlan, droppedEntryIds, clearedEntryIds, renameMap)`.
The consumed state stays for a disabled entry. It goes away for a removed entry and for an entry whose schedule
changed. `renameMap` (`idx:<n>` to `entryId`) moves the consumed state to the new key when the upgrade of a
running queue gives IDs to its entries. An entry of an old template uses the key `idx:<position>` until then.

### OperatorPause (new, on `QueueRunHandle`)

| Field | Meaning |
|-------|---------|
| `Requested` | A pause was requested. |
| `RequestedAt` | When the request was made. |
| `PausedAt` | When the loop reached the gate. Null while "pausing". |
| gate | A task that completes on resume. The loop waits on it. |
| `GameBackedOut` | True when an idle hold stopped early because of the request. The loop brings the game to the front on resume, then clears it (FR-036). |
| `ApplyLock` | One apply at a time for the run. |

State derived from the fields:

```text
running --pause request--> pausing --gate reached--> paused
pausing --resume/cancel--> running
paused  --resume---------> running
any     --run ends--------> (cleared; run state gone)
```

No transition leaves the `Running` status of the queue. The state is not stored on disk.

### TemplateDifference (new, response only)

| Field | Meaning |
|-------|---------|
| `key` | `<type>:<entryId>`. Stable for one `baseToken`. |
| `type` | `added`, `removed`, `enabled`, `disabled`, `parameters`, `schedule`, `order`. |
| `entryId` | The entry. |
| `sequenceName` | Display name of the sequence. |
| `summary` | One STE sentence for the operator. |
| `selectedByDefault` | False for `schedule`. True for all other types. |
| `affectsSchedule` | True for `schedule`, `added`, and `enabled`. Shown as a note in the dialog. |

## Validation rules

- Apply needs the operator pause in state `paused` (FR-012).
- Apply needs `baseToken` equal to the token of the template now (FR-019).
- Each selected key must exist in the difference list of that token (unknown key: 400).
- The new plan must pass the template rules: schedule fields, parameter names, image references (the same
  checks as the template save). One failure refuses the whole apply (FR-015, FR-016).
- Apply is refused with 409 when the template needs an upgrade (FR-028).
- A running entry without an ID, or a template entry without an ID, never matches. The upgrade comes first.
