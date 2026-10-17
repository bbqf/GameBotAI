# Data Model: Seed queue templates in a gamepack

No stored format changes. The registry file and the template files keep their shape.

## GamepackFile (change)

| Member | Type | Rule |
|--------|------|------|
| `SeedTemplates` | `IReadOnlyList<QueueTemplateDoc>` | Optional. Default empty. JSON name `seedTemplates`. A file without it is valid (FR-001). |

`QueueTemplateDoc` (existing, `GameBot.Domain.QueueSettings`): `Name`, `Entries`.
`QueueTemplateEntryDoc` (existing): `Sequence` (name), `Enabled` (default true), `ScheduleType`, `TimerTimeOfDay`, `TimerRelativeOffset`, `ParameterValues`.

## Seed validation rules (QueueTemplateRules)

`QueueTemplateRules` is in `GameBot.Domain.QueueSettings`. The queue template route and the gamepack validator both use it.

Run on every install, dry run, and update (FR-011, FR-015). Not run on a rollback.

1. The template name is not empty, has at most 100 characters, and uses only letters, digits, spaces, hyphen, and underscore.
2. Template names in the file are unique without case.
3. Each entry has a non-empty `Sequence`.
4. `ScheduleType` is a defined value. A `Timer` entry has exactly one of `TimerTimeOfDay` and `TimerRelativeOffset`. A non-timer entry has neither.
5. Each parameter value name passes `ParameterNameRules.ValidateName`.

A fail gives a `structure` problem (object type `seedTemplate`, name = template name).

## Reference rule

Only on a first install (FR-015). On a later install or update the platform does not run it. Each `Sequence` name matches (without case) a sequence in the file, or a sequence on the instance. A miss gives a `reference` problem.

## GamepackObjectTypes (change)

New constant `SeedTemplate = "seedTemplate"`. It is a problem object type and a signature file type. It is not an owner type and is not in the registry.

## SeedPlan (new, in GamepackPlan)

| Member | Type | Meaning |
|--------|------|---------|
| `Items` | `IReadOnlyList<SeedPlanItem>` | One item for each seed, in file order. Empty when the install is not a first install. |

`SeedPlanItem`:

| Member | Type | Meaning |
|--------|------|---------|
| `Name` | string | The template name from the file. |
| `Outcome` | string | `create` or `skip_exists` (`SeedOutcomes`). |
| `Entries` | list of resolved entry | Only for `create`. Each has `SequenceId` and the entry fields. |
| `TemplateId` | string? | Only for `create`. A new id (`Guid` "N" format). |

`GamepackPlan` gets `SeedPlan Seeds` (default empty), `SeedsToCreate` (count), and `SeedsSkipped` (count).

## State transitions

```text
no registry record  --install (apply ok)-->  record exists   [seeds: create or skip_exists; name re-checked before each write]
no registry record  --install (apply fails)--> no record     [seed templates removed again]
record exists       --install / update / rollback--> record exists   [seeds: none]
```

## Result types

`GamepackInstallOutcome` (change):

| Member | Type | Meaning |
|--------|------|---------|
| `SeedTemplatesCreated` | `IReadOnlyList<string>` | Template names created (planned for a dry run). Always set. |
| `SeedTemplatesSkipped` | `IReadOnlyList<SeedSkip>` | Seeds not created (planned for a dry run). Always set. |

`SeedSkip` (new record): `Name`, `Reason`. `Reason` is `skipped_exists` (`SeedSkipReasons.Exists`).

`GamepackApplyOutcome` (change): `SeedTemplatesCreated` (list of names) and `SeedTemplatesSkipped` (list of `SeedSkip`) for the service to copy. If a first install fails, the applier deletes the created templates by their ids, logs each delete error, and continues (FR-014). The applier adds a `skipped_exists` item when the write-time re-check finds the name used (FR-016). The service merges these skips with the plan skips.

The same two lists go to `ManagedUpdateReport` and to the catalog install result.

## Created template

An ordinary `QueueTemplate`: new `Id`, `Name` from the seed, `Entries` from the plan. It has no owner entry and no link to the gamepack.
