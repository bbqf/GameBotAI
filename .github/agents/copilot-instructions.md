# GameBot Development Guidelines

Auto-generated from all feature plans. Last updated: 2026-03-03

## Active Technologies
- C# 13 / .NET 9 (backend), TypeScript ES2020 / React 18 (web UI) + ASP.NET Core Minimal API, existing GameBot.Domain sequence/command services, existing image detection pipeline, existing execution-log services, existing JSON repositories (031-sequence-conditional-steps)
- File-backed JSON repositories under `data/commands/sequences` and existing image metadata stores under `data/images` (031-sequence-conditional-steps)
- C# 13 / .NET 9 (backend), TypeScript ES2020 / React 18 (frontend) + ASP.NET Core Minimal API, existing GameBot.Domain sequence/command services, existing image detection pipeline, existing execution-log services, existing action execution infrastructure (031-sequence-conditional-steps)
- File-backed JSON repositories under `data/commands/sequences` and image metadata under `data/images` (031-sequence-conditional-steps)
- C# 13 / .NET 9 (backend), TypeScript ES2020 / React 18 (frontend) + ASP.NET Core Minimal API, GameBot.Domain sequence services/repositories, existing condition evaluator stack, React/Vite web-ui authoring modules (032-per-step-conditions)
- File-backed JSON repositories under `data/commands/sequences` (no new datastore) (032-per-step-conditions)
- [e.g., Python 3.11, Swift 5.9, Rust 1.75 or NEEDS CLARIFICATION] + [e.g., FastAPI, UIKit, LLVM or NEEDS CLARIFICATION] (033-command-loops)
- [if applicable, e.g., PostgreSQL, CoreData, files or N/A] (033-command-loops)
- C# 13 / .NET 9 (backend); TypeScript ES2020 / React 18 (frontend) + ASP.NET Core Minimal API, `System.Text.Json` (polymorphic serialization), existing `SequenceRunner` + `SequenceStepCondition` infrastructure, React 18 + Vite 5 (033-command-loops)
- File-backed JSON sequence repository under `data/` (`FileSequenceRepository`); global config under `data/config/config.json` (033-command-loops)
- C# 13 / .NET 9 (backend); TypeScript ES2020 / React 18 (frontend) + ASP.NET Core Minimal API, SharpAdbClient/ADB integration (via existing `AdbClient`), System.Drawing (Bitmap), existing `ISessionManager`, `IScreenSource`, `CachedScreenSource` infrastructure (034-background-screenshot-service)
- In-memory only (no persistence); session-scoped cached frames held in `ConcurrentDictionary` (034-background-screenshot-service)
- C# 13 / .NET 9 (backend), TypeScript ES2020 / React 18 (frontend) + ASP.NET Core Minimal API, existing `ConfigSnapshotService` + `IConfigApplier`, React 18 + Vite 7, HTML5 Drag and Drop API (no external DnD library) (035-ui-config-editor)
- File-backed JSON under `data/config/config.json` (existing); parameter order persisted as key order in the JSON `parameters` object (035-ui-config-editor)
- C# 13 / .NET 9 + ASP.NET Core Minimal API, OpenCvSharp (TemplateMatcher), SharpAdbClient/ADB integration, Microsoft.Extensions.Logging, existing `GameBot.Domain` and `GameBot.Emulator` libraries (036-tap-wait-retry)
- Existing file-backed JSON repositories under `data/` (no new stores); configuration via `AppConfig` singleton (036-tap-wait-retry)
- C# 13 / .NET 9 (backend service/domain), TypeScript ES2020 / React 18 (authoring consumer) + ASP.NET Core Minimal API, existing `GameBot.Domain` command/sequence models and `SequenceRunner`, existing file-backed repositories, Swagger/OpenAPI generation in `GameBot.Service` (001-sequence-random-delay)
- Existing file-backed JSON under `data/commands/sequences` (001-sequence-random-delay)
- Backend C# 13 / .NET 9; Frontend TypeScript ES2020 / React 18 (Vite 5) + ASP.NET Core Minimal API, GameBot.Domain repositories/services, System.Text.Json, existing OpenCvSharp/ADB/session services, React + existing web-ui toolchain (001-primitive-actions-refactor)
- File-backed JSON under `data/` (notably `data/commands`, `data/commands/sequences`, `data/config`); Action storage removed from authored model (001-primitive-actions-refactor)
- C# 13 / .NET 9 (backend), TypeScript ES2020 / React 18 (web UI) + ASP.NET Core Minimal API, existing `GameBot.Domain` command/action/logging models, `CommandExecutor`, existing detection pipeline (`IReferenceImageStore`, `IScreenSource`, `ITemplateMatcher`), existing web-ui command authoring and execution-log APIs (001-wait-for-image)
- Existing file-backed JSON command repository under `data/commands` and existing file-backed execution-log repository under `data/execution-logs`; no new persistence store (001-wait-for-image)
- Backend C# 13 / .NET 9, Frontend TypeScript ES2020 / React 18 + ASP.NET Core Minimal API, existing `GameBot.Domain` sequence repository and runner, existing execution-log service, React/Vite authoring UI, existing command repository contracts (001-fix-sequence-step-names)
- File-backed JSON under `data/commands/sequences`, `data/commands`, and `data/execution-logs` (001-fix-sequence-step-names)
- TypeScript 5.x / React 18 + React Testing Library, Jest, @dnd-kit/core, @dnd-kit/sortable (053-command-editor-rework)
- N/A (UI-only change) (053-command-editor-rework)

- Backend C# 13 / .NET 9, Frontend TypeScript ES2020 / React 18 + ASP.NET Core Minimal API, existing `GameBot.Domain` sequence/command services, existing image detection pipeline, existing execution-log services/repositories, React 18 + Vite 5 UI stack (030-sequence-conditional-logic)

## Project Structure

```text
backend/
frontend/
tests/
```

## Commands

npm test; npm run lint

## Code Style

Backend C# 13 / .NET 9, Frontend TypeScript ES2020 / React 18: Follow standard conventions

## Recent Changes
- 053-command-editor-rework: Added TypeScript 5.x / React 18 + React Testing Library, Jest, @dnd-kit/core, @dnd-kit/sortable
- 001-fix-sequence-step-names: Added Backend C# 13 / .NET 9, Frontend TypeScript ES2020 / React 18 + ASP.NET Core Minimal API, existing `GameBot.Domain` sequence repository and runner, existing execution-log service, React/Vite authoring UI, existing command repository contracts
- 001-wait-for-image: Added C# 13 / .NET 9 (backend), TypeScript ES2020 / React 18 (web UI) + ASP.NET Core Minimal API, existing `GameBot.Domain` command/action/logging models, `CommandExecutor`, existing detection pipeline (`IReferenceImageStore`, `IScreenSource`, `ITemplateMatcher`), existing web-ui command authoring and execution-log APIs


<!-- MANUAL ADDITIONS START -->
<!-- MANUAL ADDITIONS END -->
