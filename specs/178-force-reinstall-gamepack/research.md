# Research: Force Install of the Same Gamepack Version (178)

All unknowns from the Technical Context are resolved here. No "NEEDS CLARIFICATION" item stays open.

## R1. What does an install of the same version do today?

Finding: `GamepackValidator.ValidateAsync` has no version check. `Decide` marks an object of the same gamepack as an update, without a look at the version. So `GamepackService.InstallAsync` with the same version already rewrites the objects from the file. It uses `GamepackPreviousHandling.Keep`. It makes no rollback point. It reports no overwritten objects.

The managed update (`GamepackUpdateOrchestrator.ExecuteAsync`) is different. For the same version, `IsSameVersion` returns a `Done` report and does nothing.

Decision: "Without force, behave as today" (FR-003) means no change to either path. The force option adds three things to the plain install: the rollback point, the overwritten list, and the dry-run report. For the managed update, force skips the `IsSameVersion` no-op.

Alternatives considered: Make the plain install reject the same version without force. Rejected. It would break FR-003 and SC-006.

## R2. How does the install know that a request is a forced install of the same version?

Decision: `GamepackService.InstallAsync` reads the registry record for the manifest name. The request is a forced reinstall when `options.Force` is true and the record exists and `record.Version` equals `manifest.Version` (ordinal compare, the same compare as `IsSameVersion`).

In all other cases `Force` has no effect:
- Pack not installed: a normal first install (spec edge case).
- Different version: the update or downgrade rules stay as they are (FR-010). No rollback point from force, no overwritten list.

Alternatives considered: A separate method `ReinstallAsync`. Rejected. Dry run, signature check, gate, and seeds would be copied.

## R3. Which objects does the response list as overwritten?

Spec rule (clarification 1): objects of the pack whose stored content differs from the file. Equal objects are not listed.

Decision: The validator gets a `reinstall` flag. When it is true, `BuildPlan` sets `LiveSequence` and `LiveCommand` for update writes (today it sets them for take-overs only). A new step, `CompareOverwritesAsync`, compares the live content with the plan content. It reuses `GamepackContentComparer.Hash` for sequences and commands. It reuses `ImageIsIdenticalAsync` for images. It does not set `PostHash` (that field is for the take-over backup). The result is `GamepackPlan.Overwrites`, a list of `OverwriteEntry(Type, Name)` for the objects that differ.

The plan content is already final when the compare runs: references are rewritten and `GamepackWaitForImageMapping.Apply` ran (feature 177). So a stored sequence that lacks the 177 `WaitForImage` config shows as different. This is the main repair case.

A take-over object in a forced install keeps its own list (`TakeOvers`). `Overwrites` covers update writes only. An object deleted by hand is created again. It is not listed, because no data is lost.

Alternatives considered: List every object of the pack. Rejected by clarification 1. Compare by file bytes. Rejected. The stored form can differ in volatile members (`createdAt`, `updatedAt`); the comparer already ignores them.

## R4. How is the rollback point stored? (FR-007)

Constraint from the spec: the rollback of a forced install gives back the objects from before the install. The version stays X. The kept previous version stays as it is (clarification 5).

The current rollback reads only the kept previous version (`record.Previous` and `gamepacks/previous/<name>.json`). If force used `GamepackPreviousHandling.Capture`, it would replace the older previous version W with the live X. That breaks clarification 5.

Decision: Add a second, separate rollback point, the "reinstall point":
- File: `<dataRoot>/gamepacks/reinstall/<name>.json`. The content is the live content of the installed version as a gamepack file. The applier already makes this text with `CaptureLiveAsync` (it includes hand edits).
- Registry: `InstalledGamepack.Reinstall` (type `PreviousVersion`, null when none). Old registry files stay valid, because the member is optional.
- Only one reinstall point exists for a pack. A new forced install replaces it.
- `GamepackService.RollbackAsync` uses the reinstall point first when it exists. Then it uses the previous version. After a rollback from the reinstall point the applier clears the reinstall point and keeps `Previous`. A second rollback then goes to the previous version W.
- A normal install of a different version clears the reinstall point. The point would be stale.
- `RemoveAsync` and `RemoveInstalledAsync` delete the reinstall file.

The write order in `GamepackApplier.ApplyAsync` is: objects, previous file, reinstall file, registry. The failure restore puts back the reinstall file the same way as the previous file (new `reinstallTouched` and `reinstallFileBefore` values). So a failed forced install leaves no object, file, or registry change (FR-006, SC-003).

Alternatives considered:
- Reuse the take-over backup. Rejected. Its restore only gives objects back to the user and removes ownership. It needs the post-install hash to match.
- Capture into `Previous`. Rejected (see above).
- Store the point inside `registry.json`. Rejected. Images can be large; the registry file is read at each request.

## R5. Which routes get the force option, and in which form? (FR-004, FR-005, clarification 4)

The form follows the dry-run option on each route:

| Route | Form |
|---|---|
| `POST /api/gamepacks/install` | query `force=true` |
| `POST /api/gamepacks/update` (also managed mode) | query `force=true` |
| `POST /api/gamepacks/catalog/install` | body field `force` |
| `POST /api/gamepacks/catalog/update` (also managed mode) | body field `force` |

These are exactly four routes. The managed update (`managed=true`) is a mode of the two update routes, not a fifth route. The catalog route prefix is `ApiRoutes.GamepackCatalog`.

Decision for the managed mode: `ManagedUpdateQuery.From` gets a `force` argument. `ManagedUpdateRequest` gets `Force`. In `ExecuteAsync`, `IsSameVersion` returns false when `request.Force` is true, so the flow runs stop, backup, install, book, and restart. The orchestrator passes `Force` in `GamepackInstallOptions` to both the dry-run check and the real install.

Managed failure restore: after a failed call, `RestoreGamepackAsync` calls `RollbackAsync`. With a reinstall point this restores the content from before the call. `RestorePreviousMetadataAsync` must also put back the old `Reinstall` value and file. So `GamepackInstalledSnapshot` gets `ReinstallFileText`, and `WritePreviousMetadata` handles it.

Managed rollback (`managed=true`): `CheckRollbackAsync` must accept a reinstall point as the rollback source. Otherwise the report says `no_previous_version` for a pack that has a point.

## R6. Dry run with force (FR-009, SC-005)

Decision: The dry-run branch in `InstallAsync` returns `Overwritten` from `plan.Overwrites` when the request is a forced reinstall. It does not call the applier, so it makes no rollback point and changes no object. The managed dry run reuses this through `CheckUpdateAsync`.

## R7. Response shape

Decision: Add two optional members to `GamepackInstallResponse` and `ManagedUpdateReportDto`:
- `reinstalled` (bool): true when the call was a forced reinstall of the same version. Not written when the call was not a forced reinstall.
- `overwritten` (list of `{type, name}`): the objects from R3. Written when `reinstalled` is true, also when empty.

Both members are absent in a normal call, so the response of a call without force does not change (SC-006). `GamepackListItemDto` gets `hasReinstallPoint` (bool). The web UI does not change.

## R8. Gate and concurrency

No change. The forced install runs inside the same `_gate` as install and rollback. The capture of live content runs inside the gate, in the applier.

## R9. Performance (Constitution IV)

A forced install reads each live object of the pack one more time. The applier already reads them for the undo snapshot. The install also hashes the plan content and the live content. The cost is linear in the pack size. Install is not a hot path.

SC-007 sets the limit. A forced install of a pack with 200 objects takes 1.5 times the time of a normal install, or less.

Decision: Add a timed integration test, `GamepackForceReinstallPerfTests`. The test installs a pack with 200 objects. It then runs a normal install of the same pack and a forced install of the same pack. Each run is measured more than once. The test compares the medians and fails when the ratio is more than 1.5.

The task list saves both times and the ratio in `quickstart.md`, section "SC-007 result". The same note goes into the PR text.

Alternatives considered: A micro-benchmark. Rejected. The limit is a ratio of two full installs, and the timed test measures it.

## R10. Name of the option

The assumption in the spec: `force`. Kept. Query `force`, body field `force`, C# `GamepackInstallOptions.Force`.
