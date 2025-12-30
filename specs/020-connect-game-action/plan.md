# Implementation Plan: Connect to game action

**Branch**: `020-connect-game-action` | **Date**: 2025-12-30 | **Spec**: [specs/020-connect-game-action/spec.md](specs/020-connect-game-action/spec.md)
**Input**: Feature specification from /specs/020-connect-game-action/spec.md

## Summary

Implement a new action type "Connect to game" that requires selecting a game and adbSerial, executes POST /api/sessions synchronously (30s cap), returns and caches sessionId scoped to game+adbSerial, and makes sessionId optional on command execution endpoints via auto-injection when available.

## Technical Context

**Language/Version**: C# (.NET 9) for backend; TypeScript (ES2020) + React 18 for frontend  
**Primary Dependencies**: ASP.NET Core Minimal API, SharpAdbClient/ADB integration, existing JSON repositories, React/Vite toolchain  
**Storage**: File-based JSON repositories (data/), client localStorage for session cache  
**Testing**: xUnit + coverlet (backend), React Testing Library/Jest + Playwright (frontend)  
**Target Platform**: Windows  
**Project Type**: Web (backend API + React frontend)  
**Performance Goals**: /api/sessions responds within 30s (target <5s typical); device suggestions within 2s; no added hot-path regressions  
**Constraints**: Session reuse scoped to game+adbSerial; sessionId optional only when cache hit; timeout enforced for session creation; no new persistence stores beyond existing JSON/localStorage  
**Scale/Scope**: Single-operator command sequences; existing data set sizes for actions/games/devices

## Constitution Check

Planned work aligns with the GameBot Constitution:
- Code Quality: follow .NET/TS lint/format, maintain modular action type addition, keep security scan clean (no secrets, reuse existing logging patterns).
- Testing: add unit/integration coverage for session timeout and optional sessionId paths; frontend RTL/e2e for authoring and execution flows; maintain coverage baselines.
- UX Consistency: keep API shapes stable while making sessionId optional with clear errors; surface actionable messages on missing/expired sessions; show sessionId on success.
- Performance: enforce 30s cap on session creation; keep suggestions under 2s; avoid reuse across mismatched game/device to prevent retries/regressions.

## Project Structure

### Documentation (this feature)

```text
specs/020-connect-game-action/
├── plan.md
├── research.md
├── data-model.md
├── quickstart.md
├── contracts/
└── tasks.md (created by /speckit.tasks)
```

### Source Code (repository root)

```text
src/
├── GameBot.Domain/
├── GameBot.Service/
├── GameBot.Emulator/
└── web-ui/

tests/
├── contract/
├── integration/
└── unit/
```

**Structure Decision**: Use existing backend (GameBot.Service + Domain) for API and action model changes; update web-ui for authoring/execution UI and client session cache.

## Complexity Tracking

No constitution violations anticipated; no additional complexity waivers required.
