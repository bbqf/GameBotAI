# Quickstart: Quiz Answer Service

1. Build a sequence for the quiz screen. Add a `quiz-lookup` step with `game`, `category`, `store`,
   `question`, and `answers` (see contracts/quiz-steps.md).
2. Add one conditional tap per answer position. Each condition reads the outcome of the lookup step,
   for example `pick_2`.
3. After the confirm, add a `quiz-learn` step. Set `lookupStepId` and `correctRegion` or `correctPosition`.
4. Run the sequence. On the first run the store is empty, so the log says `guess`.
5. After a `stored` outcome, run the quiz again. The log says `known` and the outcome is the right position.
6. Inspect the store:
   - `GET /api/quiz/records?store=pns-quiz&category=radio`
   - `GET /api/quiz/records/{id}`
7. Remove a wrong record: `DELETE /api/quiz/records/{id}`.

## Check list for tests

- Unit: normalizer, similarity, repository (add, replace, delete, concurrent write, damaged file), both handlers.
- Unit: learn with similarity (one match, none, more than one), unreadable rules (empty and white-space text), case-insensitive category, exact game.
- Unit: type-list sync test over `SequenceActionTypes.All` (FR-022).
- Log capture: each outcome of both steps, with `known` or `guess`, normalized question, picked position, and no secret (FR-020a).
- Contract: three API routes, the `store`, `game`, and `category` filters, and a 400 for each wrong step shape on sequence save.
- Coverage: the polish task checks at least 80% line and 70% branch for the new code (FR-023).
- Performance: a lookup on 200 records in under 50 ms in memory.
