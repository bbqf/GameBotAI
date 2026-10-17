# Research: Remove an installed gamepack record

## D-001: Where the logic lives

- Decision: Add `GamepackService.RemoveAsync(string name, CancellationToken ct)`.
- Rationale: The service owns the gate (`_gate`) that install, rollback, and restore use. FR-008 needs the same gate.
- Alternatives considered: Put the logic in the endpoint handler. Rejected: the handler cannot take the private gate, and the Domain test project could not test it.

## D-002: Write order and all-or-nothing change

- Decision: Under the gate, (1) load the registry and find the record by `GamepackNameRules.Key(name)`; (2) change the registry in one `IGamepackRegistryStore.Update` call that removes the record and every owner entry whose `Gamepack` equals the record name; (3) after that write works, call `DeletePrevious(name)` when the record had a previous version.
- Rationale: `Update` writes the file with an atomic replace, so the registry is old or new, never half. If the registry write fails, nothing changed (FR-007). The previous file is deleted last, so a failed registry write never loses the kept copy. If `DeletePrevious` fails after the registry write, the record is already gone. The service logs a warning and still reports success: an orphan file is harmless, because a rollback reads the file only when the record has a `Previous` member, and a later install with `Capture` overwrites it.
- Alternatives considered: Delete the file first. Rejected: a failed registry write then leaves a record that claims a previous version with no file. Two-phase restore of the file. Rejected: more code for a harmless orphan.

## D-003: Which owner entries to remove

- Decision: Remove each owner entry whose `Gamepack` equals the record name, compared with `StringComparison.OrdinalIgnoreCase`. Do not read the sequence, command, or image stores.
- Rationale: FR-006 and FR-013 need the removal to work when the items are gone, and to remove entries whose item no longer exists. The owner entry holds the gamepack name, so no item lookup is needed.
- Alternatives considered: Use `record.Objects` to build the keys. Rejected: it can miss an owner entry that is out of date, and FR-013 says "every owner entry of the gamepack".

## D-004: Name check and not-found result

- Decision: An invalid name (`GamepackNameRules.IsValid` false) and an unknown name both give `NotFound`, as `RollbackAsync` does.
- Rationale: Same behaviour and same error model as the rollback route (spec edge case: "The name matches as for the rollback route"). The match uses `GamepackNameRules.Key`, so case does not matter.
- Alternatives considered: `400` for an invalid name. Rejected: the rollback route gives `404`, and one rule for both routes is simpler.

## D-005: Route, status codes, and error model

- Decision: `DELETE /api/gamepacks/{name}`. `204` on success. `404` with `result` `not_found` through the existing `ProblemResult`. `500` through the existing `WriteFailed("failed", detail)`.
- Rationale: Spec clarification of 2026-10-17. The same family as `DELETE keys/{fingerprint}`.
- Alternatives considered: `200` with a body. Rejected by the clarification.
- Route conflict check: `{name}/rollback` is a POST route with two segments, and `catalog`, `managed`, and `signature` routes use other methods or paths. A `DELETE {name}` route does not clash with them. The implement step MUST confirm this with a test that sends `DELETE` to the fixed paths that exist (for example `keys/{fingerprint}`) and sees no change.

## D-006: Take-over backups and content items

- Decision: Do not touch the take-over backup files and do not touch any sequence, command, or image.
- Rationale: Spec clarification and FR-013. A restore then gives `409 owner_changed` by the existing rule in `GamepackApplier.RestoreTakeOverAsync`.
- Alternatives considered: Delete the backups. Rejected: it would destroy user data without a request.

## D-007: OpenAPI

- Decision: Add `.WithName("RemoveGamepack")`, summary, description, and `Produces` for 204, 404, 500. Add response examples in `SwaggerConfig.ApplyGamepackExamples` for 404 and 500.
- Rationale: FR-010. The existing routes use the same pattern. Descriptions are in `GamepacksEndpoints` because the project has no XML comments in Swagger.
- Alternatives considered: A schema filter. Rejected: the route has no new body schema.

## D-008: Tests

- Decision: Contract tests with `GamepackContractHost` (success, list after removal, unknown name, repeat request, new install after removal is first install with no previous version, rollback after removal gives `404`, kept previous version removed, other pack unchanged, items kept and user-owned). Unit tests in the Domain test project with a failing store for FR-007. One OpenAPI test for FR-010.
- Rationale: FR-011 and the edge cases. Contract tests share a data folder, so each test must use its own host (see existing gamepack tests).
- Alternatives considered: Only contract tests. Rejected: FR-007 needs a store that fails, and the contract host cannot make one.

## D-009: Docs and the class comment

- Decision: Change the summary comment of `GamepacksEndpoints` (it says "No route uninstalls a gamepack"). Search `docs\` for a gamepack route list and add the new route if a list exists.
- Rationale: Stale comments mislead.
