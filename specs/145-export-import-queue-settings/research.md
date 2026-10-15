# Research: Export and import queue settings by name

All items from the Technical Context are resolved. No "NEEDS CLARIFICATION" is left.

## R1. Where the data lives today

Decision: Read and write through the existing repositories. Add no new store.

- Queues: `IQueueRepository` (`ExecutionQueue`: name, emulator fields, cycle and idle options, `LinkedTemplateId`, `LinkedGameId`, failure policy, resume flag, notification level, device recovery).
- Queue runtime entries (the queue membership list): `IQueueRuntimeStore` (`GetEntries`, `SetEntries(queueId, sequenceIds)`, `GetStatus`). A queue runs the entries that it loaded from its template when it starts.
- Queue templates: `IQueueTemplateRepository` (`FindByNameAsync`). A `QueueTemplateEntry` holds `SequenceId`, `Enabled` (the disabled switch), `ScheduleType`, `TimerTimeOfDay`, `TimerRelativeOffset`, and `ParameterValues` (the entry options and schedules).
- Sequences: `ISequenceRepository`. Commands: `ICommandRepository`. Images: `IImageRepository`. Games: `IGameRepository`.
- Owner records: `IGamepackRegistryStore.Load()` gives the `GamepackRegistry`. `GamepackOwnerResolver.Resolve(type, id)` returns `GamepackOwner` (name and version) or `GamepackOwner.User`. The owner read in feature 143 already checks that the object exists.
- Reference walkers: `GamepackReferences` (`CommandIds`, `ImageIds`, `RewriteCommandIds`) already find and rewrite the command and image ids in sequences and commands.

Rationale: Feature 143 built all the pieces. The export is a read over them. The import is a write over them with one extra rule (never touch an owned object).

Alternatives: A new store for the settings document. Rejected: it would copy state and could drift.

## R2. Names instead of ids (FR-004, FR-012)

Decision: On export, every instance-assigned id is replaced by the name of the target. Object ids (`Id`) are removed from the document. On import, the service builds a name-to-id map (existing objects plus the objects it creates) and rewrites the references back to ids of the target instance.

- Sequence name, command name, template name, queue name, and game name are the keys. Image keys are the image id, because feature 143 defines the image name as its id (FR-024 of 143). An image id is chosen by the user, not assigned by the instance, so it is allowed in the document.
- Matching ignores case, as in feature 143 (`GamepackNameRules` case-insensitive match). A name is unique per object type inside the document (queues, queue templates, sequences, commands).
- A queue links to a game by game name. Games are not exported. A missing game is an unresolved reference.
- Timestamps (`CreatedAt`, `UpdatedAt`) are not exported.
- A name that is not unique in the document is a document error (spec edge case).

Alternatives: Keep ids and map them. Rejected by FR-004.

## R3. Which objects are "gamepack-owned" (FR-003, FR-006)

Decision: An object is gamepack-owned when `GamepackOwnerResolver.Resolve` returns a non-user owner for its type and id on the instance that reads (export) or writes (import). The import decides by the target instance state, not by the document. The document only carries the gamepack name and version as hint text for the report.

Rationale: The target registry is the only trusted source. A document that claims full content for an owned name is skipped (US2 scenario 4).

## R4. Export scope (clarifications 1, 2, 5)

Decision: The export holds:

1. All queues (settings and membership by sequence name).
2. All queue templates with no owner. Queue templates cannot be gamepack-owned today (the owner types are sequence, command, and image), so all templates are user-owned.
3. All sequences and commands with no owner, in full, with ids removed and references turned into names.
4. A `references` list: each use of a gamepack-owned object by a queue, a template entry, a sequence, or a command, as `{type, name, gamepack, version, usedBy}`.

Images are not carried (spec assumption). A user-owned image that a document object uses is listed as an unresolved-capable `image` reference without a gamepack field. A gamepack-owned image is listed in the same `references` list as `{type: image, name: <image id>, gamepack, version, usedBy}`, with no content (clarification C2). A recovery after HTTP 500 is a second send of the same document (clarification D1).

Alternatives: Export only objects that queues use. Rejected by clarification 1.

## R5. Import algorithm and order (FR-005 to FR-011, FR-016)

Decision: A single pass service, `QueueSettingsImportService`, in this order:

1. Parse and validate the document: JSON shape, `formatVersion` equals 1, no duplicate name per type (case-insensitive). On failure return 400 and change nothing (FR-009).
2. Load the target registry once. Build the owner resolver.
3. Plan each item: for each sequence, command, and template, find the target by type and name. If the target is gamepack-owned: `skipped` with reason `owned_by_gamepack`. If the target is absent: `created`. If present and equal after normalization: `unchanged`. Else `updated`.
4. Apply commands first, then sequences, then templates, then queues. Before each write, resolve its references with the name map. A reference to a missing target is added to `unresolvedReferences` and the field is left empty or the entry is skipped (see R6).
5. For a queue that is running (`GetStatus` is not Stopped) and is updated, add `restartRequired: true` to its item. The service does not stop or start the queue (FR-016).

No delete call is made anywhere (FR-007).

Idempotency (FR-011): "equal" is decided on the normalized form (the same projection that the export uses), so a second import of the same document gives `unchanged` for all items and writes nothing. Stored ids of existing objects stay (the update keeps the target id). Timestamps change only on a real update.

Atomicity: The spec asks for partial apply, not all-or-nothing, so no snapshot or restore is needed. Validation of the document (step 1) runs before any write, so a refused document changes nothing.

Write failure (FR-018): When a repository call throws, the service stops the apply. It returns HTTP 500 with a partial report in three lists: `applied` (items written before the failure), `failed` (the one item that threw, with a short STE reason), and `notApplied` (items not tried). Items already written stay written. The service does not undo and does not delete. A repeat of the import is safe because of idempotency. A test wraps one repository in a decorator that throws on a chosen write.

Concurrency (FR-017): The import takes the same gate lock as gamepack install (`GamepackService` gate) before the plan step, and holds it until the last write. It waits while the lock is held, and makes no write before it has the lock. An install that starts during an import waits for the lock. See R7.

## R6. Unresolved references (FR-008)

Decision: A reference has no target when its name is missing on the target after the apply of the document objects. The service handles each place of use:

| Place of use | Treatment |
|--------------|-----------|
| Template entry to a sequence | The entry is skipped. The template is still written with the other entries. The reference is listed. |
| Queue linked template | The link is left empty. Listed. |
| Queue linked game | The link is left empty. Listed. |
| Queue membership entry to a sequence | The entry is skipped. Listed. |
| Sequence step to a command | The sequence is written with the command id left as the name text. Listed. (A later import repairs it, because the update is by name.) |
| Sequence or command to an image | Not rewritten (image id is already a name). Listed when the image does not exist on the target, also when the image is user-owned. |
| Template entry or queue to a gamepack | The gamepack is not installed: the gamepack-owned target is missing, so the reference is listed with the gamepack name and version from the document. |

A "reference" is an entry in the document that points to an object by type and name. An "unresolved reference" is a reference, or a link inside an item, whose target is missing on the instance at import.

Rationale: The list answers "what is missing" and the rest works. A skipped template entry is the least risky way to avoid a step that would fail at run time.

Open point handled: A dangling command reference in a sequence is a known engine failure (it fails loudly at run time). The list tells the user before that happens.

## R7. Gate and locking

Decision: Reuse the single gate lock of `GamepackService` (a private `SemaphoreSlim` today) through a small new method `RunExclusiveAsync` on that class. This does not change the behavior of install, list, rollback (FR-013); it only exposes the lock. The import runs the plan step and the apply step inside `RunExclusiveAsync`, so the owner records cannot change during the import (FR-017). Export takes no lock.

Test proof (FR-017): the test holds the lock through `RunExclusiveAsync`, starts the import, waits a short time, and checks that the recording repositories saw no write. It then releases the lock and checks that the import finishes. A second test holds the lock inside the import (a repository that blocks) and checks that an install waits.

Alternatives: No lock. Rejected: an install in parallel can add an owner record between the plan step and the write step.

## R8. API shape

Decision: Two routes under a new group, `GET /api/queue-settings/export` and `POST /api/queue-settings/import`, in `QueueSettingsEndpoints.cs`, with named handlers (taint analyzers). Export returns the document as `application/json` with a `Content-Disposition` file name. Import takes the document as the body. A request limit of 100 MB is not needed because no image is carried; the default Kestrel limit applies, with a 20 MB request limit set on the route.

Alternatives: Put the routes under `/api/gamepacks`. Rejected: this is not gamepack content and FR-013 says to leave that group as is. Use a multipart upload. Rejected: one JSON body is simpler (SC-006).

## R9. Web UI

Decision: No web UI in the first release (assumption in the spec). The API and OpenAPI text are the product. This keeps the phone check (Principle III) not applicable.

## R10. Machine-specific queue fields

Decision: Export `emulatorSerial`, `emulatorInstanceName`, `emulatorInstanceIndex` as they are, and import them as given. The import report does not check them, because no check is possible without the device. On a new machine the user edits them after the import.

## R11. Test seams

Decision: Use the existing in-memory or file repositories with a temp data root, as the 143 tests do. Add test-only decorators over the repository interfaces: a recording decorator (counts writes and deletes) and a failing decorator (throws on a chosen write, FR-018). The decorators live in the test project, so no production seam is added. Lock tests (FR-017) use `RunExclusiveAsync` from R7.
