# Data Model: Force Install of the Same Gamepack Version (178)

## Changed types (Domain, `src\GameBot.Domain\Gamepacks`)

### GamepackInstallOptions
Record. New member:

| Member | Type | Default | Rule |
|---|---|---|---|
| `Force` | `bool` | `false` | Has an effect only when the pack is installed with the same version as the file. |

### GamepackInstallOutcome
New members:

| Member | Type | Rule |
|---|---|---|
| `Reinstalled` | `bool` | True when the request was a forced reinstall of the same version (also for a dry run). |
| `Overwritten` | `IReadOnlyList<OverwriteEntry>?` | Null when `Reinstalled` is false. Else the objects whose stored content differs from the file. Can be empty. |

### OverwriteEntry (new)
Record `OverwriteEntry(string Type, string Name)`. `Type` is one of `GamepackObjectTypes` (sequence, command, image). `Name` is the object name. For an image it is the id.

### GamepackPlan
New member `Overwrites` (`IReadOnlyList<OverwriteEntry>`, default empty). The validator sets it only when its `reinstall` argument is true. The validator also sets `LiveSequence` and `LiveCommand` on update writes in that case.

### InstalledGamepack (registry record)
New optional member:

| Member | Type | Rule |
|---|---|---|
| `Reinstall` | `PreviousVersion?` | The reinstall point. Null when none. Same shape as `Previous`. Old registry files have no such member and stay valid. |

### GamepackInstalledSnapshot
New member `ReinstallFileText` (`string?`): the text of the reinstall file, or null. Used by the managed restore.

### IGamepackRegistryStore
New members with default implementations (so existing test fakes still compile):

| Member | Meaning |
|---|---|
| `string? ReadReinstall(string name)` | Text of `gamepacks/reinstall/<name>.json`, or null. |
| `void WriteReinstall(string name, string text)` | Atomic write. |
| `void DeleteReinstall(string name)` | Delete. A missing file is not an error. |
| `string? ReinstallFilePath(string name)` | Full path for error text, or null. |

### GamepackApplier.ApplyAsync
New optional argument `ReinstallPointHandling reinstall = Keep`.

| Value | Meaning |
|---|---|
| `Keep` | Do not touch the reinstall point. |
| `Capture` | Write the live content of the installed version as the new reinstall point. Used by a forced reinstall. |
| `Clear` | Delete the reinstall point. Used by an install of a different version, and by a rollback. |

`GamepackApplyOutcome` gets no new member.

## Persistence layout

```text
<dataRoot>/gamepacks/
  registry.json              (record may hold "reinstall")
  previous/<name>.json       (unchanged)
  reinstall/<name>.json      (new: live content before the last forced install)
  takeover-backups/...       (unchanged)
```

## State transitions of a pack

| Event | Previous | Reinstall point | Version |
|---|---|---|---|
| First install | none | none | X |
| Install Y (Y differs from X) | X (captured) | cleared | Y |
| Forced install of Y (same version) | unchanged | captured (live Y) | Y |
| Second forced install of Y | unchanged | replaced (live Y) | Y |
| Rollback with a reinstall point | unchanged | cleared | Y |
| Rollback with no reinstall point | cleared | none | previous version |
| Remove the record | file deleted | file deleted | none |
| Forced install fails | restored | restored | unchanged |
| Forced dry run | unchanged | unchanged | unchanged |

## DTOs (Service, `src\GameBot.Service\Endpoints\Dto`)

- `GamepackInstallResponse`: new `Reinstalled` (`bool?`, not written when null) and `Overwritten` (`IReadOnlyList<GamepackOverwriteEntryDto>?`, not written when null).
- `GamepackOverwriteEntryDto` (new): `Type`, `Name`.
- `ManagedUpdateReportDto` (the report of the managed mode of the two update routes; managed mode is not a fifth route): the same two members.
- `GamepackCatalogInstallRequest` and `GamepackCatalogUpdateRequest`: new `Force` (`bool`, default false).
- `ManagedUpdateQuery`: new `Force`. `From(...)` gets a `force` argument.
- `GamepackListItemDto`: new `HasReinstallPoint` (`bool`).
- `ManagedUpdateRequest` (Domain): new `Force`.

## Validation rules

- The force option is a boolean. A missing value means false.
- Force with a first install, or with a different version: no error. The option has no effect (FR-010).
- The existing checks (structure, references, conflicts, signature, API version) run in full in a forced install.
