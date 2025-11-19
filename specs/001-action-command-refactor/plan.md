# Implementation Plan: Action/Command Refactor (001)

## Goals Recap
- Complete legacy type renaming across domain, API, and persistence (breaking change).
- Decouple Triggers from Actions; no automated/background evaluation.
- Introduce `Command` as a composite of Actions and/or Commands (acyclic).
- Provide explicit operations:
  - Evaluate-and-execute (executes only if associated Trigger is Satisfied)
  - Force-execute (bypasses Trigger)

## Architecture Changes
- Domain:
   - Establish `Action` as atomic executable unit (legacy types removed).
  - Add `Command` aggregate with ordered `CommandStep` (ActionRef | CommandRef) and optional `TriggerId`.
  - Keep `Trigger` as standalone evaluable condition; remove background worker.
  - Add cycle detection for Command composition.
- Persistence:
   - Migrate file-based repos and JSON keys/paths from legacy folder → `actions`.
  - Add `commands` store; reuse `triggers` store but remove background flags.
  - Optional migration script to rename on-disk files/keys.
- API:
   - Actions endpoints:
      - `POST /actions`, `GET /actions`, `GET /actions/{id}`, `PUT /actions/{id}`, `DELETE /actions/{id}`
  - Triggers remain CRUD; drop any background/evaluation scheduling endpoints.
  - Commands endpoints:
    - `POST /commands`, `GET /commands`, `GET /commands/{id}`, `PUT /commands/{id}`, `DELETE /commands/{id}`
    - `POST /commands/{id}/evaluate-and-execute` → evaluates associated Trigger; executes if Satisfied
    - `POST /commands/{id}/force-execute` → bypass Trigger

## Work Breakdown
1. Repo Preparation [DONE]
   - Create new DTOs/models: `Action`, `Command`, `CommandStep`.
   - Introduce repositories: `IActionRepository`, `ICommandRepository`; update `ITriggerRepository` if needed.
   - Implement cycle detection utility for commands.

2. Legacy Type Removal & Action Finalization
   - Domain: remove obsolete namespaces, ensure only `Actions` remain.
   - Persistence: ensure folder `data/actions` canonical; remove any legacy folders.
   - API Contracts: confirm request/response models reflect only `Action`.
   - Tests: verify fixtures/data files updated.

3. Remove Background Evaluation [DONE]
   - Remove `TriggerBackgroundWorker` and related hosted services/config.
   - Delete related tests.
   - Ensure no timers/cron-like loops remain.

4. Command Feature [PARTIAL]
   - Domain: `Command` aggregate, step list, composition validation.
   - Persistence: JSON repo for commands.
   - Services: executor that runs a command sequentially; stop on first failure with result. [DONE]
   - API: CRUD + evaluate-and-execute + force-execute. [DONE]

5. Trigger Decoupling
   - Ensure triggers are not tied to actions; keep explicit association only on command (optional).
   - Provide an internal evaluation method used by evaluate-and-execute.

6. Migration & Data Compatibility
   - Script/one-time migration for existing legacy data folder to `actions`.
   - Update `data/config` references, if any.

7. Tests
   - Unit: domain validation (cycles), command execution ordering, trigger satisfaction gating, force-execute behavior.
   - Integration: endpoints for actions/commands/triggers; no background worker; evaluate-and-execute and force-execute flows.
   - Contract: OpenAPI updates.

8. Documentation & Release Notes
   - Update README/ENVIRONMENT.md as needed.
   - Migration guide for clients: endpoint/path/property renames.

## Acceptance Gates
- All legacy endpoints removed; Action endpoints available and tested.
- No background worker code present; all tests green.
- Command CRUD and execution endpoints function per spec.
- Cycle detection blocks recursive definitions.
- Migration script verified on sample data.
