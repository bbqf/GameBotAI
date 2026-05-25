# Quickstart: Randomized Sequence Step Delays

## Prerequisites

- Windows development environment
- .NET SDK used by this repository
- Dependencies restored

## 1) Build and baseline verification

1. Run workspace task `build` (`dotnet build -c Debug`).
2. Run workspace task `test` (`dotnet test -c Debug`).
3. Confirm both are green before feature validation.

## 2) Authoring contract validation

1. Create a sequence payload without `interStepDelayRangeMs`.
2. Save sequence via `POST /api/sequences`.
3. Read back via `GET /api/sequences/{sequenceId}`.
4. Confirm payload remains valid and runtime uses default `100..300` ms inter-step delay.

## 3) Default delay behavior validation

1. Create a sequence with at least three executable steps.
2. Execute sequence repeatedly via `POST /api/sequences/{sequenceId}/execute`.
3. Verify inter-step delays are sampled per boundary and always within `100..300` ms inclusive.
4. Verify no trailing delay is added after the final executed step.

## 4) Custom per-sequence range validation

1. Update sequence with `interStepDelayRangeMs: { "min": 250, "max": 450 }`.
2. Execute the sequence repeatedly.
3. Verify all inter-step sampled delays are within `250..450` ms inclusive.
4. Verify another sequence without custom range still uses `100..300` ms.

## 5) Input validation checks

1. Submit negative `min` value and verify API returns validation error.
2. Submit `min > max` and verify API returns validation error.
3. Submit non-integer values and verify API returns validation error.
4. Submit `min == max` and verify deterministic fixed inter-step delay equals that value.

## 6) Execution path coverage checks

1. Validate linear step sequence path applies inter-step delay between executed steps.
2. Validate flow-graph path applies inter-step delay when transitioning between resolved next/true/false targets.
3. Validate early termination (failure/cancel/terminal) does not apply extra post-termination delay.

## 7) Suggested automated test coverage

1. Unit tests for range normalization/defaulting and inclusive sampling boundaries.
2. Unit tests for validation (`min >= 0`, integers only, `min <= max`).
3. Integration tests for end-to-end execution timing behavior in linear and flow modes.
4. Contract tests asserting request/response schema support for `interStepDelayRangeMs`.
