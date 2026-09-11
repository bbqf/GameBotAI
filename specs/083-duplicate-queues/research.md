# Research: Duplicate Queues

No unresolved `NEEDS CLARIFICATION` markers carried into planning — the spec's Assumptions
section and the domain-model research done during specification already settled the open
questions. This file records the resulting decisions for traceability.

## Decision: Where duplication lives in the API

**Decision**: A new endpoint `POST /api/queues/{id}/duplicate` accepting `{ "name": string }`,
returning `201 Created` with the same `QueueResponse` shape `POST /api/queues` returns, `Location`
header pointing at the new queue.

**Rationale**: Every other queue mutation in `QueuesEndpoints.cs` (`start`, `stop`, `entries`,
`template`, `game`) follows the `POST /api/queues/{id}/<verb>` sub-resource-action convention
rather than overloading the base collection route. Duplication is an action on an existing queue,
not a plain create, so it belongs in the same family.

**Alternatives considered**:
- Overload `POST /api/queues` with an optional `sourceQueueId` field — rejected: conflates two
  different validation paths (fresh create requires `emulatorSerial`; duplicate requires an
  existing source) into one handler, and breaks the existing `CreateQueueRequest` contract.
- Client-side duplication (fetch source, then call `createQueue` with copied fields) — rejected:
  can't copy runtime entries (client has no direct write access to `IQueueRuntimeStore` semantics
  beyond `replaceQueueEntries`), and duplicates business logic (threshold coercion, instance-name
  normalization) that already lives server-side.

## Decision: Name-difference validation, not global uniqueness

**Decision**: Reject only when the trimmed submitted name equals the source queue's current name
(ordinal, case-sensitive comparison, matching how queue `Name` is otherwise treated as a plain
opaque string with no case-folding anywhere in `ExecutionQueue`/`FileQueueRepository`). No check
against any other existing queue's name.

**Rationale**: Confirmed via the domain model (`ExecutionQueue.cs`, `FileQueueRepository.Validate`)
that queue names are explicitly documented as "need not be unique" and no existing validation
enforces uniqueness. Introducing a stricter uniqueness rule only for duplication would be an
inconsistent, surprising special case relative to every other queue-naming path (`CreateQueue`,
`UpdateQueue`).

**Alternatives considered**:
- Global uniqueness across all queues — rejected: inconsistent with existing `CreateQueue`/
  `UpdateQueue` behavior; not requested by the feature description, which only asked for the
  duplicate's name to differ from the original.

## Decision: Copy currently-loaded runtime entries, not just the template link

**Decision**: `IQueueRuntimeStore.GetEntries(sourceId)` is read and its sequence IDs are written
into the new queue via the existing `SetEntries(newId, sequenceIds)`, in addition to copying
`LinkedTemplateId`.

**Rationale**: A queue's "current entries" can diverge from its linked template (e.g., the user
added/removed/reordered entries without saving as a template yet). A duplicate that only copied
`LinkedTemplateId` and relied on the existing `MaybeAutoLoadAsync` auto-load would silently drop
that in-progress, unsaved state — not a true 1:1 copy. Reusing `SetEntries` means no new runtime
API is needed and the duplicate's entries are visible from its first `GET` (auto-load is a no-op
once `HasRuntimeState` is true).

**Alternatives considered**:
- Only copy `LinkedTemplateId` and let auto-load populate entries on first display — rejected:
  loses any unsaved entry edits on the source queue, which contradicts "1:1 copy of the original".

## Decision: Duplicate is always created stopped, with fresh runtime status

**Decision**: The duplicate is created via the normal `IQueueRepository.CreateAsync` path (same as
`POST /api/queues`), which never touches `IQueueRuntimeStore`'s status map — so the new queue's
status defaults to `Stopped` exactly like any newly created queue. No explicit "stop" step needed.

**Rationale**: `QueueExecutionStatus` is keyed by queue ID in the runtime store and only set by
`StartAsync`/`StopAsync`; a brand-new ID has never been started, so it is `Stopped` by construction
— matching FR-005/FR-008 without extra code.

**Alternatives considered**: None needed — this falls out of the existing runtime-store design for
free.
