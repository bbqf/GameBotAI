# Tasks: Action/Command Refactor (001)

- [ ] Introduce domain models: Action, Command, CommandStep
- [ ] Add repositories: IActionRepository, ICommandRepository
- [ ] Implement cycle detection for Command composition
- [ ] Rename Profile → Action across domain and API
- [ ] Migrate persistence: profiles → actions (files/keys)
- [ ] Remove TriggerBackgroundWorker and any auto-evaluation code
- [ ] Implement Command executor (sequential, stop-on-failure)
- [ ] CRUD endpoints: /actions, /commands, /triggers (decoupled)
- [ ] Exec endpoints: POST /commands/{id}/evaluate-and-execute, POST /commands/{id}/force-execute
- [ ] Unit tests: cycles, execution ordering, trigger gating, force-execute
- [ ] Integration tests: endpoints and flows; ensure no background evaluation
- [ ] Update OpenAPI/contract tests
- [ ] Add migration script and docs
