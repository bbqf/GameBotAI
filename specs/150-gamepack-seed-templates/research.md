# Research: Seed queue templates in a gamepack

All items below are decided. No item is open.

## R1. Seed shape and file member

- **Decision**: The file member is `seedTemplates`. The C# member is `GamepackFile.SeedTemplates` of type `IReadOnlyList<QueueTemplateDoc>`. `QueueTemplateDoc` and `QueueTemplateEntryDoc` (namespace `GameBot.Domain.QueueSettings`) are reused as they are.
- **Rationale**: FR-002 says the seed has the settings-document shape and adds no field. The type already holds name, entries, sequence name, enabled flag, schedule type, timer fields, and parameter values. The settings JSON options (`SettingsDocumentJson.Options`: camel case, enum as name) read it.
- **Alternatives**: A new seed type. Rejected: it copies the shape and drifts over time.

## R2. Parse and write

- **Decision**: `GamepackFileSerializer.Parse` reads `seedTemplates` with `SettingsDocumentJson.Options` through the existing `ReadList` helper. A wrong shape gives a `structure` problem. `Serialize` writes the member only when the list is not empty.
- **Rationale**: A file without seeds stays the same byte for byte (SC-004). The previous-version file that the applier captures has no seeds, which is right: a seed never comes back (FR-005, FR-007).
- **Alternatives**: Always write an empty list. Rejected: it changes the output of every export and the kept previous files.

## R3. First install decision

- **Decision**: First install means `registry.Gamepacks` has no key `GamepackNameRules.Key(name)`. The validator already loads the registry, so it makes the decision. The validator gets a new parameter `seeds` (default false). The service passes true for an install and a dry run. The rollback passes false.
- **Rationale**: Clarification Q2 and the assumption in the spec. The registry record stays after the first install and no route removes it. A failed install does not write a record (the applier writes the registry last), so a failed first install is not an install.
- **Alternatives**: Check for existing owner entries. Rejected: a gamepack with no object has no owner entry but is installed.

## R4. Template rules in one place

- **Decision**: Move the entry checks of `QueueTemplatesEndpoints.POST` (name pattern and length, schedule type, exactly one timer field for `Timer`, timer format, parameter value names) into `QueueTemplateRules` in the Domain project, file `src\GameBot.Domain\QueueSettings\QueueTemplateRules.cs`. The class has a neutral name and is not in a Seeds folder, because the queue template route also uses it. The route calls it. The validator calls it for each seed. The rules work on the typed doc (`TimerTimeOfDay`, `TimerRelativeOffset`), so the text parse of the route stays in the route.
- **Rationale**: FR-011 needs the same rules. One class avoids two copies. The route keeps its error texts and status codes (a unit test pins them).
- **Alternatives**: Copy the checks. Rejected: they drift. Call the route code from the validator. Rejected: Domain must not use Service.
- **Note**: The doc types already have a typed `ScheduleType` (an unknown name fails in the JSON read, which gives a `structure` problem). The parameter name check uses `ParameterNameRules.ValidateName`. The image reference check of the route (feature 114) is not part of the seed check. A seed that names an unknown image is a risk only at run time, and the images of the gamepack do not exist yet at check time.

## R5. Reference check

- **Decision**: On a first install, each entry `Sequence` must be (a) a sequence name in the file, or (b) a sequence name on the instance. Names are compared without case. A miss gives a problem with category `reference`, object type `seedTemplate`, and the template name. The message names the entry position and the sequence.
- **Rationale**: FR-009, FR-015, and edge cases 1 and 2. On a later install or update, the platform ignores the seeds, so no reference check runs and no seed plan is built. The structure check (R4, FR-011) runs on every install, because the file must be valid as a whole.
- **Alternatives**: Check references on every install. Rejected: a later version could not install because of a seed that the platform will not use.

## R6. Final ids and the seed plan

- **Decision**: The validator builds `SeedPlan` after `BuildPlan`. For each seed it resolves each entry sequence name to an id: first the final id of a sequence write of the plan, then the id of a stored sequence. It marks the seed `create` when `IQueueTemplateRepository` has no template of that name (compare without case), else `skip_exists`. The plan keeps the resolved entries only for `create` items.
- **Rationale**: The ids exist only after `BuildPlan` (a new sequence can get a new id). The skip decision uses the store state at check time. The gate of the service holds the install, so the state does not change until the apply.
- **Alternatives**: Resolve names in the applier. Rejected: the dry run would not show the same result (SC-003).

## R7. Atomic write and undo (FR-014)

- **Decision**: `GamepackApplier.ApplyAsync` creates the seed templates after the object writes and before `WritePreviousFile`. The gate calls `BeforeWrite(Create, "queueTemplate:<name>")` for each create. The applier keeps the ids of the created templates. In the `catch` block, `RestoreAsync` deletes them first (reverse order, before the object restore). The undo is best effort (FR-014): it deletes by the ids that this install created, logs each delete error, and continues with the next id and with the object restore. A delete failure goes into `failed` and gives `PartialRestore`.
- **Rationale**: The registry write is the last write. A failure at any earlier step, or at the registry write itself, then removes the templates that this install created. This is the whole of FR-014 and needs no second pass.
- **Re-check (FR-016)**: Just before each template write, the applier reads the template list again and compares names without case. If the name is now used, the applier does not write the template. It records the seed as skipped with the reason `skipped_exists` and returns it in `SeedTemplatesSkipped` of `GamepackApplyOutcome`. The service merges this skip into the install result. Result: a name that was free at plan time and is used at write time never gives an overwrite or a duplicate. A test uses a template store that adds the name between plan and write.
- **Alternatives**: Create the seeds after `ApplyAsync`. Rejected: a seed failure then leaves an installed gamepack, and the install result would be wrong. Create the seeds before the objects. Rejected: a seed could point to a sequence that does not exist yet.
- **Note**: The applier needs `IQueueTemplateRepository`. The constructor gets a new optional parameter, as the take-over store did in feature 149. Without the store, an apply with a seed `create` in the plan returns `Failed` with no change. Tests that build the applier by hand keep working.

## R8. Rollback, uninstall, update

- **Decision**: The rollback passes `seeds: false`, so the plan has no seed item and the rollback result has two empty lists (FR-010). A rollback and a managed update are always "not first install" for seed purposes. The managed update always finds a registry record, because it updates an installed gamepack. There is no uninstall route. The managed update and the catalog routes call `GamepackService.InstallAsync`, so they follow the same rule: on an update the registry has a record, so no seed is planned.
- **Rationale**: FR-005, FR-007, and the edge cases. The registry record is the only state that is needed.

## R9. Result lists

- **Decision**: `GamepackInstallOutcome` gets `SeedTemplatesCreated` (list of names) and `SeedTemplatesSkipped` (list of `SeedSkip`: name and reason). The HTTP response uses the names `seedTemplatesCreated` (array of string) and `seedTemplatesSkipped` (array of object with `name` and `reason`). Both are always present, also for a dry run (then they mean "would create" and "would skip") and also when empty.
- **Rationale**: FR-010 and scenario 3 of story 1. An always-present array is easier for a client than a missing member. It follows `takenOver`, which is also always present.
- **Alternatives**: One list with a status per item. Rejected: FR-010 says two lists.
- **Names**: `skipped_exists` is the only reason now. The reason is a string so that a later reason needs no schema change.

## R10. Catalog and managed update

- **Decision**: The catalog install result and the managed update report carry the same two lists, copied from the install step. The managed update report fills them from the `GamepackInstallOutcome` of its install step. A managed update of an installed gamepack gives two empty lists.
- **Rationale**: The spec edge cases say that the catalog route acts in the same way. A user who installs from the catalog needs to see the seed result.

## R11. Signature

- **Decision**: `GamepackContentHasher` adds `AddList(files, root, "seedTemplates", GamepackObjectTypes.SeedTemplate, "name")`. `SignatureFile` accepts the type `seedTemplate`. A signature made by a signer that does not know the list stays valid for a file without seeds. A file with seeds needs a signer that hashes them.
- **Rationale**: The verifier compares the full list of signed files with the computed list. Without this change, a seed list is not signed, and an attacker could add a seed to a signed gamepack with no change of the signature. A seed starts no run, but it adds a visible schedule that the user can start.
- **No-seed rule (FR-017)**: `AddList` adds no file entry when the list is empty or missing. A signed file without seeds then has the same file list and the same hash as before this feature, and its signature check gives the same result. A test pins the hash of a sample file without seeds, made before the change.
- **Risk**: A signer tool that is outside this repository must hash the seeds the same way. The docs say this. A test pins the hash of a sample.
- **Alternatives**: Leave the seeds out of the signature. Rejected: it is a hole in the trust model of feature 146.

## R12. Settings export and the owner view

- **Decision**: No change. A created seed has no owner entry, so the settings export (`QueueSettingsExporter`) lists it as an ordinary template (FR-006). A test checks the export.

## R13. Template name conflict check

- **Decision**: Use `IQueueTemplateRepository.ListAsync()` once per check and compare names with `StringComparison.OrdinalIgnoreCase`. Do not use `FindByNameAsync` in a loop.
- **Rationale**: One read for all seeds. The repository also has this rule for its own uniqueness. The applier uses the same read again for the FR-016 re-check.

## R14. Version bump

- **Decision**: One task, before the push, bumps `installer\versioning\version.override.json` by the rule in CLAUDE.md: compare with `origin/master`; if `minor` or `major` changed, set `patch` to `"0"`; else set `patch` to the `patch` on `origin/master` plus 1; set `updatedBy` and `updatedAtUtc`.
- **Rationale**: The constitution requires the bump for each new PR.
