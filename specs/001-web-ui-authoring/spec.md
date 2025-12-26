# Feature: Web UI — Authoring (MVP)

## Overview

Create a browser-first Web UI for GameBot with mobile-friendly responsiveness. The first phase delivers an Authoring UI MVP for creating and managing sequences, actions, commands, triggers, and images. Subsequent phases will add Configuration, Execution, and Monitoring UIs.

## Clarifications

### Session 2025-12-26
- Q: How should the Web UI persist the auth token? → A: Memory-only by default; optional "remember token" persists to localStorage.

## Scope

- In-scope (MVP Authoring):
  - Sequence authoring: create, read, update, delete (CRUD)
  - Steps authoring: order, delay, gate
  - Blocks authoring: repeatCount, repeatUntil, while, ifElse (basic fields only)
  - Trigger and image reference browse-select (read-only catalog)
  - Validation feedback surfaced from service responses
  - Minimal navigation + responsive layout
- Out-of-scope (future phases):
  - Live device control, execution orchestration
  - Advanced image detection workflows and OCR tuning
  - Real-time monitoring dashboards

## Actors

- Author: creates and edits sequences and related entities
- Reviewer: views sequences and checks validation feedback

## Assumptions

- Default frontend stack: React + Vite for rapid MVP, accessibility, and broad browser support; responsive layout via CSS flex/grid.
- Authentication: Bearer token reused from service (same origin or proxied), with minimal UI prompt; token stored in memory by default with optional user opt-in to persist in localStorage ("remember token").
- API base URL: configurable; defaults to same-origin paths exposed by GameBot service.

## [NEEDS CLARIFICATION: Frontend framework]
- Choose between React (+ Vite) vs Blazor WASM vs Next.js. Default assumption above is React + Vite for MVP speed.

## Auth UX — Resolved
- Token persistence model: memory-only by default; optional "remember token" toggle to persist in localStorage.

## [NEEDS CLARIFICATION: Mobile support level]
- Minimum target: small screens (375px width). Any specific device classes or PWA requirements?

## User Scenarios & Testing

1. Create a new sequence
   - Author opens Authoring UI, inputs name, adds steps and optional blocks; saves
   - System returns created sequence with id; UI routes to detail view
   - Test: POST /api/sequences returns 201 with id; UI shows success state

2. Edit an existing sequence
   - Author selects a sequence from list; updates steps/blocks; saves
   - System validates and persists changes
   - Test: PUT/PATCH (or POST update) returns 200; UI shows updated values

3. Validation feedback on invalid blocks
   - Author adds invalid cadence or elseSteps in non-ifElse; save
   - System returns 400 with errors[]; UI highlights fields and messages
   - Test: Simulate invalid payload; ensure errors render and fields indicate problems

4. Browse triggers/images to select targetId
   - Author opens a picker; selects existing trigger or image reference
   - System fetches lists; selection populates fields
   - Test: GET endpoints return paged lists; picker filters and selection sets targetId

5. Mobile viewport
   - Author uses phone; layout collapses; forms remain usable
   - Test: Viewport at 375px shows single-column; primary actions are accessible

## Functional Requirements

FR-1: Create sequence
- UI captures name and basic metadata; submits to /api/sequences; shows 201 result

FR-2: List sequences
- UI fetches existing sequences; displays name, id, updatedAt; supports basic search

FR-3: Update sequence
- UI edits steps and blocks; submits; shows success or validation errors

FR-4: Delete sequence
- UI sends delete; confirms irreversible action; updates list

FR-5: Steps authoring
- Add/remove steps; set order, delayMs, delayRangeMs, gate (targetId, condition)

FR-6: Blocks authoring (basic)
- Add repeatCount (maxIterations, cadenceMs), repeatUntil/while (condition, timeoutMs or maxIterations, cadenceMs), ifElse (condition, then/else steps)

FR-7: Validation display
- On 400 { errors[] }, UI surfaces per-field validation messages and top-level summary

FR-8: Trigger/image selection
- UI fetches and filters items; sets `targetId` fields on conditions/gates

FR-9: Auth token input
- UI prompts for bearer token once; attaches to requests; memory-only by default with an optional "remember token" toggle to persist in localStorage.

FR-10: Responsive layout
- UI adapts to small screens (≥375px); targets 12pt minimum tap targets

## Success Criteria

SC-1: Author can create, edit, and delete sequences without inspector/console use
SC-2: 95% of create/edit operations complete under 1.5s in typical local environment
SC-3: Validation errors render within 300ms and are clearly associated to fields
SC-4: UI is usable on 375px-wide viewport with no horizontal scroll for core paths
SC-5: Accessibility: form fields have labels; color contrast meets WCAG AA

## Key Entities

- Sequence: id, name, steps[], blocks[]
- Step: order, commandId, delayMs|delayRangeMs, gate
- Block: type, plus fields per FR-6
- Condition: source, targetId, mode, confidenceThreshold, region, language

## Dependencies

- GameBot service API endpoints for sequences, triggers, images
- Auth token configuration

## Risks

- Divergent validation rules between service branches; mitigate by dynamic schema loading or contract tests
- UX complexity of nested blocks; mitigate with progressive disclosure

## Implementation Notes (Non-binding)

- Keep implementation details out of requirements; initial MVP will likely use React + Vite + TypeScript with fetch-based client.

## Readiness

SUCCESS — spec ready for planning.
