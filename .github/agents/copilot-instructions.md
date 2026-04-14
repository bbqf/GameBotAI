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
- 034-background-screenshot-service: Added C# 13 / .NET 9 (backend); TypeScript ES2020 / React 18 (frontend) + ASP.NET Core Minimal API, SharpAdbClient/ADB integration (via existing `AdbClient`), System.Drawing (Bitmap), existing `ISessionManager`, `IScreenSource`, `CachedScreenSource` infrastructure
- 033-command-loops: Added C# 13 / .NET 9 (backend); TypeScript ES2020 / React 18 (frontend) + ASP.NET Core Minimal API, `System.Text.Json` (polymorphic serialization), existing `SequenceRunner` + `SequenceStepCondition` infrastructure, React 18 + Vite 5
- 033-command-loops: Added [e.g., Python 3.11, Swift 5.9, Rust 1.75 or NEEDS CLARIFICATION] + [e.g., FastAPI, UIKit, LLVM or NEEDS CLARIFICATION]


<!-- MANUAL ADDITIONS START -->
<!-- MANUAL ADDITIONS END -->
