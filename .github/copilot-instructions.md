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
- 001-fix-trigger-evaluate: Added C# / .NET 9 + GameBot.Domain repositories, GameBot.Emulator.Session, TriggerEvaluationService (no new external packages)
- 001-runtime-logging-control: Added C# 13 / .NET 9 + ASP.NET Core Minimal API, Microsoft.Extensions.Logging configuration pipeline, existing JSON config repository
- 001-tesseract-logging: Added C# 13 / .NET 9 + Tesseract CLI, System.Diagnostics.Process, Microsoft.Extensions.Logging, Serilog, xUnit + coverlet for coverage enforcement


<!-- MANUAL ADDITIONS START -->
<!-- MANUAL ADDITIONS END -->
