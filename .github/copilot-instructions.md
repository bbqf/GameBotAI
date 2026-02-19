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
- TypeScript (ES2020), React 18, Vite 5 + react, react-dom, @vitejs/plugin-react, React Testing Library, Jest, Playwright (019-web-ui-nav)
- N/A (client-side state only) (019-web-ui-nav)
- C# (.NET 9) for backend; TypeScript (ES2020) + React 18 for frontend + ASP.NET Core Minimal API, SharpAdbClient/ADB integration, existing JSON repositories, React/Vite toolchain (020-connect-game-action)
- File-based JSON repositories (data/), client localStorage for session cache (020-connect-game-action)
- C# 13 / .NET 9 (backend), TypeScript ES2020 / React 18 (frontend) + ASP.NET Core Minimal API, existing file-backed image store under `data/images`, React 18 + Vite toolchain (021-images-authoring-ui)
- File-system image blobs and metadata; trigger references from JSON repositories (021-images-authoring-ui)
- C# / .NET 9 (frontend: React 18/Vite) + Existing GameBot emulator session & ADB capture, System.Drawing/OpenCvSharp already in repo; no new packages. (022-emulator-image-crop)
- File-backed images under `data/images` with metadata persisted alongside existing JSON repos if needed. (022-emulator-image-crop)
- Backend C# 13 / .NET 9; Frontend TypeScript (ES2020) / React 18 (Vite 5) + ASP.NET Core Minimal API, existing GameBot.Domain repos, SharpAdbClient/ADB + System.Drawing/OpenCvSharp (already present), file-based JSON stores under data/, React Testing Library + Playwright for web UI tests; no new external packages. (023-authoring-execution-ui)
- File-based JSON repos under data/ (commands, triggers, config); running session cache kept in service memory; no new persistence. (023-authoring-execution-ui)
- C# 13 / .NET 9, PowerShell 5.1+, WiX authoring (XML) + `WixToolset.Sdk` (v6), existing `GameBot.Service` publish output, existing web UI build output (025-standalone-windows-installer)
- Installer runtime logs under `%LocalAppData%\GameBot\Installer\logs`; installed app files in `%LocalAppData%\GameBot` (per-user only) (025-standalone-windows-installer)
- C# / .NET 9, PowerShell 5.1+, WiX authoring (XML) + `WixToolset.Sdk` (v6), ASP.NET Core minimal API host configuration pipeline, existing JSON config/file repositories, PowerShell build scripts (026-installer-semver-upgrade)
- Checked-in repository version files for override/counter/marker state; existing installer/runtime persisted properties under user scope (`%LocalAppData%`) (026-installer-semver-upgrade)

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
- 026-installer-semver-upgrade: Added C# / .NET 9, PowerShell 5.1+, WiX authoring (XML) + `WixToolset.Sdk` (v6), ASP.NET Core minimal API host configuration pipeline, existing JSON config/file repositories, PowerShell build scripts
- 025-standalone-windows-installer: Added C# 13 / .NET 9, PowerShell 5.1+, WiX authoring (XML) + `WixToolset.Sdk` (v4), existing `GameBot.Service` publish output, existing web UI build output
- 023-authoring-execution-ui: Added Backend C# 13 / .NET 9; Frontend TypeScript (ES2020) / React 18 (Vite 5) + ASP.NET Core Minimal API, existing GameBot.Domain repos, SharpAdbClient/ADB + System.Drawing/OpenCvSharp (already present), file-based JSON stores under data/, React Testing Library + Playwright for web UI tests; no new external packages.


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
