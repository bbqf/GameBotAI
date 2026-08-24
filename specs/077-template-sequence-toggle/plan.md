# Implementation Plan: Enable/Disable Template Sequences

**Branch**: `077-template-sequence-toggle` | **Date**: 2026-07-30 | **Spec**: [spec.md](spec.md)
**Input**: Feature specification from `/specs/077-template-sequence-toggle/spec.md`

## Summary

Add a per-entry **enabled/disabled** state to queue template entries. A disabled entry stays in the template (position, schedule, sequence reference all preserved) but is excluded when a queue run is built from the template, so it never fires and never participates in scheduling. The state is surfaced as an on/off switch on each sequence card in the existing template scheduling-areas editor, carried in the existing template save payload, and persisted to durable template JSON. Legacy entries with no stored state default to **on**.

Technical approach: add `Enabled` (bool, default `true`) to `QueueTemplateEntry`; thread it through the save request contract, the detail response contract, and the endpoint mapping; filter it out in one place at run build (`QueueExecutionService` where `template.Entries` is materialized) and in the runtime-materialization/auto-load paths so `GET` matches what runs; and in the web UI carry `enabled` on the `EntrySchedule` view-model, render a switch on `SchedulingSequenceCard`, and include it in the template save/load round-trip. No new storage system, no new endpoint, no schema migration (default-on is achieved by the property initializer surviving absent JSON).

## Technical Context

**Language/Version**: C# / .NET 9 (backend service, `GameBot.Domain` + `GameBot.Service`; all csproj target `net9.0`); TypeScript + React (Vite) for `web-ui`
**Primary Dependencies**: none new — System.Text.Json (existing), @dnd-kit (existing, unchanged)
**Storage**: existing file-backed template JSON under `data/queue-templates/*.json` (`FileQueueTemplateRepository`); one new boolean field per entry, backward-compatible
**Testing**: xUnit + FluentAssertions (backend, `tests/unit`, `tests/integration`); Jest + React Testing Library (`web-ui`)
**Target Platform**: Windows service host + web UI
**Project Type**: Web (backend .NET 9 service + separate web-ui front end); both change. Backend test projects: `tests/unit`, `tests/integration`, `tests/contract`.
**Performance Goals**: No measurable impact — one boolean per entry; run-build adds a single `Where(e => e.Enabled)` over a handful of entries
**Constraints**: Backward compatibility (legacy templates with no `Enabled` field MUST read as enabled); no change to any other schedule source's behavior; CamelCase method names only
**Scale/Scope**: ~1 domain field, 2 contract fields, 1 endpoint mapping change, 1–3 execution/materialization filter points, ~4-7 web-ui files; unit + integration + jest tests

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

*NON-NEGOTIABLE*: If `build` or required `test` runs are failing (local or CI), implementation progression is blocked until failures are fixed or a documented maintainer waiver exists.

- **I. Code Quality Discipline**: PASS — small, cohesive additive change; no dead code; new public/serialized members get XML docs; CamelCase only; no new dependencies.
- **II. Testing Standards**: PASS — new unit tests for the domain default, the endpoint round-trip (save→read preserves `enabled`; absent→true), the run-build filter (disabled entries excluded from every partition; enabled untouched), and a jest test for the card switch + save payload. Deterministic and isolated.
- **III. UX Consistency**: PASS — switch reuses existing card layout and disabled-state conventions; disabled entries render visibly "off"; no breaking interface change; wire values for schedule types unchanged.
- **IV. Performance**: PASS — negligible; a single linear `Where` at run start over a small entry list. No hot-path regression.
- **V. Living Documentation (NON-NEGOTIABLE)**: `docs/architecture.md` describes the queue-template persistence layout and the queue-run build; it MUST gain the `Enabled` entry field and the "disabled entries are excluded at run build" behavior, with a refreshed "Last reviewed" date. This spec carries a `Status` line; `specs/STATUS.md` gets a row for 077. No earlier spec is superseded (this is additive to the template/queue-run features 047/051), so no prior `Status` line changes.

No violations → Complexity Tracking not required.

## Project Structure

### Documentation (this feature)

```text
specs/077-template-sequence-toggle/
├── plan.md              # This file
├── spec.md              # Feature spec (with Clarifications)
├── research.md          # Phase 0 output
├── data-model.md        # Phase 1 output
├── quickstart.md        # Phase 1 output
├── contracts/
│   └── template-entry-enabled.md   # Phase 1 output (API contract delta)
├── checklists/
│   └── requirements.md  # Spec quality checklist
└── tasks.md             # Phase 2 output (/speckit-tasks)
```

### Source Code (repository root)

```text
src/GameBot.Domain/QueueTemplates/
└── QueueTemplateEntry.cs            # CHANGE: add `bool Enabled { get; set; } = true;` + XML doc

src/GameBot.Service/Contracts/QueueTemplates/
├── TemplateEntrySaveRequest.cs      # CHANGE: add `bool? Enabled` (null → true)
└── QueueTemplateDetailResponse.cs   # CHANGE: add `bool Enabled` to QueueTemplateEntryResponse (default true)

src/GameBot.Service/Endpoints/
├── QueueTemplatesEndpoints.cs       # CHANGE: map Enabled on save (null→true) and in BuildDetailAsync
└── QueuesEndpoints.cs               # CHANGE: MaybeAutoLoadAsync SetEntries excludes disabled (keep GET==run)

src/GameBot.Service/Services/QueueExecution/
└── QueueExecutionService.cs         # CHANGE: exclude disabled entries when materializing template.Entries
                                     #   at run build (~line 182) and runtime SetEntries (~line 136)

src/web-ui/src/
├── services/queueTemplates.ts       # CHANGE: add `enabled` to QueueTemplateEntryDto + TemplateEntrySaveDto
├── components/queues/QueueEntryList.tsx        # CHANGE: EntrySchedule gains `enabled?: boolean`
├── components/queues/schedulingAreas.ts        # CHANGE: SchedulingCard carries enabled; group passes it through
├── components/queues/SchedulingSequenceCard.tsx# CHANGE: render on/off switch, onToggleEnabled callback, dim when off
├── components/queues/SchedulingArea.tsx        # CHANGE: forward onToggleEnabled
├── components/queues/QueueSchedulingAreas.tsx  # CHANGE: forward onToggleEnabled
└── pages/QueuesPage.tsx             # CHANGE: toggle handler, include enabled in save payload + load

tests/
├── unit/... (domain default, endpoint round-trip, run-build filter)
├── integration/Queues/... (queue run skips disabled entries end to end)
└── web-ui .../__tests__ (card switch renders + toggles + save payload carries enabled)

docs/
├── architecture.md                  # CHANGE: template-entry Enabled field + run-build exclusion
└── ../specs/STATUS.md               # CHANGE: add 077 row
```

**Structure Decision**: Additive change spanning the backend domain/service and the web-ui, because the feature is inherently full-stack (persist a field + honor it at run + surface a control). The execution behavior is centralized to a single filter point so no schedule-type pass needs its own change.

## Complexity Tracking

No constitution violations; section intentionally empty.
