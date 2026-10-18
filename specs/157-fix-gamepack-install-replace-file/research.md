# Research: Fix gamepack install file replace failure

## Method

I searched the source for the message text "Unable to remove the file to be replaced" and for each use of `File.Replace`. I read the install write path, the restore path, and the stores they call. I ran small Windows tests of `File.Replace` with different file states.

## Where the message comes from

The text is not in the GameBot source. It is the .NET `IOException` message for Windows error 1175 (`ERROR_UNABLE_TO_REMOVE_REPLACED`). The Windows `ReplaceFile` function returns it, and `System.IO.File.Replace` calls that function. So the failing call is a `File.Replace`.

`File.Replace` is used in five places in `src\GameBot.Domain`:

| Place | Target file | In the install path |
|-------|-------------|---------------------|
| `Images\ImageRepository.cs` `SaveAsync` (line 130) | image file | Yes: 117 images, with take-over |
| `Images\ImageAlternatesRepository.cs` `SetAlternates` (line 77) | alternates JSON file | Yes: images with alternates |
| `Gamepacks\GamepackRegistryStore.cs` `WriteAtomic` (line 128) | `registry.json`, `previous\<name>.json` | Yes: once per install |
| `Triggers\Evaluators\ReferenceImageStore.cs` (line 45) | reference image | No |
| `Logging\AtomicFileWriter.cs` (line 16) | log files | No (it has a catch fallback) |

Sequences, commands and queue templates do not use `File.Replace`. `FileSequenceRepository`, `FileCommandRepository` and `FileQueueTemplateRepository` open the file with `FileMode.Create` and write it. They cannot give this error.

So the error comes from an image file, an alternates file, or `registry.json`. Images are 117 of the 259 objects, so images are the main suspect.

## Root cause

Three facts together cause the failure.

1. **`File.Replace` is fragile on Windows.** `ReplaceFile` must delete or rename the old file. It fails with error 1175 (or 32 or 5) when anything else holds the old file in a way that blocks the delete. Examples: a handle opened without `FileShare.Delete`, a virus scanner, the search indexer, a thumbnail reader. The GameBot code itself opens image files with `FileShare.Read` only (`ImageRepository.OpenReadAsync`, `ReferenceImageStore`, and the `File.OpenRead` calls in repositories). Those opens have no `FileShare.Delete`. A running queue, a detector or a reader on the same image blocks the replace. My tests on this host confirm this:
   - old file open with `FileShare.Read`: `File.Replace` fails ("being used by another process");
   - old file open with `FileShare.Read | Delete`: `File.Replace` works;
   - old file with the read-only attribute: `File.Replace` fails ("Access denied").
   I could not force the exact text of error 1175 with a plain handle in a short test. Windows picks 1175 when the delete step fails after the file was opened. The mechanism is the same: the old file cannot be removed.
2. **The write path has no retry and no safe fallback.** `ImageRepository.SaveAsync` and `SetAlternates` call `File.Replace` once. A short lock (a scanner or a reader) makes the whole install fail. `AtomicFileWriter` has a fallback (`File.Delete` then `File.Move`), but it hides the real error and is no better. The new-file case already uses `File.Move`, which works. That is why a first install (no take-over) works and the take-over install fails.
3. **The error hides the file, and the restore uses the same path.** The applier catches the exception and only returns `ex.Message`. The message has no path and no object. The restore step (`RestoreObjectAsync` for an image calls `SaveAsync` again) uses the same `File.Replace`. If the lock is still there, the restore fails too. This explains the leftover state in issue #333 (13 sequences, 130 commands, 121 images, 7 templates after the failed first attempt). Also, `GamepackApplyOutcome.FailedRestoreKeys` is filled but the endpoint never sends it. `WriteFailed` only says "See the service log".

The dry run never writes. It reads image files and closes the streams, so it cannot show this failure. This is why the dry run agrees and the real install fails (FR-002).

### Open point

The exact blocker on the reporter host is not known from the code alone. The plan does not depend on it. The fix removes the dependency on `ReplaceFile` and adds the file name to the error, so the next failure names the blocker.

## Decisions

### D-001: Replace `File.Replace` with a shared helper that uses `File.Move(overwrite: true)` and a short retry

- Decision: add `AtomicFile.ReplaceFile(tempPath, targetPath, kind, id)` in `GameBot.Domain`. It calls `File.Move(temp, target, overwrite: true)` (`MoveFileEx` with `MOVEFILE_REPLACE_EXISTING`). It retries only for sharing and lock violations (FR-010): an `IOException` whose Win32 code (`HResult & 0xFFFF`) is 32 (`ERROR_SHARING_VIOLATION`) or 33 (`ERROR_LOCK_VIOLATION`). The retry makes up to 5 tries with a delay of 50 ms, 100 ms, 200 ms, 400 ms, 800 ms between them. Any other error (access denied, read-only, path not found) does not retry. It fails at once. On the final failure the helper deletes the temp file and throws an `IOException` with the fixed text of FR-008: `Cannot replace file '<full path>' for <kind> '<id>': <reason>`. It keeps the inner exception. The three install-path callers use it: `ImageRepository`, `ImageAlternatesRepository`, `GamepackRegistryStore`.
- Rationale: `MoveFileEx` with replace is atomic on the same NTFS volume, needs no backup file, and is more tolerant than `ReplaceFile`. The bounded retry covers short locks (scanner, reader) and adds no wait for errors that a retry cannot fix. The fixed text satisfies FR-003 and FR-008.
- Why not `AtomicFileWriter` (FR-013): it calls `File.Replace` first (the failing call), then falls back to non-atomic delete-then-move with an unfiltered catch that hides the real error. It has no retry. It takes a byte array and makes its own temp file, but the install callers already hold a temp file. To make it fit, we must change its behavior for the log writer, which the spec forbids. A new helper is the smaller and safer choice.
- Alternatives considered:
  - Keep `File.Replace` and only add a retry: still fragile. Rejected.
  - `File.Delete` then `File.Move` (the `AtomicFileWriter` pattern): not atomic; a crash between the two steps loses the file. Rejected.
  - Open all image reads with `FileShare.Delete`: helps but does not cover other processes. May be done as a small extra; not required for the fix. Kept out of scope to limit the change.
  - Write images in place with `FileMode.Create`, like sequences: not atomic for a 10 MB file. Rejected.
- Out of scope (note): `ReferenceImageStore` (line 45) and `AtomicFileWriter` (line 16) also use `File.Replace`. They are not in the install path. They stay as they are, to keep the change small. A follow-up may move them to the helper.

### D-002: Name the file in the install error (FR-003, FR-008)

- Decision: the helper throws the fixed text `Cannot replace file '<full path>' for <kind> '<id>': <reason>`. `<kind>` is one of sequence, command, image, template, registry, previous (the last two are for the pack record files; FR-008, FR-008a). `GamepackRegistryStore` has three public writes that call `WriteAtomic`: `Save`, `Update` (kind `registry`) and `WritePrevious` (kind `previous`). The task list re-checks this list with a search. An error has one prefix only; an image error is not wrapped again. The applier passes the kind and id to the repository call. For sequences, commands and templates (`FileMode.Create` writes), the applier catches `IOException` and `UnauthorizedAccessException` and throws an `IOException` with the same template, so all four kinds give one text. The contract and the tests use this exact template.
- Rationale: matches the clarification: full path, object kind and id.
- Alternative: log only. Rejected: the operator reads the response, not the log.

### D-003: List the files in an unknown state (FR-005, FR-009)

- Decision: send `GamepackApplyOutcome.FailedRestoreKeys` to the client. `WriteFailed` for `partial_restore` gains a `failedRestoreKeys` list in the problem details (an extension member). The result name `partial_restore` and the HTTP status 500 stay. The take-over restore route and the rollback route use the same list. Each entry uses one format for all kinds: `<kind>:<id> (<full file path>)`, for example `image:abc (C:\data\images\abc.png)`. The applier builds the entry when the restore of that object fails. It gets the full path from the store that wrote the file, through a small read-only path method on each store (FR-009). The registry store already holds the registry and previous paths. For the registry and the previous-file the kind is `registry` and `previous`, and the id is the pack name.
- Rationale: no API break (clarification 2). The data exists already.
- Alternative: new status name. Rejected by clarification 2.

### D-004: Keep the backup-and-restore model (FR-004)

- Decision: no change of the model. One small fix: the restore of an image uses the same helper, so it gets the same retry. A write that fails before the object changes does not need a restore, but the code restores it too; this stays (it is safe: it writes the old content).
- Rationale: clarification 3.

### D-005: Tests (FR-007, SC-004)

- Root cause test (unit, Windows only, skipped on other systems): hold an existing image file open with `FileShare.Read` on a worker thread, release it after 300 ms, then call `ImageRepository.SaveAsync(overwrite: true)`. Before the fix: `File.Replace` throws at once, the test fails. After the fix: the retry succeeds and the file holds the new content (FR-007, SC-004).
- Failure test: hold the file for the whole test. `SaveAsync` must throw an `IOException` whose message equals the FR-008 template with the full file path. The temp file must be gone.
- No-retry test (FR-010): a read-only target fails at once (time below the first retry delay) and the message names the file (FR-012).
- Alternates and registry store: the same tests for `SetAlternates` and `WritePrevious`.
- Applier test: a repository stub that throws for one object of each kind. The install result is `failed` with the FR-008 text, and all other objects hold their old content (FR-012, restore works).
- Restore failure test: stubs that fail on the write and on the restore for more than one key. The result is `partial_restore` and the response lists every key in the FR-009 format (the gate in `IGamepackWriteGate` can force the failure).
- Contract test: the install route with a `partial_restore` outcome returns 500 with `result` `partial_restore` and a `failedRestoreKeys` list. A second contract test covers `failed` with a restore that works.
- Full-scale integration test (FR-011, SC-001): install a pack of 259 objects (117 images, 130 commands, 12 sequences, templates) on top of the same 259 existing objects with take-over. Assert success and the pack content for all four kinds. Assert that the counts match and no stale or duplicate object remains.
- Mixed test (FR-012): one install with new and existing objects.
- Image prefix test (FR-008): an image failure through the applier gives one prefix only.
- Registry tests (FR-008a): `Save`, `Update` and `WritePrevious` give the FR-008 text with kind `registry` or `previous` when the target is blocked.
- Platform (FR-012): tests that need Windows lock behavior skip on other systems. CI runs on Windows (`windows-latest`), so they run there. The 259-object test uses a synthetic pack and does not need the signed PNS 1.0.0 file.

### D-006: Docs

- Update `docs/architecture.md` (persistence note: image writes use move-with-replace and a retry) and refresh "Last reviewed" (Principle V). Update the install API description in the OpenAPI schema filter for the new `failedRestoreKeys` member. Update the Status line in spec.md and the feature entry in `specs/STATUS.md` (FR-014).
