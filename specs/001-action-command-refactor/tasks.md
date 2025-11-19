# Tasks: Action/Command Refactor (001)

- [x] Introduce domain models: Action, Command, CommandStep
- [x] Add repositories: IActionRepository, ICommandRepository
- [x] Implement cycle detection for Command composition
- [x] Remove legacy type references; confirm only Action remains
- [x] Migrate persistence to `actions` (legacy folder cleaned)
- [x] Remove TriggerBackgroundWorker and any auto-evaluation code
- [x] Implement Command executor (sequential, stop-on-failure)
- [x] CRUD endpoints: /actions, /commands, /triggers (decoupled)
- [x] Exec endpoints: POST /commands/{id}/evaluate-and-execute, POST /commands/{id}/force-execute
- [ ] Unit tests: cycles, execution ordering, trigger gating, force-execute
- [ ] Integration tests: endpoints and flows; ensure no background evaluation
- [ ] Update OpenAPI/contract tests
- [ ] Add migration script and docs
	- [x] Migration completed eliminating legacy folder
	- [x] Post-migration verification checklist (counts match, spot-check one action & one trigger)
