# Research

## Decision 1: Represent primitive tap as a native command step type
- Decision: Add a dedicated `PrimitiveTap` command step type instead of auto-creating or requiring an action entity.
- Rationale: Matches the feature goal of inline authoring, avoids hidden persistence side effects, and keeps action repository clean.
- Alternatives considered:
  - Synthetic hidden action records (rejected: creates hidden data and lifecycle complexity).
  - Reusing only action steps with forced templates (rejected: does not satisfy “no explicit action exists”).

## Decision 2: Enforce detection as a required field for primitive tap at save/validation time
- Decision: Command create/update validation rejects primitive tap steps missing detection configuration.
- Rationale: Prevents unsafe runtime behavior and makes authoring feedback immediate and deterministic.
- Alternatives considered:
  - Runtime skip when detection is missing (rejected: allows invalid persisted configuration).
  - Runtime hard failure (rejected: shifts an authoring error into execution-time disruption).

## Decision 3: Use highest-confidence match when multiple detections are available
- Decision: Primitive tap resolves to the highest-confidence detection candidate.
- Rationale: Produces deterministic and quality-biased behavior consistent with existing detection selection semantics.
- Alternatives considered:
  - First returned match (rejected: ordering can be unstable and less accurate).
  - Treat multi-match as failure (rejected: unnecessarily drops valid interactions).

## Decision 4: Out-of-bounds computed tap points are skipped, not clamped
- Decision: If detected point + offsets is outside valid screen bounds, skip tap and record `skipped/invalid-target`.
- Rationale: Avoids accidental taps on unintended coordinates and preserves safety-first semantics.
- Alternatives considered:
  - Clamp to nearest valid coordinate (rejected: may cause unintended taps).
  - Fail entire command (rejected: too disruptive for a single step failure mode).

## Decision 5: Expose primitive tap execution outcomes in command execution response payloads
- Decision: Extend execute endpoint responses to include per-step outcomes while retaining existing `accepted` compatibility field.
- Rationale: Satisfies requirement to distinguish detection success from skipped taps and keeps existing clients functional.
- Alternatives considered:
  - Logs only (rejected: insufficient for API consumers and automated assertions).
  - Separate telemetry endpoint (rejected: added complexity for immediate scope).

## Decision 6: Preserve command-level detection for existing action behavior while adding step-level primitive detection
- Decision: Keep existing command-level detection behavior untouched for action steps and introduce primitive-step-local detection config.
- Rationale: Minimizes regression risk and keeps migration path simple.
- Alternatives considered:
  - Move all detection to step-level immediately (rejected: broad migration scope and compatibility risk).
  - Keep only command-level detection and infer primitive behavior globally (rejected: ambiguous per-step control).
