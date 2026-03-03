# Quickstart: Visual Conditional Sequence Logic

## Prerequisites

- Windows development environment
- .NET SDK matching repository requirements
- Node.js environment for web UI
- Existing auth token configuration for API testing

## 1) Build and baseline tests

1. Run workspace task `build` (`dotnet build -c Debug`).
2. Run workspace task `test` (`dotnet test -c Debug`).
3. Record baseline pass results in PR notes.

## 2) Run service and authoring UI

1. Start backend with task `run-service`.
2. Start frontend with task `run-web-ui`.
3. Open the Sequences authoring screen and verify conditional flow editor is available.

## 3) Authoring validation flow

1. Create a sequence graph with:
   - One command step
   - One condition step using command-outcome operand
   - Distinct true/false branches
2. Save and reload; verify graph structure and condition expression persist unchanged.
3. Add nested condition logic using AND/OR/NOT and verify visual branch labels remain clear.

## 4) Image-detection condition validation

1. Configure an image-detection operand with threshold.
2. Execute the sequence with a matching screen state; verify true branch is selected when at least one match meets threshold.
3. Execute with non-matching state; verify false branch is selected.

## 5) Failure and cycle policy validation

1. Force condition evaluation failure (missing image target or evaluator error).
2. Verify condition step status is failed and sequence stops immediately.
3. Author a cycle without iteration limit; verify save/activation is rejected.
4. Author cycle with explicit iteration limit; verify execution marks current step failed and stops when the limit is reached.

## 6) Logging and observability validation

1. Execute a conditional sequence and inspect step logs.
2. Confirm each log entry includes:
   - Immutable sequence ID + step ID
   - Readable sequence label + step label
   - Deep-link metadata to authoring step
3. Enable debug-level logging and confirm each condition step emits:
   - Operand evaluation outcomes
   - Operator application trace
   - Final boolean and selected branch

## 7) Performance validation

- Compare sequence execution latency for equivalent linear vs conditional flows.
- Verify median overhead from condition evaluation + enriched logging stays within 10%.
- Verify p95 execute response for a 50-step sequence remains below 250ms in local validation runs.

## 8) Regression checks

- Verify existing non-conditional sequences execute unchanged.
- Verify existing execution log views can render enriched step metadata without regression.
- Verify contract and integration tests cover success, failure, and bounded-cycle paths.
