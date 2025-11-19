# Spec: Action/Command Refactor (001)

## Summary
Refactors the domain to:
- Rename Profile to Action
- Decouple Triggers from Actions (no implicit binding)
- Remove automated/background trigger evaluation entirely
- Introduce Command as an executable composite of Actions and/or Commands
- Allow optional Trigger association per Command (evaluate-and-execute)
- Provide force-execute to run a Command ignoring Trigger

Spec: specs/001-action-command-refactor/spec.md  
Checklist: specs/001-action-command-refactor/checklists/requirements.md

## Status Update (2025-11-19)
Implementation has started on branch `001-action-command-refactor` and is being merged into this PR:

- Added new domain models and repositories:
	- `Action` + `IActionRepository` (file-backed `actions/`)
	- `Command` + `ICommandRepository` (file-backed `commands/`)
- Wired DI and added CRUD endpoints:
	- `POST/GET /actions`, `GET /actions/{id}`
	- `POST/GET /commands`, `GET /commands/{id}`, `PATCH /commands/{id}`, `DELETE /commands/{id}`
- Implemented `CommandExecutor` with simple cycle detection and sequential execution:
	- Execution endpoints: `POST /commands/{id}/force-execute?sessionId=...` and `POST /commands/{id}/evaluate-and-execute?sessionId=...` (currently same behavior; trigger gating to follow)
- Removed automated background evaluation:
	- Unregistered `TriggerBackgroundWorker` and its options; kept metrics surface

All unit/integration/contract tests are currently green on this branch.

## Clarification Resolution
Decision: A — Breaking rename now. External API, contracts, and persistence naming migrate from "Profile" to "Action" as part of this feature (no deprecation window). Migration notes will include updated endpoints, payloads, and client guidance.

## Success Criteria
- CRUD for Action, Trigger, Command defined in spec
- Command composition validated as acyclic
- Evaluate-and-execute runs only when Trigger = Satisfied; force-execute bypasses Trigger
- No background trigger evaluation present post-change

## Next Steps
- Update API/contract to finalize breaking rename Profile → Action (endpoints and models) and adjust tests.
- Introduce standalone Trigger repository and wire evaluate-and-execute gating.
- Migrate any persisted `profiles/` data to `actions/`.
- Documentation and migration notes for clients.
