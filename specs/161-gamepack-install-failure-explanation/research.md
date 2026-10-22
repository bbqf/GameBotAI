# Research: Gamepack Install Failure Explanation

No item in the Technical Context needed clarification. This file records the findings from the source and the decisions.

## Findings in the source

- `GamepackProblem` has `Category` (structure, reference, conflict, signature), `ObjectType`, `Name`, `Message`,
  `Owner` (conflict only: `user` or `<name> <version>`), and `Code`.
- `GamepackValidator.Decide` makes a conflict for each match that is not an update of the same gamepack. With
  `takeOver` on, the first user-owned match is a take-over and makes no problem. A second user-owned match, and every
  match owned by another gamepack, stays a conflict.
- `GamepackService.InstallAsync` runs the validator once, in the install gate (`ValidateAsync(..., takeOver: options.TakeOver, seeds: true)`).
  It returns `Invalid` with the problems. The validator has no write, so a second call with `takeOver: true` is safe.
- `GamepackUpdateOrchestrator.InProgressProblem` makes a conflict with `Owner` null and `Code` `update_in_progress`.
  Take-over cannot resolve it.
- `GamepacksEndpoints.ToInstallResult` maps all outcomes. The catalog install route calls it too.
  One change there covers both routes.
- `GamepackProblemDto.From` is also used for the rollback refused answer and the managed update report.
- An API major version problem is a manifest (structure) problem. No special case is needed.
- Web UI: `GamepacksPage.handleInstall` calls `installFromCatalog`. A 422 becomes a `CatalogApiError` with only the first
  problem message. A dry run that is not valid shows all messages joined as one text. `errorText(err, '')` can be empty,
  so the failure notice can have no reason.

## Decisions

### D1: Compute the facts in one domain class

- Decision: a pure static class `GamepackFailureExplainer` holds the rules and the STE text.
- Rationale: the install route, the catalog route, the rollback answer, and the managed report share one source.
  The page shows server text (spec clarification).
- Alternatives: store the facts in `GamepackProblem` (the validator and the serializer need changes in many places);
  build the text in the page (two copies, API callers get nothing).

### D2: `wouldInstallWithTakeOver` by a second validator run

- Decision: the rule is exact (spec clarification I1). A valid check gives true. A check with take-over on that is not valid gives
  false. A check with take-over off that is not valid runs the validator again with `takeOver: true` in the same gate. The
  result of that run is the value. The second run uses the same inputs as the first run (file, signature policy, seeds),
  except that take-over is on (spec clarification, loop 2).
- Rationale: it is exact. The mixed cases are right with no new rule code. A rule-based guess (all problems are user
  conflicts) is wrong when two user objects have one name.
- Alternatives: guess from the problem list (not exact); add a flag to the validator (changes the validator, risk against FR-009).
- Cost: one more validator run, only for a failed check without take-over. No write.

### D3: `takeOverResolves` for a problem is rule-based

- Decision: true only for a conflict with owner `user`, no `Code`, and take-over off in the request. With take-over on, each
  remaining problem gives false (spec clarification U1).
- Rationale: it is the rule of the spec (FR-004, FR-004a). The whole-file answer is `wouldInstallWithTakeOver`, which is exact.
  A second user-owned object with the same name is a conflict that take-over resolves (spec clarification U2).
- Alternative: a diff of the two validator runs for each problem. Rejected: the message is the only key, and the gain is small.

### D4: Summary is always present

- Decision: `summary` is in every install answer that has a validation result. A valid file has headline `No problems.` and no entries.
- Rationale: spec scenario 4. A caller needs no special case.

### D5: Summary shape and order

- Decision: object `{ headline, entries[] }`, entry `{ category, count, label, remedy, resolvedByTakeOver }`.
  `resolvedByTakeOver` is a bool. The order is structure, signature, reference, conflict (spec clarification).
  The DTO property order puts `summary` before `problems`.
- Alternative: a plain list of strings (the page could not use the count and category).

### D6: Conflict entry with mixed owners

- Decision: the label and the remedy of the conflict entry depend on the problems. If all are user-owned with no code: label
  "conflicts: objects owned by the user" and the take-over remedy. If all are owned by another gamepack: the rename remedy.
  If a problem has a special code: that remedy. Mixed groups give a label and a remedy that name each part.
  `resolvedByTakeOver` is true only if every problem in the entry has `takeOverResolves` true.
- Rationale: the flag is a bool (spec clarification I2), so it states the whole entry.

### D7: API major version problem

- Decision: no special case. It is a structure (manifest) problem and gets the structure remedy (spec clarification).

### D8: Page error path

- Decision: a new `CatalogInstallRejectedError` (subclass of `CatalogApiError`) keeps the 422 body. The page reads the
  summary from it for Install and Update. A dry run that is not valid shows the summary from the answer. A fallback text
  covers an empty reason (network or server error).
- Rationale: old code that tests `instanceof CatalogApiError` keeps working (FR-011). Nothing parses text.

### D9: Other install modes and routes

- Decision: no change to the managed update route flow and the file install flow beyond the shared DTO. The page uses the
  catalog route for Install and Update, so the page needs only the catalog answer. The rollback refused answer and the managed
  report get the two problem fields and no summary.

### D10: Evidence for FR-009, FR-010, FR-015

- Decision: no new test for the blocking sets. The existing install and take-over tests stay green with no change to their
  expected results. They are the evidence (SC-004).
