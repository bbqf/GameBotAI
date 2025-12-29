# GameBot Development Guidelines

Auto-generated from all feature plans. Last updated: 2025-11-05

## Active Technologies
- C# / .NET 8 existing project baseline. Existing: ASP.NET Core Minimal API, ADB integration libs. New: NEEDS CLARIFICATION for image similarity and OCR library choice (001-profile-triggers)
- File-based JSON repositories (existing) extended to persist triggers alongside profiles (001-profile-triggers)
- C# 13 / .NET 9 + Tesseract CLI, System.Diagnostics.Process, Microsoft.Extensions.Logging, Serilog, xUnit + coverlet for coverage enforcement (001-tesseract-logging)
- Local filesystem temp directories for OCR I/O; log output routed to existing sinks (console/Application Insights). No new persistence. (001-tesseract-logging)
- C# 13 / .NET 9 + ASP.NET Core Minimal API, Microsoft.Extensions.Logging configuration pipeline, existing JSON config repository (001-runtime-logging-control)
- Reuse file-based JSON configuration persisted under `data/config` (no new store) (001-runtime-logging-control)
- C# / .NET 9 + GameBot.Domain repositories, GameBot.Emulator.Session, TriggerEvaluationService (no new external packages) (001-fix-trigger-evaluate)
- File-based JSON repositories under `data/` (001-fix-trigger-evaluate)
- C# / .NET 9 (align with existing services) + External Tesseract CLI (no new managed package) (001-ocr-confidence-refactor)
- None added; transient temp files only (001-ocr-confidence-refactor)
- C# / .NET 8 (Service), .NET 9 (Domain alignment) + None new (disk I/O via `System.IO`) (001-image-storage)
- Disk-backed under `data/images` (001-image-storage)
- Existing detection pipeline (OpenCvSharp via TemplateMatcher), Windows-only System.Drawing usage guarded with platform attributes; no new external packages. (005-image-detect-command)
- File-based JSON repositories under `data/` (commands, triggers, config). No new persistence stores; extend command schema to include `DetectionTarget` parameters. (005-image-detect-command)
- Frontend: TypeScript (ES2020), React 18, Vite 5 + React, Vite, @vitejs/plugin-react (001-web-ui-authoring)
- Backend: C# / .NET 9 + Existing detection (OpenCV image-match), OCR (Tesseract), trigger evaluation services (001-sequence-logic)
- File-backed JSON under `data/commands/sequences` (001-sequence-logic)
- TypeScript (ES2020), React 18 + React, React DOM, Vite, @vitejs/plugin-react (001-authoring-crud-ui)
- None client-side (in-memory state); persistence via backend API (001-authoring-crud-ui)
- TypeScript (ES2020) + React 18 (Vite 5) + React, Vite toolchain, existing GameBot Service API (action types/actions) (001-semantic-actions-ui)
- No new client persistence; uses backend for actions. Client state is in-memory form state. (001-semantic-actions-ui)
- TypeScript (ES2020), React 18, Vite 5; backend contracts in ASP.NET Core (.NET 9) + React, React Router, form state utilities already in web-ui (no new packages expected) (017-unify-authoring-ui)
- Backend file-backed JSON repositories (data/), no new stores (017-unify-authoring-ui)
- C# 13 / .NET 9 + ASP.NET Core Minimal API, Swashbuckle/Swagger tooling, existing GameBot.Domain + Emulator services (018-api-refactor)
- File-backed JSON repositories under `data/` (no new stores) (018-api-refactor)

- .NET 9 + ASP.NET Core Minimal API; SharpAdbClient (ADB integration); System.Drawing/Imaging or Windows Graphics Capture for snapshots (001-android-emulator-service)

## Project Structure

```text
src/
tests/
```

## Commands

## Development environment
- Development is done on Windows, using Visual Studio Code
- Don't use Linux/WSL commands for development, as some dependencies (ADB, Tesseract, System.Drawing) are Windows-specific
- Always use powershell commands and syntax in scripts and documentation

# Add commands for .NET 9 C#

## Test Failure Analysis
After running `dotnet test -c Debug --logger trx;`, execute `scripts/analyze-test-results.ps1` to emit `TESTERROR:` lines for each failing test (name, outcome, message) and exit non-zero. Integrate into CI or local `verify` task to ensure rich failure detection.

## Code Style

Coding style: Follow standard .NET 9 C# conventions

## Recent Changes
- 018-api-refactor: Added C# 13 / .NET 9 + ASP.NET Core Minimal API, Swashbuckle/Swagger tooling, existing GameBot.Domain + Emulator services
- 017-unify-authoring-ui: Added TypeScript (ES2020), React 18, Vite 5; backend contracts in ASP.NET Core (.NET 9) + React, React Router, form state utilities already in web-ui (no new packages expected)
- 001-semantic-actions-ui: Added TypeScript (ES2020) + React 18 (Vite 5) + React, Vite toolchain, existing GameBot Service API (action types/actions)


<!-- MANUAL ADDITIONS START -->
### Agent Terminal Monitoring Policy


### Terminal Reuse & No-Close Policy

## Terminal Command Guidelines

When running terminal commands in this project:
- Prefer explicit output over silent operations
- Use `--verbose` flags when available
- Break down operations into smaller steps if timeout occurs

Common commands and expected duration:
- `npm install` - 10-30 seconds
- `npm run dev` - Background process
- `npm run build` - 30-90 seconds
- `git status` - <1 second
- `dotnet build` - 60-120 seconds
- `dotnet test` - 60-180 seconds

### GitHub Client Usage
When interacting with GitHub repositories:
- use git CLI for low-level operations like commits, branches, and pushes
- use gh CLI for operations like cloning, creating branches, and managing pull requests
<!-- MANUAL ADDITIONS END -->
