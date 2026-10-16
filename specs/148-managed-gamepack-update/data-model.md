# Data Model: Managed gamepack update

The platform adds no new persisted object except the backup file. All other types live in memory for one call.

## ManagedUpdateRequest (in memory)

| Field | Type | Rule |
|-------|------|------|
| Mode | `Update` or `Rollback` | `Rollback` comes from `managed=true` on the rollback route. |
| FileText | string or null | The gamepack file text. Set for a file source. Null for a rollback. |
| SignatureText | string or null | The detached signature text. Used when the signature policy is not off. |
| CatalogName, CatalogVersion | string or null | Set for a catalog source. The route turns them into `FileText` through `FetchAsync`. |
| GamepackName | string | From the file manifest, or from the route for a rollback. Must pass `GamepackNameRules.IsValid`. |
| DryRun | bool | True: no stop, no backup, no install, no template change. |
| Templates | list of string or null | Template names. Null or empty means all templates. Names match without case. |

## ManagedUpdateReport (response)

| Field | Type | Meaning |
|-------|------|---------|
| result | `done`, `planned`, `failed`, `rejected` | `planned` is a dry run with no problem. `rejected` is a check that ended before any change. |
| reason | string or null | One name from the reason list below. Null for `done` and `planned`. |
| dryRun | bool | Echo of the request. |
| gamepack | `{ name, version }` or null | The target version. |
| previousVersion | string or null | The version that was installed before the call. |
| stoppedQueues | list of `QueueRef` | Queues that the platform stopped. For a dry run: the queues that it would stop. |
| restartedQueues | list of `QueueRef` | Queues that run again. |
| notRestartedQueues | list of `QueueRef` with `error` | Queues where the start failed. |
| backupId | string or null | Id of the stored backup. Null for a dry run, for the same version, and when the update affects no queue template. |
| notRestartedQueues note | - | A queue in this list does not change `result`. The result stays `done`. |
| created, updated, removed | int | Object counts of the install (planned for a dry run). |
| newEntries | list of `EntryRef` | New disabled entries (planned for a dry run). |
| removedSequenceEntries | list of `EntryRef` | Entries whose sequence the new version removed. The entries stay. |
| notAffectedTemplates | list of string | Templates in the request list that the gamepack does not affect. |
| problems | list of `GamepackProblemDto` | All check problems (same type as install). |
| signature | `GamepackSignatureStatusDto` or null | Same block as install. |
| restore | `RestoreReport` | See below. |

`QueueRef`: `{ id, name, templateName }`. `EntryRef`: `{ templateName, sequenceName, position }`.

`RestoreReport`: `{ happened: bool, restored: bool, steps: [ { step, outcome, error? } ], failedObjects: [ string ] }`.
`happened` is true when the flow ran at least one restore step. `restored` is true when all restore steps worked. With `happened` false, `restored` is false and `steps` is empty.

## Reason names

`invalid_request`, `invalid_file`, `signature_rejected`, `validation_failed`, `template_not_found`, `gamepack_not_found`, `no_previous_version`, `entry_not_listed`, `not_configured`, `catalog_error`, `too_large`, `update_in_progress`, `stop_timeout`, `backup_failed`, `install_failed`, `booking_failed`, `restart_failed`, `rollback_refused`, `unexpected`.

An error body has one field `reason` with one name from this list. The report field `result` has only these values: `done`, `planned`, `failed`, `rejected`.

`restart_failed` does not fail the call. The report lists the queue in `notRestartedQueues`, and `result` stays `done`. The platform uses this reason only in `restore.steps`, for the error text of a restore start.

## ManagedBackup (file)

- Path: `<dataRoot>\gamepacks\backups\<backupId>.json`.
- Content: the `SettingsDocument` JSON of feature 145 (`SettingsDocumentJson.Serialize`), reduced to the queue templates that the update affects (and the queues that link to them).
- Id: `<yyyyMMddTHHmmssfff>-<gamepack name>`. The gamepack name is validated. The id has no path character. The platform checks the id format before any read.
- No file exists when the update affects no queue template.
- Retention: the newest 20 files. The store deletes older files after a successful write.
- Write: write-to-temp, atomic replace.

## State of one call

```text
Received -> Checked (dry run of install; scope; lock) -> [dryRun: Planned, end]
         -> Stopped (queues recorded) -> BackedUp -> Installed -> Booked -> Restarted -> Done
Any failure after Stopped -> Restoring -> Failed (restore.happened = true)
Any failure before Stopped -> Rejected (nothing changed)
```

Rules:

- The flow records a queue as stopped only when it was running when the flow started.
- The flow restarts only the recorded queues (FR-009).
- A failure in Checked gives `rejected` and no change.
- A failure in Stopped (timeout) restarts the queues that were stopped and ends as `failed` with `restore.happened` true and no gamepack or template step.
- The instance-wide lock (one managed call for all gamepacks) is held from Checked to the end. It is always released (finally). A dry run does not take it.
- Same version as installed (exact match of the version text): the call ends in Checked with `done`, no stop, no backup, and no change.
- A failed managed rollback: the restore puts back the recorded installed state and imports the backup. It does not call the rollback a second time.
- Failed first install: the restore returns to "no gamepack installed" and removes the files that the install step created.

## Existing types that the feature reads

- `InstalledGamepack.Objects` (sequence ids of the installed version) and `InstalledGamepack.Previous`.
- `QueueTemplate.Entries` and `QueueTemplateEntry` (`SequenceId`, `Enabled`, `ScheduleType`, timer fields, `ParameterValues`).
- `ExecutionQueue.LinkedTemplateId`.
