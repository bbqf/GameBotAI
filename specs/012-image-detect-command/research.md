# Research: Commands Based on Detected Image

## Decisions

- Decision: Reuse existing detection pipeline defaults (thresholding, max-results)
  - Rationale: Keeps behavior consistent with current detections; reduces risk and complexity.
  - Alternatives considered: Override `maxResults` specifically for command resolution (e.g., 5 or 10) to bound work. Rejected for now to avoid divergence; can be revisited if perf data suggests need.

- Decision: Place `DetectionTarget` in `GameBot.Domain.Commands`
  - Rationale: It’s a command authoring concern; actions are consumers of resolved coordinates, not owners of detection configuration.
  - Alternatives considered: `GameBot.Domain.Actions` (would couple to specific actions) or `Triggers` (semantic mismatch). Both rejected due to ownership and cohesion.

- Decision: Introduce `DetectionCoordinateResolver` used by `CommandExecutionService`
  - Rationale: Centralizes resolution logic before action execution; easy to pass results to all coordinate-requiring actions without altering each action’s signature.
  - Alternatives considered: Implement per-action resolution or add a cross-cutting pipeline middleware. Rejected: per-action duplicates logic; middleware unclear in current architecture.

- Decision: Confidence default 0.8 configured per-command (`DetectionTarget.confidence`)
  - Rationale: Matches spec; empowers authors; respects existing detection scoring scales.
  - Alternatives considered: Global default in config only. Rejected due to need for per-command overrides.

- Decision: Base point center by default with optional offset (dx, dy)
  - Rationale: Center fits most tap scenarios; offsets enable adjacent control taps.
  - Alternatives considered: Multiple base point modes (top-left, bottom-right). Deferred; can be modeled via offsets without extra mode flags initially.

- Decision: Clamp to screen bounds using emulator session dimensions
  - Rationale: Prevent invalid inputs to ADB; logged at debug when clamping occurs.
  - Alternatives considered: Fail hard on OOB. Rejected to preserve ergonomics.

## Patterns and Best Practices

- Validation: Ensure `confidence` in [0,1]; numeric offsets; required `referenceImageId`. Fail fast with clear messages.
- Logging: Use existing detections category; include `count`, `threshold`, `resolvedX/Y`, `offsetX/Y` in structured logs.
- Determinism: Leverage existing deterministic ordering in `TemplateMatcher` to ensure stable selection when enforcing uniqueness.
- Performance: Measure detection-to-resolution time with Stopwatch; keep temp allocations minimal; no image re-decoding when avoidable.

## Open Questions Resolved

- Default `maxResults` usage: Do not override; use pipeline default.
- Ownership of `DetectionTarget`: Domain Commands.
- How actions receive coordinates: Resolver produces `ResolvedCoordinate` passed through `CommandExecutionService` to coordinate-requiring actions.
