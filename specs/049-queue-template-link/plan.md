# Implementation Plan: Queue–Template Link with Auto-Load

**Branch**: `049-queue-template-link` | **Date**: 2026-06-01 | **Spec**: [spec.md](spec.md)
**Input**: Feature specification from `specs/049-queue-template-link/spec.md`

## Summary

Give each queue an optional, **persisted** link to a single template (0..1; a template
may be linked by 0..n queues), and **auto-load** that template's entries into the queue's
runtime when the queue's edit/detail page is opened. This converts the 047/048 UI-only
"remembered template name" into a durable, ID-based association stored on the queue config.

Approach (full-stack, thin slice):
- **Persist the link**: add `LinkedTemplateId` (string, nullable) to `ExecutionQueue`
  (the only durable part of a queue). It references the template by **stable ID** (FR-001a),
  so renaming a template does not break the link; only deletion does.
- **Auto-load on display**: `GET /api/queues/{id}` (what the edit page calls via `openEdit`)
  materializes the linked template into the queue's runtime entries **once per service
  lifetime per queue** — only when the queue has no runtime state yet (post-restart),
  is not Running, and the link resolves. If the linked template is gone, the link is
  cleared and persisted; the queue still opens empty without error.
- **Set the link**: a new `PUT /api/queues/{id}/template` `{ templateId | null }` endpoint
  sets/clears the link. It is **not** gated on Running (it touches no entries), so saving a
  template from a running queue can still associate it. The frontend calls it as a side
  effect of the existing load and save flows — **no new visual controls** (FR-005).
- The list endpoint (`GET /api/queues`) is intentionally untouched (Q3: only the detail
  page triggers auto-load).

No new dependencies. Reuses existing `IQueueRuntimeStore.SetEntries`, the template
repository, and the existing replace-entries/load/save UI plumbing.

## Technical Context

**Language/Version**: C# / .NET (GameBot.Domain, GameBot.Service minimal-API) backend;
TypeScript 5.6 / React 18.3 (web-ui) frontend.
**Primary Dependencies**: ASP.NET Core minimal APIs, System.Text.Json (backend); React +
Vite, existing in-house components/services (`QueuesPage`, `QueueTemplateControls`,
`queues.ts`, `queueTemplates.ts`) (frontend). No new packages.
**Storage**: File-backed JSON. Queue **config** persists under `data/queues/*.json`
(`FileQueueRepository`); the new `LinkedTemplateId` is one more property on that document.
Queue **entries/status** remain runtime-only (`QueueRuntimeStore`, in-memory singleton).
Templates persist under `data/queue-templates/*.json` (unchanged).
**Testing**: xUnit (unit/integration/contract) backend; Jest 29 + React Testing Library 14
frontend.
**Target Platform**: GameBot Windows service + authoring SPA in a modern browser.
**Project Type**: Web application (backend + frontend); this feature touches both.
**Performance Goals**: Opening a queue reflects entries <1s at the established scale
(≤~50 templates × ≤~100 entries) — inherits 046/047 budgets; auto-load is one template
read + one in-memory `SetEntries`.
**Constraints**: CamelCase method names only (no underscores); functions ≤50 LOC; reuse
existing error-envelope and UX conventions; auto-load must not block/err the display, must
not touch a Running queue's entries, must not re-fill a queue the operator deliberately
emptied (operationalized as "only when no runtime state exists yet"); link-clear on a
missing template must persist.
**Scale/Scope**: ~4 backend files + ~2 new contracts/endpoint changes; ~2 frontend files
touched; new tests on both sides. Backward-compatible: existing queue JSON without the new
field deserializes with `LinkedTemplateId = null` (unlinked).

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

*NON-NEGOTIABLE*: If `build` or required `test` runs are failing (local or CI),
implementation progression is blocked until failures are fixed or a documented maintainer
waiver exists.

| Gate (from constitution) | Status | Notes |
|--------------------------|--------|-------|
| Lint/format/static analysis clean | Must pass | ESLint (web-ui) + analyzers (C#); enforced in CI |
| No underscores in method names — CamelCase only | Must pass | New handlers/services/components CamelCase |
| Functions ≤50 LOC | Must pass | Auto-load helper extracted; link endpoint thin; frontend handlers stay small |
| Unit ≥80% line / ≥70% branch on touched areas | Must pass | Tests for auto-load matrix, link set/clear, runtime-store `HasRuntimeState`, frontend link wiring |
| Deterministic, isolated, fast tests | Must pass | xUnit with temp data roots; RTL with mocked services; no real I/O beyond temp dirs |
| UX consistency | Must pass | No new visual controls; reuses existing template name button/error envelope |
| Actionable error messages | Must pass | Link endpoint returns existing error envelope ("template not found"); display never errors on broken link |
| Performance goals declared | ✅ Declared | <1s open (one template read + in-memory set) |
| Public API/contract documented | ✅ Will update | New endpoint + new response field in `contracts/` and `specs/openapi.json`; contract tests updated |
| Observability | ✅ N/A | No new logging (consistent with 047: no template-action logging) |
| No unjustified new dependencies | ✅ None added | Reuses existing stack |

No constitution violations. No waivers required. No Complexity Tracking entries needed.

## Project Structure

### Documentation (this feature)

```text
specs/049-queue-template-link/
├── plan.md              # This file
├── research.md          # Phase 0 output (decisions & rationale)
├── data-model.md        # Phase 1 output (entities, fields, state)
├── quickstart.md        # Phase 1 output (manual verification)
├── contracts/
│   └── queue-template-link-api.md  # Phase 1 output — API contract (new endpoint + fields + auto-load)
├── checklists/
│   └── requirements.md  # Spec quality checklist (from /speckit-specify)
└── tasks.md             # Phase 2 output (/speckit-tasks — NOT created here)
```

### Source Code (repository root)

```text
src/GameBot.Domain/Queues/
├── ExecutionQueue.cs          # MODIFIED — add LinkedTemplateId (string?, persisted)
├── IQueueRuntimeStore.cs      # MODIFIED — add HasRuntimeState(queueId): bool
└── QueueRuntimeStore.cs       # MODIFIED — implement HasRuntimeState via _states.ContainsKey

src/GameBot.Service/Contracts/Queues/
├── QueueResponse.cs           # MODIFIED — add LinkedTemplateId
├── QueueDetailResponse.cs     # MODIFIED — add LinkedTemplateName (resolved, for display)
└── SetQueueTemplateLinkRequest.cs  # NEW — { TemplateId?: string }

src/GameBot.Service/Endpoints/
└── QueuesEndpoints.cs         # MODIFIED — auto-load in GET {id}; new PUT {id}/template;
                               #            include link fields in BuildResponse/BuildDetailAsync;
                               #            extract MaybeAutoLoadAsync + ResolveLinkName helpers

specs/openapi.json             # MODIFIED — document PUT {id}/template + new response fields

src/web-ui/src/services/
└── queues.ts                  # MODIFIED — add linkedTemplateId/linkedTemplateName to DTOs;
                               #            add setQueueTemplateLink(id, templateId|null)

src/web-ui/src/pages/
└── QueuesPage.tsx             # MODIFIED — openEdit derives associated name from persisted link;
                               #            applyLoad + handleSaveTemplate set the link by id;
                               #            handleLoadTemplate/handleReload thread templateId

tests/unit/Queues/
├── QueueRuntimeStoreHasStateTests.cs   # NEW — HasRuntimeState true/false lifecycle

tests/integration/Queues/
├── QueueTemplateLinkEndpointTests.cs   # NEW — PUT {id}/template set/clear/validate/persist/running-ok
└── QueueAutoLoadEndpointTests.cs       # NEW — GET {id} auto-load matrix (fresh/linked, running,
                                        #        state-exists, missing-template→clear, unlinked)

tests/contract/Queues/
└── QueuesApiContractTests.cs           # MODIFIED — new endpoint + response fields present

src/web-ui/src/services/__tests__/
└── queues.spec.ts (or queue link)      # NEW/MODIFIED — setQueueTemplateLink request shape

src/web-ui/src/pages/__tests__/
└── QueuesPage.link.spec.tsx            # NEW — link set on load & save; name from persisted link;
                                        #        auto-loaded entries shown on open
```

**Structure Decision**: Web application touching both tiers. The durable link lives on the
persisted queue config (`ExecutionQueue`), the only place a queue stores anything across
restarts. Auto-load is server-side on the queue **detail** GET so that (a) it triggers
exactly where the spec scopes it (the editor open, not the list), and (b) the materialized
entries are real runtime entries usable by execution (FR-006a), not a client-only view.
Link mutation is a small dedicated endpoint rather than folding into replace-entries,
because the link must be settable while a queue is Running (save-while-running) whereas
replace-entries is blocked when Running.

## Implementation Design

### Persisted link (Domain)

`ExecutionQueue` gains `public string? LinkedTemplateId { get; set; }`. It is written by
`FileQueueRepository.Update/CreateAsync` automatically (whole-object serialization).
Existing queue files lacking the property deserialize as `null` (unlinked) — no migration.

### Runtime "first display" guard

Add `bool HasRuntimeState(string queueId)` to `IQueueRuntimeStore`, implemented as
`_states.ContainsKey(queueId)`. This distinguishes "never materialized since startup"
(no key) from "exists but currently empty" (operator cleared it). Auto-load fires only
when **no** runtime state exists yet, which captures FR-012's intent ("only when the queue
has no entries", post-restart) without re-filling a queue the operator deliberately
emptied during the session. (Decision recorded in research.md.)

### Auto-load on GET detail (Service)

`GET /api/queues/{id}` handler gains `IQueueTemplateRepository` (resolve template) and uses
the already-present `IQueueRepository` (persist link-clear). Before building the detail:

```
MaybeAutoLoadAsync(queue, runtime, templates, repo):
  if queue.LinkedTemplateId is null: return
  if runtime.GetStatus(queue.Id) == Running: return        // FR-010
  if runtime.HasRuntimeState(queue.Id): return             // FR-012 (first display only)
  tpl = await templates.GetAsync(queue.LinkedTemplateId)
  if tpl is null:                                           // FR-011 broken link
    queue.LinkedTemplateId = null
    await repo.UpdateAsync(queue)                           // persist the clear
    return
  runtime.SetEntries(queue.Id, tpl.Entries.Select(e => e.SequenceId))   // FR-006/006a/008
```

Then `BuildDetailAsync` reads runtime entries as today (stale projection already handled,
covering FR-009). `BuildResponse`/`BuildDetailAsync` also emit `LinkedTemplateId` and a
resolved `LinkedTemplateName` (looked up from the template repo when building detail; null
if unlinked or unresolved). Helper kept ≤50 LOC.

### Set/clear link endpoint (Service)

`PUT /api/queues/{id}/template` with `SetQueueTemplateLinkRequest { string? TemplateId }`:
- 404 if queue not found.
- If `TemplateId` non-null and the template does not exist → `400 invalid_request`
  ("template not found") via the existing error envelope.
- Set `queue.LinkedTemplateId = TemplateId` (null clears), `repo.UpdateAsync`, return the
  queue detail. **Not** blocked while Running (touches no entries; enables save-while-running
  association).

### Frontend wiring (QueuesPage)

- `queues.ts`: `QueueDto`/`QueueDetailDto` gain `linkedTemplateId: string | null` and
  `linkedTemplateName: string | null`; add `setQueueTemplateLink(id, templateId)` →
  `PUT /api/queues/{id}/template`.
- `openEdit`: set `associatedTemplateName` from `q.linkedTemplateName ?? undefined`
  (replaces the current reset-to-undefined). The auto-loaded entries arrive in `q.entries`
  from the GET, so the editor shows them with no extra call.
- `applyLoad(name, sequenceIds, templateId)`: after `replaceQueueEntries`, call
  `setQueueTemplateLink(detail.id, templateId)`; thread `templateId` from
  `handleLoadTemplate` (`tpl.id`) and `handleReload` (`match.id`).
- `handleSaveTemplate`: `saveQueueTemplate` returns the detail incl. `id`; after save call
  `setQueueTemplateLink(detail.id, saved.id)` so saving associates the queue with that
  template (FR-005). Keep updating `associatedTemplateName` for immediate feedback.
- `QueueTemplateControls.tsx`: unchanged (still driven by `associatedTemplateName`).

## Contracts

New/changed REST surface (documented in `contracts/queue-template-link-api.md` and
`specs/openapi.json`):
- **NEW** `PUT /api/queues/{id}/template` — body `{ "templateId": string | null }`;
  200 → queue detail; 400 (template not found); 404 (queue not found).
- **CHANGED** `GET /api/queues/{id}` — response adds `linkedTemplateId`,
  `linkedTemplateName`; performs auto-load side effect (first display, not Running,
  resolvable link) and may clear a broken link.
- **CHANGED** `GET /api/queues` and other queue responses — add `linkedTemplateId`.
Reuses unchanged: `PUT /api/queues/{id}/entries`, `GET/POST/DELETE /api/queue-templates`.

## Testing Strategy

**Backend (xUnit)** — touched-area coverage ≥80% line / ≥70% branch:
- `QueueRuntimeStoreHasStateTests`: false before any op; true after Add/SetEntries/SetStatus;
  false again after `Remove`.
- `QueueAutoLoadEndpointTests` (integration): linked + fresh → entries materialized from
  template; linked + Running → not materialized; linked + state already exists → not
  re-filled; linked + template missing → entries empty AND link cleared+persisted; unlinked
  → empty, no error; persistence of materialized entries usable by a subsequent start.
- `QueueTemplateLinkEndpointTests` (integration): set link → persisted (re-read via new repo
  instance); set null clears; non-existent template → 400; non-existent queue → 404; link
  settable while Running.
- `QueuesApiContractTests` (contract): new endpoint present; responses expose
  `linkedTemplateId`/`linkedTemplateName`.

**Frontend (Jest + RTL)**:
- `queues` service test: `setQueueTemplateLink` issues `PUT {id}/template` with the body.
- `QueuesPage.link.spec.tsx`: loading a template calls `setQueueTemplateLink` with the
  template id; saving calls it with the saved id; opening a linked queue shows the
  auto-loaded entries and the linked name (mocked `getQueue` returns
  `linkedTemplateName` + entries); opening an unlinked queue shows "(no template)".

**No new logging tests** (no logging added). Manual verification in `quickstart.md`.

## Complexity Tracking

No constitution violations requiring justification.
