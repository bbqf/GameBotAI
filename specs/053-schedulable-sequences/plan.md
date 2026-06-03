# Implementation Plan: Queue Sequence Scheduling

**Branch**: `053-schedulable-sequences` | **Date**: 2026-06-03 | **Spec**: [spec.md](spec.md)  
**Input**: Feature specification from `specs/053-schedulable-sequences/spec.md`

## Summary

Extend queue template entries with a `ScheduleType` field (`OncePerRun` / `EveryStep` / `Timer`) and refactor the queue execution loop to respect those types. `EveryStep` sequences run after each regular step; `Timer` sequences fire once per calendar day at a configured wall-clock time (server local), checked at iteration boundaries. Schedule type is configurable per template entry via API and UI. All existing behavior is preserved via a `OncePerRun` default.

## Technical Context

**Language/Version**: C# / .NET 9 (backend) · TypeScript / React 18 (frontend, Vite)  
**Primary Dependencies**: ASP.NET Core Minimal API · System.Text.Json · XUnit / FluentAssertions (tests) · Jest / React Testing Library (UI tests)  
**Storage**: File-based JSON under `data/queue-templates/` — `FileQueueTemplateRepository`  
**Testing**: XUnit (unit + integration + contract) · Jest (frontend)  
**Target Platform**: Windows desktop (single-operator local tool)  
**Project Type**: Desktop web-app (React UI + .NET local server)  
**Performance Goals**: Timer evaluation at iteration boundaries is O(|timerEntries|) — negligible overhead  
**Constraints**: Timer precision = best-effort (fires at next iteration boundary after scheduled time); no sub-second precision required  
**Scale/Scope**: Up to ~50 templates, ~100 entries each, ~10 every-step sequences per template

## Constitution Check

| Gate | Status | Notes |
|------|--------|-------|
| Build passes | Required — verify before and after each phase | Run `dotnet build` + `vite build` |
| Unit tests pass | Required | `dotnet test tests/unit` |
| Integration tests pass | Required | `dotnet test tests/integration` |
| Lint/format clean | Required | No new warnings in modified C# or TSX files |
| CamelCase methods | Required | No underscores in new method names |
| Public API docstrings | Required | New public types and their properties need summaries |
| Coverage ≥ 80% lines | Required | New logic in `QueueExecutionService`, `ScheduleType`, entry models |
| Performance note | Required | Document timer evaluation complexity in plan (done above) |

*Gate re-check after Phase 1 design confirms no new violations.*

## Project Structure

### Documentation (this feature)

```text
specs/053-schedulable-sequences/
├── plan.md              ← this file
├── spec.md
├── research.md          ← Phase 0 output
├── data-model.md        ← Phase 1 output
├── quickstart.md        ← Phase 1 output
├── contracts/
│   └── queue-template-schedule.openapi.yaml  ← Phase 1 output
└── tasks.md             ← /speckit-tasks output (not yet)
```

### Source Code

```text
src/GameBot.Domain/
└── QueueTemplates/
    ├── ScheduleType.cs           NEW — enum (OncePerRun, EveryStep, Timer)
    ├── QueueTemplateEntry.cs     MOD — add ScheduleType + TimerTimeOfDay
    ├── QueueTemplate.cs          no change
    ├── IQueueTemplateRepository.cs  no change
    └── FileQueueTemplateRepository.cs  no change (JSON serialization picks up new fields automatically)

src/GameBot.Service/
├── Contracts/QueueTemplates/
│   ├── TemplateEntrySaveRequest.cs     NEW — { SequenceId, ScheduleType, TimerTimeOfDay }
│   ├── SaveQueueTemplateRequest.cs     MOD — replace SequenceIds[] with Entries[]
│   └── QueueTemplateDetailResponse.cs  MOD — add ScheduleType + TimerTimeOfDay to QueueTemplateEntryResponse
├── Endpoints/
│   └── QueueTemplatesEndpoints.cs      MOD — update SaveQueueTemplate handler + validation
└── Services/QueueExecution/
    └── QueueExecutionService.cs        MOD — refactor RunAsync loop for schedule types

src/web-ui/src/
├── services/
│   ├── queueTemplates.ts    MOD — update SaveQueueTemplateRequest type + entry types
│   └── queues.ts            MOD — update QueueEntryDto (if schedule type surfaced in runtime entries)
└── components/queues/
    └── QueueEntryList.tsx   MOD — add schedule type selector + timer time input per entry

tests/
├── unit/Queues/
│   └── QueueExecutionServiceTests.cs   MOD — add every-step + timer scheduling tests
└── integration/QueueTemplates/
    ├── QueueTemplatesSaveEndpointTests.cs   MOD — update for new Entries[] shape
    └── QueueTemplatesScheduleTypeTests.cs   NEW — focused schedule-type persistence + retrieval tests
```

## Implementation Phases

### Phase A: Domain model (backend)

**Files**: `ScheduleType.cs` (new), `QueueTemplateEntry.cs` (mod)

1. Create `src/GameBot.Domain/QueueTemplates/ScheduleType.cs`:
   - `public enum ScheduleType { OncePerRun = 0, EveryStep = 1, Timer = 2 }`
   - Apply `[JsonConverter(typeof(JsonStringEnumConverter))]` at the enum level.

2. Extend `QueueTemplateEntry`:
   - Add `public ScheduleType ScheduleType { get; set; } = ScheduleType.OncePerRun;`
   - Add `public TimeOnly? TimerTimeOfDay { get; set; }`
   - Update XML summary to document both new fields.

3. No changes to `FileQueueTemplateRepository` — `System.Text.Json` serializes the new fields automatically; existing JSON files deserialize with `OncePerRun` default.

**Gate**: `dotnet build` clean; existing unit tests still pass.

---

### Phase B: API contracts and endpoint (backend)

**Files**: `TemplateEntrySaveRequest.cs` (new), `SaveQueueTemplateRequest.cs` (mod), `QueueTemplateDetailResponse.cs` (mod), `QueueTemplatesEndpoints.cs` (mod)

1. Create `TemplateEntrySaveRequest`:
   ```
   SequenceId: string?
   ScheduleType: string?    // "OncePerRun" | "EveryStep" | "Timer"; null → OncePerRun
   TimerTimeOfDay: string?  // "HH:mm" (24-hour); required when ScheduleType == "Timer"
   ```

2. Update `SaveQueueTemplateRequest`:
   - Remove `string[]? SequenceIds`.
   - Add `TemplateEntrySaveRequest[]? Entries`.

3. Extend `QueueTemplateEntryResponse`:
   - Add `string ScheduleType { get; set; }` (string representation: `"OncePerRun"` / `"EveryStep"` / `"Timer"`).
   - Add `string? TimerTimeOfDay { get; set; }` (`"HH:mm"` or null).

4. Update `QueueTemplatesEndpoints.SaveQueueTemplate` handler:
   - Replace the `foreach (var sid in req.SequenceIds)` loop with a loop over `req.Entries`.
   - Add validation per entry:
     - `SequenceId` non-blank.
     - `ScheduleType` parses to a known value.
     - When `ScheduleType == Timer`: `TimerTimeOfDay` present and parseable as `HH:mm`; if missing → 400.
     - When `ScheduleType != Timer`: ignore `TimerTimeOfDay`.
   - Update `BuildDetailAsync` to populate `ScheduleType` and `TimerTimeOfDay` in `QueueTemplateEntryResponse`.

**Gate**: `dotnet build` clean; existing integration tests updated for new request shape; new integration tests pass.

---

### Phase C: Execution loop refactor (backend)

**File**: `QueueExecutionService.cs`

Refactor `RunAsync` in these steps:

1. **Change snapshot** (line 114): replace `var snapshot = template.Entries.Select(e => e.SequenceId).ToList()` with capturing the full entries:
   ```csharp
   var allEntries = template.Entries.ToList();
   var oncePerRunEntries = allEntries.Where(e => e.ScheduleType == ScheduleType.OncePerRun).ToList();
   var everyStepEntries = allEntries.Where(e => e.ScheduleType == ScheduleType.EveryStep).ToList();
   var timerEntries = allEntries.Where(e => e.ScheduleType == ScheduleType.Timer).ToList();
   ```

2. **Initialize per-run timer state** — declare this **outside and before the `do {` keyword**, after the partition lists, so it persists across all cycles of the same run. Placing it inside the loop body would reset it every cycle and break the once-per-calendar-day invariant:
   ```csharp
   // OUTSIDE the do-while loop — persists across cycles
   var timerFiredDate = new Dictionary<int, DateOnly>();
   ```

3. **Rewrite the `do` loop** (currently lines 134–149):
   ```
   do {
     ct.ThrowIfCancellationRequested();

     // (1) Timer sequences at iteration boundary
     for (int ti = 0; ti < timerEntries.Count; ti++) {
       var timerEntry = timerEntries[ti];
       if (timerEntry.TimerTimeOfDay is not null) {
         var today = DateOnly.FromDateTime(DateTime.Now);
         var now   = TimeOnly.FromDateTime(DateTime.Now);
         if (now >= timerEntry.TimerTimeOfDay.Value &&
             (!timerFiredDate.TryGetValue(ti, out var lastFired) || lastFired != today)) {
           ct.ThrowIfCancellationRequested();
           if (sessions.GetSession(sessionId) is null) throw new QueueConnectionLostException();
           var ok = await RunOneSequenceAsync(timerEntry.SequenceId, rootId, ++index, sessionId, ct);
           executed++; if (!ok) failed++;
           timerFiredDate[ti] = today;
         }
       }
     }

     // (2) Once-per-run sequences, each followed by every-step sequences
     if (oncePerRunEntries.Count > 0) {
       foreach (var entry in oncePerRunEntries) {
         ct.ThrowIfCancellationRequested();
         if (sessions.GetSession(sessionId) is null) throw new QueueConnectionLostException();
         var ok = await RunOneSequenceAsync(entry.SequenceId, rootId, ++index, sessionId, ct);
         executed++; if (!ok) failed++;

         foreach (var esEntry in everyStepEntries) {
           ct.ThrowIfCancellationRequested();
           if (sessions.GetSession(sessionId) is null) throw new QueueConnectionLostException();
           var esOk = await RunOneSequenceAsync(esEntry.SequenceId, rootId, ++index, sessionId, ct);
           if (!esOk) failed++;
           // every-step does not count toward executed (SC-002)
         }
       }
     }
     else if (everyStepEntries.Count > 0) {
       // FR-009: no once-per-run entries — every-step runs exactly once
       foreach (var esEntry in everyStepEntries) {
         ct.ThrowIfCancellationRequested();
         if (sessions.GetSession(sessionId) is null) throw new QueueConnectionLostException();
         var esOk = await RunOneSequenceAsync(esEntry.SequenceId, rootId, ++index, sessionId, ct);
         if (!esOk) failed++;
       }
     }

     cycles++;
   } while (queue.CycleExecution);
   ```

4. The empty-template guard (`snapshot.Count > 0` check at line 133) is replaced by the natural behavior: if all three partitions are empty, the loop body does nothing, `cycles = 1`, and the run ends.

5. Update `BuildSummary` to reflect that `executed` counts once-per-run sequences only (not every-step or timer). Optionally add a brief note in the summary when every-step or timer sequences also ran.

**Gate**: `dotnet build` clean; existing and new unit tests (every-step + timer scenarios) pass.

---

### Phase D: Frontend (TypeScript / React)

**Files**: `queueTemplates.ts`, `queues.ts`, `QueueEntryList.tsx`

1. **`queueTemplates.ts`**: 
   - Define `TemplateEntrySaveDto { sequenceId: string; scheduleType?: string; timerTimeOfDay?: string }`.
   - Update `SaveQueueTemplateRequest` to use `entries: TemplateEntrySaveDto[]` (remove `sequenceIds`).
   - Update `QueueTemplateEntryDto` to include `scheduleType: string` and `timerTimeOfDay: string | null`.
   - Update `saveQueueTemplate` API call to pass `entries`.

2. **`QueueEntryList.tsx`** (this component is used for runtime queue entries, not template entries directly — evaluate whether it needs a schedule selector, or if schedule configuration lives in a separate template editing view):
   - The queue editor works with runtime `QueueEntryDto` (from `IQueueRuntimeStore`) — schedule type is a template-layer concept, not stored on runtime entries.
   - Schedule type is configured when saving a template (via the Save Template dialog) and loaded when loading a template.
   - **Approach**: Add a schedule type to the in-UI entry state used when building the save-template payload. The queue editor maintains an optional `scheduleType` per entry (UI-local state, not sent to runtime API), used only when saving a template.
   - Add per-entry schedule type selector (dropdown: Once Per Run / Every Step / Timer) and conditional time picker for Timer entries.
   - Show schedule type badge per entry in the list view (read-only, set when loaded from template).

3. **`QueuesPage.tsx`**: Update save-template flow to collect `scheduleType` and `timerTimeOfDay` from per-entry UI state and include them in the `saveQueueTemplate` call.

**Gate**: `vite build` clean; Jest tests for updated `QueueEntryList` pass.

---

### Phase E: Tests

**Unit tests** — `tests/unit/Queues/QueueExecutionServiceTests.cs`:

- Every-step: template with 2 once-per-run + 1 every-step → assert every-step runs after each once-per-run.
- Every-step count: assert `executed` reflects only once-per-run count.
- Timer due: timer time in the past → fires on first iteration before once-per-run.
- Timer not due: timer time in the future → never fires in a non-cyclic run.
- Timer fires once per calendar day: simulate two iterations on same date → timer fires once; simulate next-day iteration → fires again.
- Timer + every-step + once-per-run: assert correct execution order.
- No once-per-run, only every-step: every-step executes once, run ends.
- Empty template (all partitions empty): run completes immediately, no executions.

**Integration tests** — `tests/integration/QueueTemplates/`:

- `QueueTemplatesSaveEndpointTests.cs`: update existing tests for new `Entries[]` shape.
- `QueueTemplatesScheduleTypeTests.cs` (new):
  - Save template with `EveryStep` and `Timer` entries → GET returns correct schedule types.
  - Timer without `timerTimeOfDay` → 400.
  - Invalid `scheduleType` value → 400.
  - Missing `sequenceId` in entry → 400.
  - Existing template (saved before this feature) → GET returns `OncePerRun` for all entries (backward compatibility).

## Complexity Tracking

No constitution violations. No new projects, no new external dependencies, no repository pattern layers added. `FileQueueTemplateRepository` automatically serializes new fields through `System.Text.Json` without any changes.
