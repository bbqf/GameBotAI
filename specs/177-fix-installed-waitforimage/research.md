# Research: Fix inline WaitForImage in installed gamepack sequences

No item in the Technical Context is marked NEEDS CLARIFICATION. This file records the design decisions.

## Root cause

`GamepackFileSerializer` reads pack sequence steps into `SequenceStep` objects. The step `Action` holds the payload in `Action.Parameters`. The member `SequenceStep.WaitForImage` stays null, because only `SequencesEndpoints` (the save path) builds it. `SequenceRunner` and `CommandExecutor` read `step.WaitForImage` only. With a null value the step runs as a 1 s delay.

## Decision 1: One shared mapper in the Domain layer

- Decision: Add `GameBot.Domain.Commands.WaitForImageConfigMapper.Map(string? actionType, IReadOnlyDictionary<string, object?> payload)`. Move the logic of `MapWaitForImageConfig` and `MapWaitForImageDetectionTarget` into it. `SequencesEndpoints` calls it and keeps no copy.
- Rationale: Service depends on Domain. The gamepack code is in Domain and cannot call Service code. FR-005 and FR-005d allow no second copy.
- Alternatives: (a) Copy the logic into Domain. Rejected: two copies drift apart (the cause of this bug class). (b) Make the Domain call a Service delegate. Rejected: wrong dependency direction. (c) Repair at run time in `SequenceRunner` when `WaitForImage` is null. Rejected: the runner stays unchanged and stored data would stay wrong.

## Decision 2: Fields that the mapper reads

- Decision: Read `timeoutMs` (default 1000), `until`, `absentCaptures`, and `detectionTarget` (`referenceImageId`, `confidence` default 0.8, `offsetX` and `offsetY` default 0, `selectionStrategy`, `region`). Add no field and drop none.
- Rationale: Equal behavior with the save path (spec clarification 1).
- Alternatives: Read more fields. Rejected: out of scope.

## Decision 3: Value types, and the runtime type check

- Decision: The mapper accepts the same value types as the save path: `int`, `long`, `JsonElement` number, and numeric `string` for whole numbers; `string` and `JsonElement` string for `until`; `JsonElement` object for `detectionTarget`.
- Required check (first step of the work, before the mapper is final): read a real pack JSON file with `GamepackFileSerializer` (both the stored form `action.parameters` and the API form `primitiveAction.payload`, which `GamepackStepReader` converts) and write the runtime type of `Parameters["detectionTarget"]`, `["timeoutMs"]`, `["until"]`, and `["absentCaptures"]` in a unit test. Expected result: `JsonElement` for all, because `Parameters` is `Dictionary<string, object?>` and System.Text.Json gives `JsonElement` for `object`. The mapper MUST accept the type that the test shows. If the test shows another type (for example `JsonNode` or a dictionary), the mapper converts it to a `JsonElement` before it reads it, and the test stays as a guard.
- Rationale: Spec clarification I2. A mapper test that uses hand-made `JsonElement` values can pass while the real pack read gives another type. The install test MUST start from a real pack file (spec clarification 5).
- Alternatives: Accept a dictionary for `detectionTarget` with no check. Rejected: this guesses and does not prove.

## Decision 4: The mapper is the only reader, including region and numbers

- Decision: `WaitForImageConfigMapper` has three public members: `Map`, `TryReadInt32(object?, out int)`, and `TryReadRegion(JsonElement detectionTarget, out RegionFields? region, out IReadOnlyList<string> errors, string prefix)`. `RegionFields` is a small public record in the same file with four `int?` members (`X`, `Y`, `Width`, `Height`). The code of `TryReadInt32` and `TryReadRegion` moves from `SequencesEndpoints` with no change in behavior, including the error texts.
  - `Map` calls `TryReadRegion`. It keeps the region only when the read succeeds and `PixelRegion.Validate` gives no error. Otherwise the region is null. `Map` does not throw.
  - The save-time region check (`CollectRegionErrors` in Service) calls `WaitForImageConfigMapper.TryReadRegion`, wraps the numbers in `PixelRegionDto`, and calls `PixelRegionDto.Validate`. Its messages do not change (feature 130).
  - `SequencesEndpoints` keeps no number reader and no region reader for these fields.
- Rationale: Spec clarifications D1 and D2. Two readers can drift apart. This is the cause of B-039. For valid payloads the result equals the old save path result. For an invalid region, install gives no region and no failure (install adds no validation).
- Check before the move: `TryReadInt32` in `SequencesEndpoints` is used only by the two WaitForImage fields (`timeoutMs`, `absentCaptures`), so the move leaves no other caller. A search of `src` confirms this. `TryReadRegion` is used by the mapping and by `CollectRegionErrors`.
- Alternatives: (a) Keep the Service region reader for messages, add a Domain reader for values. Rejected: this is the two-reader design that the spec forbids. (b) Make `PixelRegionDto` public in Domain. Rejected: it is a Service model type.

## Decision 5: Where the walk runs in install

- Decision: Call `GamepackWaitForImageMapping.Apply(sequence)` in `GamepackValidator` (the plan builder). Place the call right after `GamepackReferences.RewriteCommandIds(sequence, Map)` and before the `PlannedObject` for the sequence is added (the loop over `file.Sequences`).
- Rationale: The take-over hash (`PostHash = GamepackContentComparer.Hash(write.Sequence)`, set in `CompareTakeOversAsync`) is computed after the plan is built. The content must be final before it. A change in `GamepackApplier` would run after the hash and make the stored content differ from the hash. All three modes (new install, take-over, update) build the plan with this method, so one call covers FR-003.
- Alternatives: (a) Apply in `GamepackApplier`. Rejected: after the hash; the applier stays unchanged. (b) Apply in `GamepackFileSerializer` at read time. Rejected: it would also change dry-run reads, signature checks, and other readers.

## Decision 6: One recursive walk

- Decision: A new Domain file, `GamepackWaitForImageMapping.cs`, has one recursive method. It visits each step, then `Body`, then `ElseBody` (null-safe). `Body` and `ElseBody` are members of `SequenceStep`, not of `IfConfig`. For each step with `Action` not null and type `WaitForImage`, set `step.WaitForImage = WaitForImageConfigMapper.Map(...)`. A step with no action is skipped with no failure.
- Rationale: One code path for all depths (FR-002, FR-005c). `GamepackReferences.Flatten` is private and has the same shape, but it needs a visibility change to reuse. Keep the new helper small and separate.
- Alternatives: Make `Flatten` internal and reuse it. Rejected to keep the change local.

## Decision 7: Existing configuration on a pack step

- Decision: The pack step format allows a `waitForImage` member (`GamepackStepReader.StoredMembers`). When the step has an `Action` of type `WaitForImage`, the walk sets `WaitForImage` from the payload and replaces any value from the file. When the step has no `Action`, the walk skips it and keeps the value.
- Rationale: The payload is the source of truth, as in the save path. Equal behavior for equal payloads.
- Alternatives: Skip steps that already have a value. Rejected: stored and payload data could differ.

## Decision 8: Image-reference checks do not change

- Decision: The checks that collect and validate image references of a pack (`GamepackReferences.ImageIds`, `ReportMissing` in `GamepackValidator`, line about `ImageIds(sequence)`) run BEFORE the plan builder, on the steps as read from the file. The walk runs after them. The checks therefore see the same data as before and give the same result. No change to these checks.
- Test: a contract test installs a pack with a WaitForImage step that names a reference image that is not in the pack and not on the system. The install result (problems list, status) MUST be equal to the result before the fix. The test records the expected result from the current behavior.
- Rationale: Spec clarification C1. The fix must not add or remove an install problem.

## Decision 9: Update writes corrected steps

- Decision: An update of a gamepack-owned pack builds the plan with the same method, so the walk runs. The stored content now holds `waitForImage`, so the compare (hash of final content against the live content) sees a difference for an old installed sequence and writes it. No special code is needed. A contract test installs an old-form sequence (no `waitForImage`, as the bug stored it), then updates the pack, then reads the stored step and expects the configuration. This holds even if the old content looked equal to the new pack payload (spec clarification A1).
- Check: confirm in the test that the update path does not skip a sequence on a compare of the payload only. If the test fails, the plan changes to force the write; the task list adds this branch.
- Alternatives: Add a migration. Rejected: spec assumption says no migration.

## Decision 10: Parity proof

- Decision: A contract test sends one WaitForImage payload (timeout, image id, confidence, offsets, strategy, until, absentCaptures, region) through two routes: a pack install (real pack file) and `POST /api/sequences`. It reads both stored sequences from the repository (or `GET /api/sequences/{id}`) and compares the `waitForImage` member and the `action` member of the step, field by field. A second case covers a nested step.
- Rationale: Spec clarification I1. A test that compares the mapper with itself proves nothing about the two paths.

## Decision 11: Tests and order

- Decision: First write a failing reproduction test (contract test: install a pack from a real JSON file and read the stored sequence; today `waitForImage` is null). Show it fail. Then fix. Test list:
  - Unit, mapper: defaults, until, absentCaptures, region valid and invalid, missing image id, wrong action type, null payload, value types (int, long, `JsonElement`, string).
  - Unit, readers: `TryReadInt32` and `TryReadRegion` keep the old results and error texts (move guard).
  - Unit, runtime type: pack file read gives the expected type (decision 3).
  - Unit, walk: top-level, loop Body, if Body, if ElseBody, three levels, no-action step, other step types unchanged.
  - Contract: install (new), install with takeOver (hash equals the hash of the stored content; a second dry run reports no change), update, parity, missing reference image, nested step.
  - Existing save-path tests for feature 130 messages stay green (SC-005).
- Run-time proof: the runner is unchanged and reads only this configuration, so the stored configuration of the installed step, equal to the saved step, proves SC-002. A manual run is optional.
- Test placement: new unit tests in `tests/unit/Gamepacks`. New contract tests in `tests/contract/Gamepacks` in a new, small class. Check the shard balance rule with `scripts/ci/measure-shards.ps1` if the shard lists need a change.

## Decision 12: Documentation and process

- Decision: Add row 177 to `specs/STATUS.md`. Set the spec `Status` line when the work is done. Review `docs/architecture.md` (the WaitForImage and gamepack install text) and refresh "Last reviewed" if text changes. Bump the patch in `installer/versioning/version.override.json` before the PR push.
- Rationale: Constitution V and the Development Workflow.
