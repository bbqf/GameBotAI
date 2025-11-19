# Tasks: Action/Command Refactor (001)

- [x] Introduce domain models: Action, Command, CommandStep
- [x] Add repositories: IActionRepository, ICommandRepository
- [x] Implement cycle detection for Command composition
- [ ] Rename Profile → Action across domain and API
- [ ] Migrate persistence: profiles → actions (files/keys)
- [x] Remove TriggerBackgroundWorker and any auto-evaluation code
- [x] Implement Command executor (sequential, stop-on-failure)
- [x] CRUD endpoints: /actions, /commands, /triggers (decoupled)
- [x] Exec endpoints: POST /commands/{id}/evaluate-and-execute, POST /commands/{id}/force-execute
- [ ] Unit tests: cycles, execution ordering, trigger gating, force-execute
- [ ] Integration tests: endpoints and flows; ensure no background evaluation
- [ ] Update OpenAPI/contract tests
- [ ] Add migration script and docs
	- [ ] Script: migrate existing `data/profiles/*.json` → `data/actions/*.json` + extract embedded `triggers` to `data/triggers/*.json`
	- [ ] Dry run mode for safety (`-DryRun`) listing planned conversions
	- [ ] Preserve original profiles directory (do not delete) unless `-DeleteOriginal` passed
	- [ ] Post-migration verification checklist (counts match, spot-check one action & one trigger)
