# Implementation Plan: Key Input and Swipe Primitive Actions

**Branch**: `054-key-swipe-actions` | **Date**: 2026-06-04 | **Spec**: [spec.md](spec.md)
**Input**: Feature specification from `specs/054-key-swipe-actions/spec.md`

## Summary

Add `KeyInput` and `Swipe` as selectable step types in the command editor UI, following the existing patterns for `Tap`, `WaitForImage`, and `EnsureGameRunning`. The domain-layer action variant classes (`PrimitiveKeyAction`, `PrimitiveSwipeAction`) and their validation logic already exist; the work is exposing them through the `CommandStep` type system (domain + service layers) and implementing two new React panel components on the frontend.

## Technical Context

**Language/Version**: C# (.NET 8) — backend; TypeScript 5 + React 18 — frontend
**Primary Dependencies**: ASP.NET Core (service layer), React + dnd-kit (UI drag-and-drop), System.Text.Json (serialization)
**Storage**: JSON files on disk (command persistence via existing file-based store)
**Testing**: xUnit — backend unit + integration tests; Jest + React Testing Library — frontend
**Target Platform**: Windows desktop application (single-user, web UI served locally)
**Project Type**: Desktop application with embedded web UI
**Performance Goals**: UI panel interactions feel immediate (<100 ms response to user input); no throughput requirements for a UI form feature
**Constraints**: New step types must serialize/deserialize cleanly alongside existing `CommandStep` JSON; `CommandStepType` enum must remain backward-compatible

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

| Gate | Status | Notes |
|------|--------|-------|
| Build passing | ✅ Assumed green (clean branch) | Must remain green throughout |
| Tests passing | ✅ Assumed green | New tests required per Testing Standards |
| Lint/format clean | ✅ | No new high/critical issues permitted; method names use CamelCase |
| UX consistency | ✅ | Panels follow established `TapPanel` / `EnsureGameRunningPanel` patterns |
| Performance goals documented | ✅ | Declared above |
| Security scan | ✅ | No secrets; no user-facing security surface |

*No violations. No Complexity Tracking entry required.*

## Project Structure

### Documentation (this feature)

```text
specs/054-key-swipe-actions/
├── plan.md              # This file
├── research.md          # Phase 0 output
├── data-model.md        # Phase 1 output
├── contracts/
│   └── command-step-types.md   # Phase 1 output
└── tasks.md             # Phase 2 output (/speckit-tasks — not created by /speckit-plan)
```

### Source Code (repository root)

```text
src/
├── GameBot.Domain/
│   └── Commands/
│       └── CommandStep.cs              # Add KeyInput + Swipe variants to enum + new config classes
├── GameBot.Service/
│   ├── Models/
│   │   └── Commands.cs                 # Add DTO enum variants + config DTO classes + CommandStepDto props
│   └── Services/
│       └── CommandExecutor.cs          # Add execution handlers for KeyInput + Swipe steps
└── web-ui/src/
    ├── components/commands/
    │   ├── ActionTypeSelector.tsx       # Add 'KeyInput' | 'Swipe' to type union + dropdown options
    │   ├── CommandForm.tsx              # Add StepEntry variants, panel rendering, toStepItems branches
    │   ├── KeyInputPanel.tsx            # New component (mirrors TapPanel pattern, single text field)
    │   ├── SwipePanel.tsx               # New component (four integer fields + optional duration)
    │   └── CommandForm.css              # Add .action-panel--key-input and .action-panel--swipe classes
    └── services/
        └── commands.ts                 # Add 'KeyInput' | 'Swipe' to CommandStepDto type + config types
```

**Structure Decision**: Web application layout — backend under `src/GameBot.*`, frontend under `src/web-ui/src`.
