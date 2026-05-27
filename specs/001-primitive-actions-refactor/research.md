# Phase 0 Research: Primitive Actions Data Model Refactor

## Decision 1: Cutover strategy is hard cutover with startup fail-fast
- Decision: Remove Action compatibility codepaths in the release and block service startup/readiness if any legacy Action reference remains in persisted data.
- Rationale: This matches clarified product intent, prevents silent partial behavior, and gives deterministic release safety.
- Alternatives considered:
  - Runtime rejection only: rejected because failures would occur late and inconsistently by access pattern.
  - Dual-read compatibility bridge: rejected because it prolongs model ambiguity and migration debt.

## Decision 2: Primitive actions use discriminated typed variants
- Decision: Model primitive actions as a shared base (discriminator + shared metadata) with explicit per-type payload variants.
- Rationale: Keeps validation strict, prevents invalid cross-type payload mixing, and improves API/test clarity.
- Alternatives considered:
  - Unstructured map payloads everywhere: rejected due to weak validation and drift risk.
  - Type key only with ad hoc fields: rejected due to inconsistent schemas across contexts.

## Decision 3: Persist primitive selections inline by value
- Decision: Commands/sequences/execution-related authored data store primitive selections inline (type + typed payload), not by new global primitive ID.
- Rationale: Eliminates cross-entity lookup complexity after Action removal and makes migration deterministic.
- Alternatives considered:
  - New global primitive repository with references: rejected because it recreates indirection similar to removed Action entity.
  - Hybrid inline/reference mode: rejected due to unnecessary complexity and ambiguous canonical form.

## Decision 4: API contract direction for Action removal
- Decision: Remove Action CRUD endpoints from authored flows and preserve only primitive-action-oriented contracts (catalog + inline payload in command/sequence models).
- Rationale: Ensures users cannot create new dependency on deprecated Action model and keeps external API aligned with domain model.
- Alternatives considered:
  - Keep Action endpoints hidden from UI: rejected because backend drift remains and can be consumed externally.
  - Immediate endpoint aliasing only: rejected because path aliases still expose deprecated semantics.

## Decision 5: Migration approach and validation diagnostics
- Decision: Provide deterministic pre-cutover migration plus startup validation report listing every remaining legacy reference (file and key path) that blocks readiness.
- Rationale: Enables auditable rollout and quick operator remediation.
- Alternatives considered:
  - Best-effort migration without detailed diagnostics: rejected because failures are harder to triage.
  - Auto-delete invalid records: rejected due to data loss risk.

## Decision 6: Performance and operational guardrails
- Decision: Enforce cutover validation budget (<= 5s for 10k records on reference hardware) and no >2% p95 execution regression for unchanged scenarios.
- Rationale: Constitution requires explicit measurable goals; these bounds keep startup and runtime behavior predictable.
- Alternatives considered:
  - No explicit budgets: rejected by constitution and weakens review gate quality.
  - Tighter budgets before implementation data: rejected as premature and likely unstable.
