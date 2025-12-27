# Research — Tesseract Logging & Coverage

## Structured CLI Logging Pattern
- **Decision**: Wrap `Process` execution in a reusable `TesseractInvocationLogger` that collects command path, sanitized arguments, working directory, correlation ID, start/end timestamps, exit code, stdout, stderr, and duration; emit via `ILoggerMessage.Define` at Debug level with strongly typed parameters.
- **Rationale**: `LoggerMessage` reduces allocations and keeps log templates consistent with existing structured logging rules. Capturing start/end timestamps plus correlation IDs allows multi-line payloads to be searched easily and correlated with higher-level session logs.
- **Alternatives Considered**:
  - Logging raw strings after every `Process` property read — rejected because it increases noise and risks leaking secrets.
  - Using Serilog enrichers exclusively — rejected since the domain library primarily uses `ILogger<T>` and needs to remain provider-agnostic.

## Stdout/Stderr Capture & Truncation
- **Decision**: Capture stdout/stderr asynchronously into bounded buffers (max 8 KB per stream). When payload exceeds the limit, stop reading additional data, append a `"…<truncated>"` marker, and set a `truncated=true` flag in the structured log entry.
- **Rationale**: 8 KB balances the need to diagnose OCR issues (usually short text) with log storage limits; asynchronous reading prevents deadlocks when Tesseract emits large error traces.
- **Alternatives Considered**:
  - Unlimited capture — rejected due to risk of flooding logs and impacting memory.
  - Extremely small caps (1 KB) — rejected because they would cut off meaningful diagnostic data such as CLI usage hints from Tesseract.

## Sensitive Argument Redaction
- **Decision**: Reuse the existing `SensitiveValueMasker` (from logging middleware) to scrub arguments/environment variables matching `KEY`, `TOKEN`, or `SECRET` patterns before logging, and add unit tests covering representative inputs.
- **Rationale**: Keeps policy consistent with other areas (auth logging). Centralizing the logic avoids duplicate regex maintenance.
- **Alternatives Considered**: Implementing ad-hoc string replacements locally — rejected because it risks missing future secret patterns.

## Coverage Enforcement Workflow
- **Decision**: Run `dotnet test tests/integration/GameBot.IntegrationTests.csproj /p:CollectCoverage=true /p:CoverletOutputFormat=cobertura /p:Include="[GameBot.Domain]GameBot.Domain.Triggers.Evaluators.Tesseract*"` via a new script (`tools/coverage/report.ps1`) that also parses the Cobertura XML and prints a human-readable summary. CI will fail if coverage <70%.
- **Rationale**: Coverlet already ships with the repo; filtering to the namespace keeps runtime low. Generating a friendly summary meets stakeholder needs without introducing new services.
- **Alternatives Considered**:
  - Adding a third-party SaaS coverage dashboard — rejected due to cost and onboarding overhead.
  - Enforcing 80% coverage immediately — rejected to keep the goal aligned with the spec (70%) while the tests mature.
