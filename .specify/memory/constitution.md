<!--
Sync Impact Report
Version change: 0.0.0 → 1.0.0
Modified principles: N/A (initial adoption)
Added sections: Core Principles (4), Quality Gates and Definition of Done, Development Workflow and Review Process, Governance
Removed sections: Template placeholder for Principle 5
Templates requiring updates:
- .specify/templates/plan-template.md ✅ updated (removed broken commands path; clarified Constitution Check)
- .specify/templates/spec-template.md ⚠ pending (consider adding UX and performance success criteria examples)
- .specify/templates/tasks-template.md ✅ updated (tests required per constitution)
- .specify/templates/commands/* ⚠ pending (directory not present in repo)
Follow-up TODOs:
- TODO(RATIFICATION_DATE): Original adoption date unknown — project owner to provide
-->

# GameBot Constitution

## Core Principles

### I. Code Quality Discipline (NON-NEGOTIABLE)

All contributions MUST meet strict quality bars:
- Linting and formatting MUST pass with zero errors using the project tools.
- Static analysis MUST report no new high/critical issues; warnings require justification in PR.
- Code MUST be modular, cohesive, and avoid dead code; functions under ~50 LOC unless justified.
- Public APIs require docstrings/comments with inputs, outputs, and error modes.
- Dependency hygiene: no unused deps; pin versions or ranges per policy; avoid unnecessary globals.
- Security checks (SAST/secret scan) MUST pass; secrets MUST NOT be committed.

Rationale: High-quality code reduces defects, accelerates onboarding, and lowers maintenance cost.

### II. Testing Standards

Testing is required for any executable logic:
- Unit tests MUST cover core logic; integration tests MUST cover externally visible contracts.
- Baseline coverage: ≥80% line and ≥70% branch coverage for touched areas; new modules to the same baseline.
- Tests MUST be deterministic, isolated, and fast (<1s avg per unit test where feasible).
- Bug fixes MUST include a failing test reproducing the issue before the fix.
- CI MUST run tests on every PR and block merges on failures or coverage regressions beyond allowed thresholds.

Rationale: A reliable test suite acts as a safety net enabling fast iteration.

### III. User Experience Consistency

Behavior and interfaces MUST be consistent and predictable:
- Follow the project’s UX conventions for CLI, APIs, and logs (naming, flags, messages, exit codes).
- Error messages MUST be actionable and include remediation hints; do not leak sensitive data.
- Inputs/outputs MUST be stable and versioned when breaking changes are required.
- Accessibility and internationalization considerations SHOULD be respected where applicable (e.g., color contrast, text).
- Provide help/usage for CLI and API schemas for programmatic usage.

Rationale: Consistency builds user trust and reduces support burden.

### IV. Performance Requirements

Define, measure, and hold performance budgets:
- Features MUST declare performance goals in the plan (e.g., p95 latency, memory cap, throughput target).
- PRs affecting hot paths MUST include a perf note and, when feasible, a micro/benchmark result.
- Avoid pathological patterns (N+1, excessive allocations); use profiling to validate.
- Performance tests or benchmarks SHOULD backstop critical paths; regressions >2% on agreed metrics block merges unless waived.

Rationale: Predictable performance protects user experience and infrastructure costs.

## Quality Gates and Definition of Done

A change is Done only when all gates pass:
- Quality: lint/format/static analysis clean; security scan clean or approved with risk notes.
- Tests: all required tests pass in CI; coverage baselines met or improved; flaky tests eliminated or quarantined with owner.
- UX: interfaces documented; help text/messages updated; any breaking change accompanied by version note and migration path.
- Performance: declared goals documented; perf note included for hot-path changes; no regression beyond agreed budgets.
- Documentation: public APIs and configuration updated; changelog entry added when user-visible.

## Development Workflow and Review Process

- Propose: open a plan/spec referencing goals, UX, tests, and performance budgets.
- Implement: small, reviewable PRs; keep commits logically grouped and well-described.
- Review: reviewers MUST check against this constitution (quality, tests, UX, performance) and request evidence as needed.
- Gate: CI enforces quality/test gates; maintainers may approve explicit, time-bound waivers with follow-up tasks.
- Traceability: link PRs to plans/specs and record decisions in the PR description.

## Governance

- Authority: This constitution supersedes informal practices. Conflicts resolve in favor of this document.
- Amendments: Any change requires PR with rationale, impact analysis, and updates to affected templates. Approval by at least one maintainer.
- Versioning: Semantic versioning for this document: MAJOR (principle removals/redefinitions), MINOR (new sections/principles), PATCH (clarifications).
- Compliance: Periodic audits review adherence. Non-compliance requires remediation tasks prioritized in the next cycle.

**Version**: 1.0.0 | **Ratified**: TODO(RATIFICATION_DATE): Original adoption date unknown — needs confirmation | **Last Amended**: 2025-11-05
