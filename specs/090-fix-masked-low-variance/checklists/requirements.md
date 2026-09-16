# Specification Quality Checklist: Masked matching must not score featureless screen regions as perfect matches

**Purpose**: Validate specification completeness and quality before proceeding to planning
**Created**: 2026-09-16
**Feature**: [spec.md](../spec.md)

## Content Quality

- [x] No implementation details (languages, frameworks, APIs)
- [x] Focused on user value and business needs
- [x] Written for non-technical stakeholders
- [x] All mandatory sections completed

## Requirement Completeness

- [x] No [NEEDS CLARIFICATION] markers remain
- [x] Requirements are testable and unambiguous
- [x] Success criteria are measurable
- [x] Success criteria are technology-agnostic (no implementation details)
- [x] All acceptance scenarios are defined
- [x] Edge cases are identified
- [x] Scope is clearly bounded
- [x] Dependencies and assumptions identified

## Feature Readiness

- [x] All functional requirements have clear acceptance criteria
- [x] User scenarios cover primary flows
- [x] Feature meets measurable outcomes defined in Success Criteria
- [x] No implementation details leak into specification

## Validation Notes

Three issues were found on the first pass and fixed before this checklist was marked complete:

1. **Implementation detail leaked into the requirements.** The first draft named the internal
   computation (`varI = sum(m*I*I) - sum(m*I)^2/n`), the accumulation width, and the specific
   correlation calls, and prescribed 64-bit accumulation as the remedy. All of that is a design
   decision for the plan, not a requirement. FR-001..FR-006 were rewritten to state the observable
   obligation — no top-of-scale similarity on a featureless region, agreement with an independent
   reference value, a value inside the declared scale, a single documented no-information rule,
   continuity across its cutoff — leaving the mechanism to `/speckit-plan`.

2. **Two success criteria were not measurable.** "Scores low" and "stays fast" became SC-001
   (at most 0.60, within 0.01 of an independently computed value, against the observed 1.0000)
   and SC-005 (under 300ms for a 42x52 reference image over a 1080x1920 screen, no more than 2.5x
   the unmasked path, against the recorded 189ms / 99ms baseline).

3. **An acceptance criterion in the source issue does not apply to this repository.** The issue
   asks for `docs/api-bugs.md` row B-013 to be updated; that file and `docs/findings.md` are not
   in this repository — they belong to the separate PNS automation repository. Recorded as A-001
   and listed under Out of Scope, with this repository's own living-documentation obligation
   (`docs/architecture.md`, spec `Status` lines) named in its place. Likewise the three live
   captures cited as evidence are not available here, so verification is calibrated against the
   reported statistics instead (A-002).

No [NEEDS CLARIFICATION] markers were raised: every open question had a defensible default, and
each default is recorded in Assumptions rather than deferred.

**Re-validated after `/speckit-clarify` (2026-09-16)**: 16/16 → 16/16 items passing, no state
changes. The clarify pass found one genuinely missing category — observability, which the spec said
nothing about — and promoted two parked assumptions into requirements (FR-015 reporting of a
suppressed position, FR-016 the cutoff being fixed rather than configurable), adding FR-017 and
SC-009 for the diagnostics. A-003 and A-004 were collapsed into a single residual assumption so that
no assumption restates a requirement.
