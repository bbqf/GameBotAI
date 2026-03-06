# GameBot Development Guidelines

Auto-generated from all feature plans. Last updated: 2026-03-03

## Active Technologies
- C# 13 / .NET 9 (backend), TypeScript ES2020 / React 18 (web UI) + ASP.NET Core Minimal API, existing GameBot.Domain sequence/command services, existing image detection pipeline, existing execution-log services, existing JSON repositories (031-sequence-conditional-steps)
- File-backed JSON repositories under `data/commands/sequences` and existing image metadata stores under `data/images` (031-sequence-conditional-steps)
- C# 13 / .NET 9 (backend), TypeScript ES2020 / React 18 (frontend) + ASP.NET Core Minimal API, existing GameBot.Domain sequence/command services, existing image detection pipeline, existing execution-log services, existing action execution infrastructure (031-sequence-conditional-steps)
- File-backed JSON repositories under `data/commands/sequences` and image metadata under `data/images` (031-sequence-conditional-steps)
- C# 13 / .NET 9 (backend), TypeScript ES2020 / React 18 (frontend) + ASP.NET Core Minimal API, GameBot.Domain sequence services/repositories, existing condition evaluator stack, React/Vite web-ui authoring modules (032-per-step-conditions)
- File-backed JSON repositories under `data/commands/sequences` (no new datastore) (032-per-step-conditions)

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
- 032-per-step-conditions: Added C# 13 / .NET 9 (backend), TypeScript ES2020 / React 18 (frontend) + ASP.NET Core Minimal API, GameBot.Domain sequence services/repositories, existing condition evaluator stack, React/Vite web-ui authoring modules
- 031-sequence-conditional-steps: Added C# 13 / .NET 9 (backend), TypeScript ES2020 / React 18 (frontend) + ASP.NET Core Minimal API, existing GameBot.Domain sequence/command services, existing image detection pipeline, existing execution-log services, existing action execution infrastructure
- 031-sequence-conditional-steps: Added C# 13 / .NET 9 (backend), TypeScript ES2020 / React 18 (web UI) + ASP.NET Core Minimal API, existing GameBot.Domain sequence/command services, existing image detection pipeline, existing execution-log services, existing JSON repositories


<!-- MANUAL ADDITIONS START -->
<!-- MANUAL ADDITIONS END -->
