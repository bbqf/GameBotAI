# Implementation Plan: Triggered Profile Execution

**Branch**: `001-profile-triggers` | **Date**: 2025-11-09 | **Spec**: specs/001-profile-triggers/spec.md
**Input**: Feature specification for triggers driving profile execution.

## Summary

Enable profiles to auto-start when any configured trigger condition is satisfied. Supported triggers: Delay (relative seconds), Schedule (absolute time), ImageMatch (reference image fuzzy match within region), TextMatch (OCR found/not-found within region). Provide validation, cooldown handling, test-evaluation endpoint, and status visibility while maintaining resolution independence (normalized regions) and performance responsiveness (≤2s detection).

## Technical Context

**Language/Version**: C# / .NET 8 (existing project baseline)  
**Primary Dependencies**: Existing: ASP.NET Core Minimal API, ADB integration libs. New: NEEDS CLARIFICATION (image similarity + OCR library choice)  
**Storage**: File-based JSON repositories (existing) extended to persist triggers alongside profiles  
**Testing**: xUnit + existing integration/contract suites; add new unit tests for trigger evaluation logic, integration tests for endpoints, contract tests for schema  
**Target Platform**: Windows host (current ADB requirement)  
**Project Type**: Backend automation service (single solution with Domain, Emulator, Service projects)  
**Performance Goals**: Trigger evaluation loop processes screen every ≤2s; image/text evaluation adds ≤500ms per cycle at default resolutions  
**Constraints**: Avoid blocking session ops; CPU utilization increase ≤15% during active trigger evaluation; memory overhead for cached images ≤50MB  
**Scale/Scope**: Tens of concurrent sessions each with up to ~10 triggers; design for future expansion (hundreds) via configurable evaluation interval  

UNKNOWNS / NEEDS CLARIFICATION:
1. OCR engine selection (e.g., Tesseract vs. Windows OCR APIs) → NEEDS CLARIFICATION
2. Image fuzzy match technique (perceptual hash vs. template matching vs. feature-based) → NEEDS CLARIFICATION
3. Persistence model: embed triggers within profile file vs. separate triggers file per profile → NEEDS CLARIFICATION

## Constitution Check (Pre-Design)

- Code Quality: Will isolate trigger evaluation logic in new domain service with ≤50 LOC methods; add XML comments for public APIs. Static analysis unchanged. PASS
- Testing: Plan includes unit tests per trigger type (happy + edge), integration tests for evaluation & firing, contract tests for new endpoints. Coverage target ≥80% for new code. PASS
- UX Consistency: Endpoints follow existing REST naming; error messages validated, normalized coordinates consistent with snapshot usage. PASS
- Performance: Budgets defined (≤2s detection, ≤500ms compute); will add optional perf test using large screen sample set. PASS

Gate Result: PASS (unknowns do not block initial research phase).

## Project Structure

### Documentation (this feature)

```text
specs/001-profile-triggers/
├── spec.md
├── plan.md
├── research.md
├── data-model.md
├── quickstart.md
├── contracts/
└── checklists/
```

### Source Code (affected areas)

```text
src/
├── GameBot.Domain/
│   ├── Profiles/
│   │   ├── AutomationProfile.cs          # Extend to reference triggers
│   │   ├── ProfileTrigger.cs             # New entity (types & params)
│   │   ├── TriggerEvaluationResult.cs    # New result struct
│   │   └── ITriggerEvaluator.cs          # Abstraction
│   ├── Services/
│   │   └── TriggerEvaluationService.cs   # Core evaluation loop (new)
├── GameBot.Emulator/
│   └── Session/
│       └── SessionManager.cs             # Invoke snapshot for evaluation
├── GameBot.Service/
│   ├── Endpoints/
│   │   ├── TriggersEndpoints.cs          # CRUD + test + status endpoints (new)
│   │   └── ProfilesEndpoints.cs          # Adjust to include triggers data
│   ├── Models/
│   │   ├── Triggers.cs                   # DTOs for trigger types
│   └── Hosted/
│       └── TriggerBackgroundWorker.cs   # Periodic evaluation (new)
tests/
├── unit/
│   └── TriggersTests.cs                  # Each trigger type logic
├── integration/
│   └── TriggerEvaluationTests.cs         # End-to-end firing conditions
└── contract/
    └── TriggersContractTests.cs          # OpenAPI contract checks
```

**Structure Decision**: Extend existing layered architecture; add dedicated evaluator service + hosted worker for periodic evaluation, maintain separation of domain logic (pure evaluation) and infrastructure (screen capture / OCR / image matching). No additional top-level projects needed.

## Complexity Tracking

| Violation | Why Needed | Simpler Alternative Rejected Because |
|-----------|------------|--------------------------------------|
| Background worker | Needed for periodic evaluation | Polling via external script adds deployment complexity |

## Phase 0: Research Plan

Resolved three NEEDS CLARIFICATION items in research.md (OCR engine: Tesseract, image matching: template NCC, persistence model: embedded triggers array). No remaining unknowns block design.

## Phase 1: Design Preview

Produce data-model.md, contracts (OpenAPI fragment), quickstart with configuration and examples, update agent context.

## Phase 2: (Out of scope for this command)

Implementation tasks and sequencing to be generated later (tasks.md).

## Post-Design Constitution Re-check (Placeholder)
Re-check after generating research, data-model, contracts, and quickstart:

- Code Quality: Documentation-only artifacts added; future code changes will include XML docs and adhere to static analysis. PASS
- Testing: Plan includes unit/integration/contract coverage; quickstart outlines flows. PASS
- UX Consistency: Endpoints named consistently; normalized region semantics documented. PASS
- Performance: Budgets reiterated; image/OCR strategies in research.md support goals. PASS
