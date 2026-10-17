# Research: Accept any member order in gamepack files

## Finding 1: Root cause

- `GamepackFileSerializer.Parse` reads each sequence and command with `System.Text.Json` (net9.0).
- `SequenceStepCondition` and `LoopConfig` use `[JsonPolymorphic]` with the discriminator members `type` and `loopType`. `Trigger` uses `type` too, but the gamepack parse path does not read it.
- By default, `System.Text.Json` needs the discriminator as the first member of the object. When it is not first, the reader throws `NotSupportedException`. When it is missing on an abstract base type, the reader also throws `NotSupportedException`.
- `ReadList` catches only `JsonException`, `InvalidOperationException`, and `ArgumentException`. A `NotSupportedException` goes up to the endpoint and gives HTTP 500.
- The ordinal (canonical) member order puts `type` after `imageId`, `minSimilarity`, `negate`. So a file in canonical order fails.

## Finding 2: Hash and signature

- `CanonicalJson` and `GamepackContentHasher` read the raw JSON and sort the keys. They do not use the typed model. The hash and the signature are already independent of the member order. No change is needed (FR-003). A test proves it.

## Finding 3: Status codes

- A structure problem found by `ReadList` today gives HTTP 422 for a real install and HTTP 200 with `valid:false` for a dry run.
- `GamepackInstallStatus.InvalidJson` gives HTTP 400 for both modes, with the existing `InvalidJson` error code.
- The spec (FR-004, FR-005) needs 400 for a missing or bad `type`.

## Decision 1: Option for out-of-order members

- Decision: set `AllowOutOfOrderMetadataProperties = true` on every `JsonSerializerOptions` that the gamepack parse path uses for polymorphic types: `SequenceOptions`, `CommandOptions`, and `SettingsDocumentJson.Options`. One shared place per options object, so FR-006 holds for `type` and `loopType`.
- Rationale: it is a supported .NET 9 option. It fixes the cause and changes no file format. Cost: the reader buffers the object when the discriminator is late. This is small for pack files and adds no second parse pass.
- Alternatives: (a) re-order members before the read: works only for known paths and misses nested objects. (b) A custom converter: more code and more risk. (c) Only catch the exception and return 400: it would reject valid files.
- Side effect: the options are also used to read stored sequences and backups. They only gain tolerance. The write output does not change. FR-008 holds because no API endpoint options change.
- Path: `SettingsDocumentJson` is in `src/GameBot.Domain/QueueSettings/SettingsDocument.cs` (no own file). Its `Options` is also read by the non-install readers `SettingsDocumentValidator` (queue settings import) and used for write by `SettingsNormalizer` and `QueueSettingsEndpoints`. The change is accepted because it has no visible effect there: `SettingsDocument` has no polymorphic type, more files are accepted, none fewer, and no write output changes. Fallback if a review finds an effect: a separate read-options object for install only. See plan.md.
- Polymorphic types in the parse path: `SequenceStepCondition` (`type`) and `LoopConfig` (`loopType`). `Trigger` is not reachable (`Command` holds only `TriggerId`). The API contract types in `SequenceStepContracts.cs` are not in the parse path. A reflection test guards the list.

## Decision 2: Structural classification of type errors (FR-009)

- Decision: do not sort errors by exception message text. Add `GamepackTypeDiscriminatorCheck`, which checks the entry `JsonElement` before the typed read. It uses `JsonTypeInfo.PolymorphismOptions` from the options: the discriminator name and the list of derived type discriminators. It walks the entry along the declared property and element types. At each polymorphic node it checks:
  - discriminator member count: 0 = missing, more than 1 = duplicated;
  - value kind: not a string = bad value type;
  - value: not in the derived list = unknown.
  It returns the JSON path and a reason code.
- A found problem sets `InvalidType` on `GamepackParseResult`. `GamepackService.InstallAsync` maps it to `GamepackInstallStatus.InvalidJson`. The endpoint answers 400 with the existing `InvalidJson` error code for dry run and real install, with the existing body and problem list. The message holds the JSON path and a short reason. No new status or error code.
- Safety net: `ReadList` also catches `NotSupportedException` and reports the entry path with `InvalidType`, so 500 cannot occur. This path uses the exception type only.
- Rationale: it uses the existing 400 route and the existing error format. It reads data, not text, so a .NET message change cannot break it. The path comes from the walk, so it is exact for nested objects.
- Alternatives: (a) match `JsonException.Message` text: fragile; rejected by FR-009. (b) Pre-scan for hard-coded discriminator names: duplicates type knowledge; breaks when a new polymorphic type is added. (c) Use `JsonException.Path`: it is not set for `NotSupportedException`, so it does not cover missing type.
- Risk: the metadata walk must follow collections, dictionaries, and nullable types. Unit tests cover nested conditions and loop configs. If a case needs text matching, one unit test covers each message form.

## Decision 3: Other callers of `Parse`

- `GamepackService.Snapshot`, the managed update orchestrator, and the catalog route call `Parse`. They use `InvalidJson` already, so they get the new behavior with no change.

## Decision 4: Test locations

- Unit tests in `tests/unit`, contract tests in `tests/contract`, integration tests in `tests/integration`. Each of the cases type first, last, middle, missing runs for dry run and real install (FR-007). Endpoint-level tests check status 400, the `InvalidJson` code, and the JSON path.
