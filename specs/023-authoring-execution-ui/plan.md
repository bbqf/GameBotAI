# Implementation Plan: Authoring & Execution UI Visual Polish

**Branch**: 023-authoring-execution-ui | **Date**: 2026-01-28 | **Spec**: [specs/023-authoring-execution-ui/spec.md](specs/023-authoring-execution-ui/spec.md)

**Input**: Feature specification from /specs/023-authoring-execution-ui/spec.md

## Summary

Improve authoring and execution UI reliability and clarity: persist detection settings, auto-use and surface cached sessions, show a running sessions list with stop controls (auto-stop old sessions per game/emulator), and enforce consistent, non-overlapping layouts across authoring/execution screens. Approach: extend existing ASP.NET Core minimal API to expose running session state and cache semantics, update React/Vite UI to render session banner and running list with stop behavior, and harden detection persistence against validation-induced data loss.

## Technical Context

**Language/Version**: Backend C# 13 / .NET 9; Frontend TypeScript (ES2020) / React 18 (Vite 5)

**Primary Dependencies**: ASP.NET Core Minimal API, existing GameBot.Domain repos, SharpAdbClient/ADB + System.Drawing/OpenCvSharp (already present), file-based JSON stores under data/, React Testing Library + Playwright for web UI tests; no new external packages.

**Storage**: File-based JSON repos under data/ (commands, triggers, config); running session cache kept in service memory; no new persistence.

**Testing**: xUnit + coverlet for backend; React Testing Library/Jest + Playwright for frontend flows; ensure coverage targets per constitution.

**Target Platform**: Windows (development and runtime), ASP.NET Core service + React web UI.

**Project Type**: Web (backend API + React frontend).

**Performance Goals**: Running sessions list fetch p95 < 300 ms server-side; UI render/update of banner and list < 100 ms after data arrival; detection save/rehydrate round-trip < 500 ms p95 during authoring flows.

**Constraints**: No new external dependencies or data stores; reuse existing JSON schemas and session management; Windows-only tooling; avoid WebSocket/new infra—prefer polling.

**Scale/Scope**: Execution UI and authoring surfaces within existing web-ui; limited to command create/edit and execution panels (~5 screens/components).

## Constitution Check

- Code Quality: Follow existing analyzers/formatting; keep session list logic small and cohesive; no unused deps.
- Testing: Add/extend xUnit and React Testing Library/Playwright coverage for detection persistence, cached session defaulting, session banner, running list auto-stop; maintain >=80% line and >=70% branch coverage for touched code.
- UX Consistency: Keep messages/action labels aligned with current UI conventions; actionable errors when session cache missing/stale; consistent button sizing.
- Performance: Adhere to listed p95 targets; avoid N+1 calls when refreshing running sessions (batch fetch + 2s polling interval).

## Project Structure

### Documentation (this feature)

```text
specs/023-authoring-execution-ui/
├── plan.md
├── research.md
├── data-model.md
├── quickstart.md
└── contracts/
```

### Source Code (repository root)

```text
src/
├── GameBot.Service/           # ASP.NET Core Minimal API; session endpoints, detection persistence
├── GameBot.Domain/            # Domain models/repos for commands/triggers/sessions
├── GameBot.Emulator/          # Emulator session integration
└── web-ui/                    # React 18 + Vite execution/authoring UI

tests/
├── unit/
├── integration/
└── contract/
```

**Structure Decision**: Single backend service with React frontend; reuse existing domain/repos and web-ui app.

## Complexity Tracking

No constitution violations anticipated; no additional complexity justifications required.
