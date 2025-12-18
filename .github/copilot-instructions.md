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
- C# / .NET 9 + Existing detection (OpenCV image-match), OCR (Tesseract), trigger evaluation services (001-sequence-logic)
- File-backed JSON under `data/commands/sequences` (001-sequence-logic)

- .NET 8 + ASP.NET Core Minimal API; SharpAdbClient (ADB integration); System.Drawing/Imaging or Windows Graphics Capture for snapshots (001-android-emulator-service)

## Project Structure

```text
src/
tests/
```

## Commands

# Add commands for .NET 8 C#

## Test Failure Analysis
After running `dotnet test -c Debug --logger trx;`, execute `scripts/analyze-test-results.ps1` to emit `TESTERROR:` lines for each failing test (name, outcome, message) and exit non-zero. Integrate into CI or local `verify` task to ensure rich failure detection.

## Code Style

Coding style: Follow standard .NET 8 C# conventions

## Recent Changes
- 001-sequence-logic: Added C# / .NET 9 + Existing detection (OpenCV image-match), OCR (Tesseract), trigger evaluation services
- 005-image-detect-command: Added Existing detection pipeline (OpenCvSharp via TemplateMatcher), Windows-only System.Drawing usage guarded with platform attributes; no new external packages.
- 001-image-storage: Added C# / .NET 8 (Service), .NET 9 (Domain alignment) + None new (disk I/O via `System.IO`)


<!-- MANUAL ADDITIONS START -->
### Agent Terminal Monitoring Policy

- Automatically monitor any terminal processes I start, especially background servers/tasks.
- For background commands started via the agent, I will retain the terminal ID and periodically fetch output to provide concise progress updates.
- Prefer VS Code tasks when available; for long-running tasks I will run them in background and summarize status using brief updates.
- I will avoid spam: updates on important milestones, errors, and readiness only.

### Terminal Reuse & No-Close Policy

- Always reuse existing terminals and task terminals; do not create new ones unnecessarily.
- Never ask to close terminals; keep them open and reuse them across commands and sessions.
- For foreground tasks (build/test), reuse the existing task terminals labeled `build`/`test` when present.
- For background servers/tasks, reuse the previously started terminal if still running; otherwise, restart and resume monitoring without prompting.
- Avoid duplicate background processes: verify status before starting another instance; if a restart is required, state it briefly and proceed.
<!-- MANUAL ADDITIONS END -->
