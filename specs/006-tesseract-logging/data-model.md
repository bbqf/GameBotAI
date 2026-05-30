# Data Model — Tesseract Logging & Coverage

## Entities

### TesseractInvocationLog
- **Purpose**: Represents one execution of the Tesseract CLI initiated by the OCR pipeline.
- **Fields**:
  - `InvocationId` (GUID, required): Correlates log entry with upstream request.
  - `ExePath` (string, required): Fully qualified executable path resolved at runtime.
  - `Arguments` (string array, required): Sanitized CLI arguments after redaction.
  - `WorkingDirectory` (string, optional): Directory supplied to `ProcessStartInfo`.
  - `EnvironmentOverrides` (dictionary<string,string>, optional): Sanitized env vars applied to the process.
  - `StartTimestampUtc` (DateTime, required) / `EndTimestampUtc` (DateTime, required): Used to compute duration.
  - `ExitCode` (int?, optional): Missing when the process fails to start.
  - `StdOut` (string, optional): Captured text up to 8 KB plus truncation marker.
  - `StdErr` (string, optional): Same limits as StdOut.
  - `WasTruncated` (bool, required): Indicates if either stream exceeded cap.
- **Validation**:
  - `InvocationId` must be unique within a process lifetime.
  - `Arguments` must be redacted by `SensitiveValueMasker` before serialization.
  - `StdOut`/`StdErr` must include `"…<truncated>"` suffix when `WasTruncated=true`.

### OcrCoverageReport
- **Purpose**: Stores coverage metrics for OCR integration namespaces per test run.
- **Fields**:
  - `GeneratedAtUtc` (DateTime, required).
  - `Namespace` (string, required) — e.g., `GameBot.Domain.Triggers.Evaluators`.
  - `LineCoveragePercent` (decimal, required) — 0–100 with one decimal precision.
  - `TargetPercent` (decimal, required) — defaults to 70.
  - `Passed` (bool, required) — `LineCoveragePercent >= TargetPercent`.
  - `UncoveredScenarios` (string array, optional) — friendly descriptions derived from instrumentation.
  - `ReportPath` (string, optional) — pointer to Cobertura XML artifact.
- **Validation**:
  - `Namespace` must match regex `^[A-Za-z0-9.]+$`.
  - `TargetPercent` must be ≥ 50 and ≤ 100.
  - `UncoveredScenarios` entries limited to 120 characters each.

### OcrTestScenario
- **Purpose**: Canonical list of deterministic test inputs required to hit coverage goals.
- **Fields**:
  - `ScenarioId` (string, required) — e.g., `success_basic`, `timeout_5s`.
  - `Description` (string, required).
  - `BitmapFixture` (string, required) — relative path to image asset or generator instructions.
  - `ExpectedOutcome` (enum: Success, Timeout, Failure, MalformedOutput).
  - `MocksUsed` (string array, optional) — names of helper classes toggled during run.
- **Relationships**:
  - Each `OcrTestScenario` maps to at least one test case in `tests/integration/TextOcrTesseractTests`.
  - Coverage reports reference zero or more scenario IDs in their `UncoveredScenarios` list.

## State Transitions
1. **Invoke Tesseract**: `TesseractProcessOcr` starts a process → populate `TesseractInvocationLog` fields → emit structured log (no persistence needed beyond logging sinks).
2. **Run Tests**: `dotnet test` executes `OcrTestScenario`s → coverlet produces Cobertura XML.
3. **Generate Report**: `tools/coverage/report.ps1` parses XML → creates `OcrCoverageReport` payload → prints summary and (optionally) archives JSON for history.

## Derived Views
- **Coverage Trend**: Sequence of `OcrCoverageReport` entries sorted by `GeneratedAtUtc` to visualize improvements.
- **Invocation Diagnostics**: Filter logs by `InvocationId` to aggregate stdout/stderr for a single OCR request.
