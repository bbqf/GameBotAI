# Phase 0 Research: Emulator Execution Queue

All Technical Context items are resolved against the existing GameBot codebase; no open `NEEDS CLARIFICATION` remains (the spec's three high-impact and five low-impact ambiguities were resolved in `/speckit-clarify`).

## Decision: Persist config in a file repository, hold contents + status in an in-memory singleton

- **Decision**: Persist `ExecutionQueue` config (name, emulator serial, cycle flag, timestamps) as JSON files under `{storageRoot}/queues/`, using a `FileQueueRepository` modeled on `FileSequenceRepository`. Hold the ordered sequence entries and the execution status in a separate `QueueRuntimeStore` singleton (`ConcurrentDictionary`), never written to disk.
- **Rationale**: The spec explicitly requires config to survive restarts (FR-020) but contents and status to reset on restart (FR-021, FR-022). A singleton with no persistence path satisfies the reset requirement "for free" — process restart discards it. Splitting the two stores keeps each model honest about its durability and avoids accidentally serializing entries.
- **Alternatives considered**:
  - *Single persisted model with `[JsonIgnore]` on entries/status* (like `CommandSequence` ignores `Steps` in some projections): rejected — easy to accidentally persist, and status/entries don't belong in the same lifecycle as config.
  - *Database / EF*: rejected — project uses simple file-backed repositories everywhere; introducing a DB violates "no unjustified new dependencies."

## Decision: REST surface under `/api/queues` via a `MapQueueEndpoints()` extension

- **Decision**: Add `ApiRoutes.Queues = Base + "/queues"` and a `QueuesEndpoints.MapQueueEndpoints(this IEndpointRouteBuilder)` extension registered in `Program.cs` alongside `MapGameEndpoints()` etc. Use the project's standard error envelope `{ error: { code, message, hint } }`.
- **Rationale**: Mirrors `GamesEndpoints`/`TriggersEndpoints` exactly; keeps Program.cs consistent and handlers thin (≤50 LOC). Sub-resources (`/entries`, `/start`, `/stop`) map cleanly to the queue's runtime store.
- **Alternatives considered**: Inline endpoints in `Program.cs` (as Sequences does) — rejected for readability; a dedicated endpoints file is the more common and cleaner pattern here.

## Decision: Emulator = ADB device serial, selected from `useAdbDevices`

- **Decision**: The bound emulator is identified by its ADB device serial (e.g., `emu-1`). The creation form populates the picker from the existing `useAdbDevices` hook / `GET /api/adb/devices`. The serial string is persisted as `EmulatorSerial`.
- **Rationale**: There is no separate "configured emulator" entity in the app; ADB devices (by serial) are the only emulator concept and are already surfaced via `adbApi`/`useAdbDevices`. Clarify session confirmed binding by connected ADB serial.
- **Alternatives considered**: Free-text serial (rejected — error-prone, clarified against); new configured-emulator entity (rejected — out of scope, would expand the feature substantially).

## Decision: Stale sequence references resolved at read time

- **Decision**: Queue entries store only `sequenceId`. When building `GET /api/queues/{id}`, resolve each `sequenceId` against `ISequenceRepository`; if missing, mark the entry `stale: true` and leave `sequenceName` null. Entries are never auto-removed.
- **Rationale**: Matches the spec decision (keep + flag) and the app's existing pattern of flagging deleted image references rather than cascading deletes. Resolution at read time avoids stale caches since contents are in-memory anyway.
- **Alternatives considered**: Auto-remove on sequence deletion (rejected — clarified against; also would require cross-store delete hooks).

## Decision: Start/stop are status-only placeholders; logged via `ILogger`

- **Decision**: `start` sets status `Running`, `stop` sets `Stopped`, both idempotent. No sequence execution occurs. Each transition emits an `ILogger` information entry (FR-019b). Start is permitted regardless of emulator connectivity (FR-019a).
- **Rationale**: Spec scopes execution as a placeholder; logging start/stop only matches the clarified observability decision and the app's existing structured-logging conventions.
- **Alternatives considered**: Blocking start when emulator offline (rejected — clarified against; deferred to the real engine); logging all CRUD (rejected — clarified to execution-only).

## Decision: Frontend tab wiring follows the existing `Nav`/`App` pattern

- **Decision**: Add `'Queues'` to the `AuthoringTab` union and `tabs[]` array in `Nav.tsx`; render `<QueuesPage/>` for `tab === 'Queues'` in `App.tsx`; reuse `List`, `ConfirmDeleteModal`, `SearchableDropdown`, and `FormField`.
- **Rationale**: Identical to how Commands/Games/Sequences/Images tabs are wired; minimizes new surface and keeps UX consistent (Constitution III).
- **Alternatives considered**: A new top-level navigation **area** (like Execution/Configuration) — rejected; the spec asks for a tab within authoring, consistent with sibling entities.

## Performance & scale

- **Target**: ~50 queues, ~100 entries/queue (SC-007, single-operator tool). File listing of ≤50 small JSON files and in-memory entry lists are trivially within a <1s interaction budget. No special indexing, paging, or caching required this iteration.
