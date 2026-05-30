# Research: Installer Semantic Version Upgrade Flow

## Decision 1: Version source-of-truth layering
- Decision: Use a checked-in version override file for `Major/Minor/Patch`, a dedicated checked-in release-line marker file for automatic minor transitions, and a checked-in CI build counter file for authoritative build increments.
- Rationale: Separates operator intent (overrides), release-line intent (marker), and monotonic CI sequencing (counter), reducing coupling and accidental increments.
- Alternatives considered:
  - Single combined state file: rejected due to higher merge conflict blast radius and harder auditability.
  - Branch-name derived minor progression: rejected because branch naming is non-authoritative and prone to accidental increments.

## Decision 2: Minor increment trigger
- Decision: Minor increments only when the dedicated release-line marker file is created or updated.
- Rationale: Explicit repository action provides deterministic, reviewable release-line transitions.
- Alternatives considered:
  - Auto-increment on branch creation: rejected because it is not stable across local/remote workflows.
  - Auto-increment on merge only: rejected because it conflates release intent with integration timing.

## Decision 3: Patch reset semantics
- Decision: Reset `Patch` to `0` on automatic minor transition; permit explicit checked-in override to supersede reset.
- Rationale: Matches semantic versioning expectations while preserving operator override control.
- Alternatives considered:
  - Preserve prior patch across minor increments: rejected as semantically incorrect for this workflow.

## Decision 4: Build counter authority and concurrency
- Decision: CI is the sole writer of the persisted build counter; local builds never persist updates.
- Rationale: Prevents developer machine writes and centralizes sequencing in reproducible CI.
- Alternatives considered:
  - Dual local+CI writes with merge conflict handling: rejected due to non-deterministic write races and operational complexity.
  - Local-only independent counters: rejected because artifacts become non-comparable.

## Decision 5: Local build build-number derivation
- Decision: Local build derives build as `persistedCiBuild + 1` for artifact generation only, without repository write-back.
- Rationale: Keeps local validation near next expected release while protecting authoritative state.
- Alternatives considered:
  - Reuse persisted CI build unchanged: rejected because local test artifacts could appear stale.
  - Timestamp-based local build: rejected because ordering semantics diverge from numeric sequence.

## Decision 6: Upgrade decision comparison
- Decision: Determine downgrade, upgrade, and same-build outcomes using full numeric lexicographic comparison of `Major.Minor.Patch.Build`.
- Rationale: The build component materially affects upgrade order and same-build detection.
- Alternatives considered:
  - Ignore build in comparisons: rejected because same-build behavior becomes ambiguous.

## Decision 7: Same-build unattended behavior and status code
- Decision: In unattended mode, same-build execution exits without changes using a dedicated same-build status code (`4090`).
- Rationale: Distinct non-generic outcome enables automation scripts to branch predictably without classifying as unknown failure.
- Alternatives considered:
  - Generic failure code: rejected due to poor diagnosability.
  - Automatic repair/reinstall: rejected to avoid unintended mutation in unattended runs.

## Decision 8: Upgrade property retention policy
- Decision: Preserve all previously persisted installer/runtime properties during higher-version upgrades unless explicitly overridden by the operator.
- Rationale: Aligns with “upgrade without changing properties” objective and minimizes regressions.
- Alternatives considered:
  - Retain only selected network properties: rejected due to partial-state surprise risk.

## Decision 9: Validation and failure behavior for version files
- Decision: Missing/malformed override, marker, or CI counter files trigger actionable validation failures with remediation hints before package/install proceeds.
- Rationale: Fail-fast behavior prevents inconsistent version artifacts and invalid upgrade decisions.
- Alternatives considered:
  - Implicit defaults on malformed inputs: rejected because it can silently skew release numbering.
