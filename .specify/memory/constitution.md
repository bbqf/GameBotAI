<!--
Sync Impact Report
Version change: 1.2.0 → 1.3.0
Modified principles: N/A
Added sections: Core Principle VI (Simplified Technical English); Definition of Done bullet for STE;
  Review bullet for STE
Removed sections: N/A
Templates requiring updates:
- .specify/templates/plan-template.md ✅ updated (STE gate in Constitution Check)
- CLAUDE.md ✅ updated (STE rule for agent-written text outside speckit, e.g. commits and PRs)
- .specify/templates/spec-template.md ✅ no change needed (principle applies to all text it produces)
- .specify/templates/tasks-template.md ✅ no change needed
- .specify/templates/spec-template.md ⚠ pending (Status line vocabulary could be documented; from 1.2.0)
Follow-up TODOs:
- Existing artifacts are not retrofitted; text converts to STE when it is changed
- TODO(RATIFICATION_DATE): Original adoption date unknown — project owner to provide
- Delete orphaned web-ui trigger code (TriggersPage.tsx, services/triggers.ts, TriggerPicker.tsx)
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
- An evaluation of test builds and runs results is necessary before each commit.
- The code quality warnings/errors within the test code can be disabled if needed.

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

### V. Living Documentation (NON-NEGOTIABLE)

Current-state documentation MUST track the code, and feature history MUST stay honestly labelled:
- `docs/architecture.md` is the single source of truth for current system behaviour (domain model,
  capability set, API surface, persistence layout). Any change that alters one of these MUST update
  `docs/architecture.md` in the same PR, and its "Last reviewed" date MUST be refreshed.
- Feature specs under `specs/` are immutable point-in-time history, NOT living documentation. Every
  `specs/NNN-*/spec.md` MUST carry an accurate `**Status**:` line (Implemented / Implemented
  (iterated by N) / Superseded by N / Meta / Draft / Abandoned), and `specs/STATUS.md` MUST be kept
  consistent with those lines.
- When a change supersedes or reworks an earlier feature, the earlier spec's `Status` MUST be
  updated to point at the superseding spec in the same PR, so no spec misrepresents current
  behaviour.
- Spec folder numbers MUST be unique; duplicates are reconciled by renumbering the later-created
  folder to the next free sequential slot, preserving the original branch name in the spec.

Rationale: Stale or mislabelled documentation actively misleads humans and AI agents — it is worse
than no documentation. Separating "what is true now" (architecture) from "why we got here"
(specs) keeps both trustworthy.

### VI. Simplified Technical English (NON-NEGOTIABLE)

All text that you write or change MUST obey ASD-STE100 Simplified Technical English (STE):
- Scope: specs, plans, tasks, checklists, research notes, data models, contracts, quickstarts,
  docs, changelog entries, commit messages, PR titles and descriptions, code comments, and
  user-facing text (error messages, log messages, UI text, and API descriptions).
- Write procedural sentences of 20 words or fewer. Write descriptive sentences of 25 words or
  fewer.
- Write one instruction in each sentence. Use the imperative form for instructions.
- Use the active voice. Use only the simple present, simple past, and simple future tenses.
- Use approved STE words, each with its approved meaning only. Use a technical name or a
  technical verb only when no approved word has that meaning.
- Use the same term for the same thing in all text. Do not use synonyms for variety.
- Do not use the "-ing" form of a verb, except in a technical name.
- Write paragraphs of 6 sentences or fewer. Put one topic in each paragraph.
- Do not omit words (such as "a", "the", or "is") to make text shorter.
- Exceptions: code, identifiers, commands, file paths, tool output, and quotations stay as
  they are. The requirement keywords MUST, SHOULD, and MAY are permitted.
- Existing text: you do not have to rewrite text that you do not change. When you change a
  sentence, write the new sentence in STE.
- Enforcement: reviewers and `/speckit-analyze` MUST report STE violations in new or changed
  text as constitution violations. You MUST correct them before the change is Done.

Rationale: STE removes ambiguity. Short and direct text is easier for humans and AI agents to
read, translate, and check. One term for one thing prevents incorrect interpretation.

## Quality Gates and Definition of Done

A change is Done only when all gates pass:
- Quality: lint/format/static analysis clean; security scan clean or approved with risk notes.
- Do not use underscores in method names, only CamelCase
- Tests: all required tests pass in CI; coverage baselines met or improved; flaky tests eliminated or quarantined with owner.
- Release blocker: any local or CI red build/test result is a hard stop; no task/phase may be marked complete and no implementation may proceed until failures are fixed or explicitly waived by a maintainer with documented rationale.
- UX: interfaces documented; help text/messages updated; any breaking change accompanied by version note and migration path.
- Performance: declared goals documented; perf note included for hot-path changes; no regression beyond agreed budgets.
- Documentation: public APIs and configuration updated; changelog entry added when user-visible.
- Living docs: `docs/architecture.md` updated (with refreshed "Last reviewed" date) for any change
  to the domain model, capabilities, API surface, or persistence; touched specs carry an accurate
  `Status` line and `specs/STATUS.md` is consistent.
- Language: all new or changed text in artifacts, commits, PRs, comments, and user-facing
  messages obeys Principle VI (STE).

## Development Workflow and Review Process

- Propose: open a plan/spec referencing goals, UX, tests, and performance budgets.
- Implement: small, reviewable PRs; keep commits logically grouped and well-described.
- Review: reviewers MUST check against this constitution (quality, tests, UX, performance, STE) and request evidence as needed.
- Gate: CI enforces quality/test gates; red build/test states block progression; maintainers may approve explicit, time-bound waivers with follow-up tasks.
- Traceability: link PRs to plans/specs and record decisions in the PR description.

## Governance

- Authority: This constitution supersedes informal practices. Conflicts resolve in favor of this document.
- Amendments: Any change requires PR with rationale, impact analysis, and updates to affected templates. Approval by at least one maintainer.
- Versioning: Semantic versioning for this document: MAJOR (principle removals/redefinitions), MINOR (new sections/principles), PATCH (clarifications).
- Compliance: Periodic audits review adherence. Non-compliance requires remediation tasks prioritized in the next cycle.

**Version**: 1.3.0 | **Ratified**: TODO(RATIFICATION_DATE): Original adoption date unknown — needs confirmation | **Last Amended**: 2026-09-24
