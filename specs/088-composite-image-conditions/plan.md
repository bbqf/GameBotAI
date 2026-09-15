# Implementation Plan: Composite Image Conditions

**Branch**: `088-composite-image-conditions` | **Date**: 2026-09-15 | **Spec**: [spec.md](./spec.md)
**Input**: Feature specification from `/specs/088-composite-image-conditions/spec.md`
**Issue**: [#191](https://github.com/bbqf/GameBotAI/issues/191) (B-011)

## Summary

Per-step guards today are a single leaf test: `SequenceStepCondition` is an abstract class with exactly two concrete forms, `ImageVisibleStepCondition` and `CommandOutcomeStepCondition`. This feature adds a third form — a **composite** that owns an ordered list of child conditions and a combining rule (`all`, `any`, `none`) — and threads it through the five places a condition can appear, the validator, the persistence guard, the runtime evaluator and the published schema.

The technical approach is deliberately narrow:

- **One new domain type** (`CompositeStepCondition`) and **one new contract type** (`CompositeConditionContract`), each following the existing `[JsonPolymorphic]` + `[JsonIgnore] override Type` pattern exactly.
- **One new evaluator class** (`SequenceStepConditionEvaluator`) holding the recursion, rather than growing `SequenceRunner`'s already-2340-line body. Both existing evaluation sites delegate to it; the leaf arms move in unchanged.
- **One new validator walk** (`CompositeConditionValidator`) producing the depth / width / emptiness / child errors, called from the existing `SequenceStepValidationService` so rejections land on the path that already returns 400.
- Every other change is an added `switch` arm or `is` branch at a site that already enumerates the two leaf kinds.

Backward compatibility is structural: nothing about the existing two leaf types changes, so a stored sequence deserializes, evaluates and re-serializes byte-identically.

## Technical Context

**Language/Version**: C# / .NET 9.0 (`net9.0`, `LangVersion preview`)
**Primary Dependencies**: ASP.NET Core minimal APIs, `System.Text.Json` polymorphic serialization, Swashbuckle (OpenAPI), OpenCvSharp4 (image matching — untouched here)
**Storage**: JSON sequence documents on disk via `FileSequenceRepository`
**Testing**: xUnit 2.7.1 + FluentAssertions 6.12, split across `tests/unit`, `tests/contract` (WebApplicationFactory), `tests/integration`
**Target Platform**: Windows service host driving an Android emulator over ADB
**Project Type**: Web service (REST API) plus a React `web-ui`; this feature is API-side only
**Performance Goals**: A composite guard performs no more screen observations than the number of children needed to settle it, and a two-signal guard costs **zero** extra screen captures versus today's one-signal guard (FR-017 shares one observation). Validation of a worst-case payload (depth 4 × 16 children) stays well under the existing save-path budget.
**Constraints**: `TreatWarningsAsErrors=true` and `AnalysisLevel=latest-all` — every new member needs XML docs and must satisfy CA rules. Method names must be CamelCase with no underscores (constitution). Build-time taint analyzers degrade badly on very large methods, so new logic goes in new small classes rather than into `SequenceRunner`.
**Scale/Scope**: ~9 source files touched, 2 new source files, 3 new test files plus additions to 5 existing ones. Depth ≤ 4, ≤ 16 children per composite.

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

*NON-NEGOTIABLE*: If `build` or required `test` runs are failing (local or CI), implementation progression is blocked until failures are fixed or a documented maintainer waiver exists.

| Principle | Gate | Assessment |
|-----------|------|------------|
| I. Code Quality Discipline | Zero lint/analyzer errors; modular; functions under ~50 LOC; public APIs documented | **PASS by design.** The recursion lives in two new focused classes instead of extending `SequenceRunner` (2340 LOC) or `SequencesEndpoints` (1327 LOC). Every new public member gets an XML doc comment, required by `TreatWarningsAsErrors` anyway. |
| II. Testing Standards | Unit + integration coverage; deterministic; ≥80% line / ≥70% branch on touched areas | **PASS by design.** Truth tables are pure unit tests over the new evaluator; the 400-not-500 contract test and the motivating end-to-end integration test are both mandated by the spec (FR-010, SC-002). No new nondeterminism: composites read the same screen observation the single-image path already uses. |
| III. UX Consistency | Actionable errors; stable versioned I/O; API schemas published | **PASS by design.** Error strings follow the existing `Step '<label>' ...` shape and name the limit and the offending path. FR-014 puts the new kinds in the OpenAPI document through the existing `ConditionalFlowSchemaDocumentFilter`. The change is purely additive, so no version bump or migration is needed. |
| IV. Performance | Declared budget; no pathological patterns | **PASS by design.** Budget declared above. Short-circuiting (FR-011) plus the shared screen observation (FR-017) mean a composite is bounded by the single-condition cost times the children actually evaluated, with no repeated capture. |
| V. Living Documentation | `docs/architecture.md` updated with refreshed review date; spec `Status` lines accurate; `specs/STATUS.md` consistent | **Gate carried into tasks.** This change alters the API surface and the domain model, so `docs/architecture.md` MUST be updated in the same PR, and this spec plus `specs/STATUS.md` must carry accurate Status lines. Tracked as explicit tasks, not left to the end. |

**Result: PASS.** No violations, so Complexity Tracking is omitted.

### Post-Design Re-Check

Re-evaluated after Phase 1 artifacts were written: still **PASS**. The design added no new project, no new dependency, no new persistence format and no new endpoint. The one judgement call — leaving the pre-existing gaps in image-reference validation coverage exactly where they are rather than widening them (see [research.md](./research.md), D-006) — is a *narrowing* in service of Principle III's stability guarantee, and is recorded rather than silently taken.

## Project Structure

### Documentation (this feature)

```text
specs/088-composite-image-conditions/
├── plan.md              # This file
├── spec.md              # Feature specification
├── research.md          # Phase 0 output — design decisions and rejected alternatives
├── data-model.md        # Phase 1 output — the condition type hierarchy
├── quickstart.md        # Phase 1 output — how to author a composite guard
├── contracts/
│   └── composite-condition.md   # JSON contract, discriminators, validation errors
├── checklists/
│   └── requirements.md  # Spec quality checklist
└── tasks.md             # Phase 2 output (/speckit-tasks — NOT created here)
```

### Source Code (repository root)

```text
src/GameBot.Domain/
├── Commands/
│   ├── SequenceStepCondition.cs          # MODIFY — add CompositeStepCondition + its JsonDerivedType rows
│   └── FileSequenceRepository.cs         # MODIFY — persistence-guard arm for composites (recursive)
└── Services/
    ├── SequenceStepConditionEvaluator.cs # NEW — the recursive evaluator, shared by both call sites
    ├── CompositeConditionValidator.cs    # NEW — depth / width / emptiness / child-shape errors
    ├── SequenceRunner.cs                 # MODIFY — delegate both condition sites; describe composites
    └── SequenceStepValidationService.cs  # MODIFY — call the composite validator per condition position

src/GameBot.Service/
├── Models/
│   └── SequenceStepContracts.cs          # MODIFY — CompositeConditionContract + JsonDerivedType rows
├── Endpoints/
│   └── SequencesEndpoints.cs             # MODIFY — map both directions; walk composites for image refs
└── Swagger/
    └── ConditionalFlowSchemaDocumentFilter.cs  # MODIFY — register + alias the new schema

tests/
├── unit/Sequences/
│   ├── CompositeConditionEvaluatorTests.cs   # NEW — truth tables, short-circuit, negation
│   ├── CompositeConditionValidationTests.cs  # NEW — depth, width, empty, bad child
│   └── PerStepConditionRunnerTests.cs        # MODIFY — composite at a step guard
├── contract/Sequences/
│   ├── CompositeConditionContractTests.cs    # NEW — 400-not-500 for every malformed shape
│   └── SequencePerStepConditionsOpenApiTests.cs  # MODIFY — schema assertions
└── integration/Sequences/
    ├── PerStepConditionExecutionPermutationIntegrationTests.cs  # MODIFY — the B-011 scenario
    └── ConditionalEditRoundTripIntegrationTests.cs              # MODIFY — nested round-trip

docs/architecture.md                       # MODIFY — condition model + API surface, refreshed date
specs/STATUS.md                            # MODIFY — add this feature's row
```

**Structure Decision**: The repository is a single .NET solution with a `GameBot.Domain` / `GameBot.Service` split and three test projects (`unit`, `contract`, `integration`). This feature keeps that split intact: the condition model, its validation rules and its evaluation semantics are **domain** concerns; the JSON contract, the mapping and the OpenAPI schema are **service** concerns. The two new files go in `GameBot.Domain/Services` because both the runner and the validator are domain-side, and the service layer reaches them only through existing seams.

## Key Design Decisions

Full reasoning and rejected alternatives are in [research.md](./research.md). In brief:

1. **A new derived type, not a new field on the base.** A `Children` list hung off `SequenceStepCondition` would make every existing leaf nominally composite and force every consumer to check for it. A third derived type keeps the two leaves untouched (FR-015).
2. **Do not reuse `ConditionExpression`.** The repo already has an `and`/`or`/`not` tree, but it serves a different step type with a different serialization and different validation rules (its `and`/`or` demand ≥2 children). Merging the models would change behaviour for existing flow steps — explicitly out of scope per spec A-008.
3. **One evaluator class, two call sites.** `SequenceRunner` evaluates conditions in two places (`ExecuteStepAsync` for step guards, `EvaluateLoopConditionAsync` for loop guards). Both get the composite capability by delegating to the new evaluator, so the semantics cannot drift between them.
4. **Validate before persist.** `FileSequenceRepository` throws `InvalidOperationException` on a malformed condition, which surfaces as a 500. `SequenceStepValidationService` runs first and returns 400. Composite rules therefore go in the validation service; the repository gets a matching recursive guard purely as a last-resort backstop, not as the primary gate (FR-010).
5. **Six sites enumerate the leaf kinds.** Mapping to DTO, mapping from contract, validation, persistence guard, runtime evaluation, and break-condition description. All six are updated; the task list names each one so none is missed.

## Phase 0: Research

Complete — see [research.md](./research.md). No `NEEDS CLARIFICATION` markers remained after the clarification session; Phase 0 instead records the design decisions and the alternatives weighed against them.

## Phase 1: Design & Contracts

Complete. Artifacts:

- [data-model.md](./data-model.md) — the condition hierarchy, the composite's fields, validation rules and the five positions a condition occupies.
- [contracts/composite-condition.md](./contracts/composite-condition.md) — JSON shape, discriminator values, worked examples including the B-011 guard, and the exact error messages for each rejection.
- [quickstart.md](./quickstart.md) — how an automation author writes and verifies a composite guard against a live device.

Agent context (`CLAUDE.md`) is updated to point at this plan.
