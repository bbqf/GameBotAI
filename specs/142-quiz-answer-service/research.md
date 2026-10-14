# Research: Quiz Answer Service

## R1. Where do the new step types plug in?

- **Decision**: Treat both steps as service-level actions, like `notify`. Touch the six backend sites
  found with `ActionTypes.RescheduleSelf`, plus the OpenAPI payload descriptions.
- **Sites**:
  1. `SequenceActionTypes.All` (`GameBot.Domain/Actions/SequenceActionTypes.cs`).
  2. `FileSequenceRepository` allow-list `supportedActionTypes`.
  3. `SequenceStepValidationService` (shape validation near the `RescheduleSelf` branch).
  4. `SequenceRunner.IsServiceLevelAction`.
  5. `SequenceExecutionService.DispatchActionAsync` (and the outcome check near line 429).
  6. `StepThroughActionPreview`.
  7. `PrimitiveActionSchemaFilter.PayloadDescriptions` (OpenAPI).
- **Rationale**: `IsServiceLevelAction` makes the runner call the action dispatcher, record the
  returned outcome in the step outcome map, and never stop the sequence. This is the wanted behavior.
- **Alternatives**: A command step with a new command kind. Rejected: it needs a device command and
  does not give a custom outcome.
- **Guard**: A contract test saves a sequence with a wrong quiz step shape and expects 400, not 500.

## R1a. Type-list sync test (FR-022)

- **Decision**: One unit test loops over `SequenceActionTypes.All`. For each type it checks: an entry
  in `PrimitiveActionSchemaFilter.PayloadDescriptions`, the `FileSequenceRepository` allow-list, the
  validation service, `IsServiceLevelAction` (or its documented non-service set), and the dispatch
  and step-through preview. The test fails and names the type and the place when one is missing.
- **Rationale**: The six-site risk is known. The test makes a missing site a build failure.
- **Alternative**: Check by grep in review. Rejected: not automatic.

## R2. How to read text

- **Decision**: Call `OcrRegionReader.Read(frame, region, ocr)` with the frame from
  `ISessionFrameSource.Capture(sessionId)` and the injected `ITextOcr`. Capture one frame for all
  regions of one step.
- **Rationale**: This is the code that `ocrOffset` and `POST /api/ocr/read` share. The spec forbids a change.
- **Alternatives**: Call the HTTP endpoint. Rejected: extra hop and no gain.
- **Failure rule**: No session, no frame, crop failure, an OCR fault, or a region whose text is empty
  or only white space after normalization gives `unreadable`. This covers the question region and
  every answer region, and no pick is made. `service_unavailable` is only for a store that cannot be used.

## R3. Text normalization and similarity

- **Decision**: Normalize with: Unicode form KC, lower case (invariant), replace each run of
  non-letter and non-digit characters with one space, trim. Similarity is `1 - levenshtein / max(len)`
  on the normalized text. Default limit 0.85. A step can set a value from 0 to 1 (exclusive of values
  outside the range; the validator rejects them).
- **Rationale**: One tolerance for small OCR errors. No new package. 200 records need only a linear scan.
- **Alternatives**: Token set match, or a trigram index. Rejected: more code, no need at this size.
- **Tie rule**: On equal best score, take the newest record by `updatedAt`.

## R4. Answer match by text

- **Decision**: Match the stored answer text to the current answer texts with the same similarity
  rule. Take the best answer at or above the limit. If no answer reaches the limit, treat the question
  as not known and make a guess (spec edge case).
- **Rationale**: FR-010 stores text, not position. OCR noise needs a tolerance on answers too.

## R4a. Learn match uses the similarity limit (FR-008)

- **Decision**: The learn step takes the correct answer text (from the region, or from the lookup
  result at the given position). It scores that text against each answer text of the lookup result
  with the same similarity function. It counts the answers at or above the limit of the lookup
  (default 0.85). Exactly one: store the pair, outcome `stored`. None: `skipped_no_match`. More than
  one: `skipped_ambiguous`. A position above N gives `skipped_no_match`.
- **Rationale**: One tolerance for OCR errors in lookup and learn (clarification, loop 1).
- **Stored text**: The matched answer text of the lookup result, not the new OCR text.

## R5. Random guess

- **Decision**: Use `Random.Shared.Next(1, N + 1)`. Hide it behind a small `IQuizRandom` seam so tests are deterministic.
- **Rationale**: Deterministic unit tests (Constitution II).

## R6. Storage and concurrency

- **Decision**: `FileQuizAnswerRepository(root)` uses folder `quiz`. One file per store. The file name
  is the SHA-256 hex (first 32 characters) of the lower-case store name, and the file holds the real
  name. This avoids path injection and invalid file name characters. A `ConcurrentDictionary` maps
  store name to a `SemaphoreSlim`. Each write loads (or uses the cached list), changes it, writes a
  temp file, then `File.Move` with overwrite. Reads use the cached list after the first load.
- **Rationale**: FR-016, FR-017. Atomic replace prevents a damaged file after a crash.
- **Damaged file**: A parse error throws `QuizStoreUnavailableException`. The handler maps it to `service_unavailable`.
- **Alternatives**: One file for all stores. Rejected: a damaged file would block all stores.
- **Note**: The API operations need a list across stores. The list reads all files in the folder.
  Records carry their store name. A record id is a GUID string, unique across stores.

## R7. Lookup result keeper

- **Decision**: A scoped object per run: `Dictionary<stepId, QuizLookupResult>` held by a keeper that
  the step handler gets by run id (`runId` is a dispatch argument). Entries are removed when the run ends.
  If the run id is null (ad hoc), use the sequence id plus step id as a key with a short expiry.
- **Rationale**: The lookup data must pass from one step to a later step. The spec calls it "Lookup result".
- **No lookup result**: If the referenced lookup did not run (other branch) or kept no result, the
  learn gives `skipped_no_match` and the log says why. The save-time check covers only order and type.
- **Alternative**: Write the data into the parameter scope. Rejected: the scope holds strings, and the
  answer texts could leak into logs.
- **Open point closed**: Step-through uses the preview only and does not run the handler (R9).

## R8. Learn rule on repeat and change

- **Decision**: Key = (store, game, category, normalized question). Same key with the same answer
  text: update `updatedAt` only, no duplicate. Same key with a new answer: replace and log "replaced".
  The key match for the record write uses exact normalized question text, so learn never merges two
  similar questions. The lookup uses the similarity limit. (The similarity limit in learn applies to
  the answer match, see R4a.) Game is exact; store and category ignore case (R14).

## R9. Step-through

- **Decision**: The step-through preview of both steps reports that the step would read the screen
  and use the store. It never writes to the store and never reads the device.
- **Rationale**: Same approach as the `reschedule-self` and `notify` preview.

## R10. Logging

- **Decision**: One log line per step: step id, outcome, `known` or `guess`, normalized question,
  picked position. The execution log message carries the same data. No raw OCR of answers beyond the
  normalized question. No secrets exist in the data.
- **Tests (FR-020a)**: A log-capture test runs each outcome of both steps (`pick_N` known, `pick_N`
  guess, `unreadable`, `service_unavailable`, `stored`, `skipped_no_match`, `skipped_ambiguous`). It
  asserts the outcome, `known` or `guess`, the normalized question, and the picked position in the
  log. It also asserts that the log has no credential, token, or device identifier. Remember: the
  logging gate must open the component before capture sees lines.

## R11. Web UI

- **Decision**: Add the two type names to the step type list of the sequence editor
  (`SequencesPage.tsx`). Add a basic field form for each type in
  `components/sequences/PrimitiveActionFields.tsx`: for `quiz-lookup` the fields game, category,
  store, similarity, question region, and answer regions; for `quiz-learn` the fields lookup step id
  and either correct position or correct region. Add Jest tests for the form. Add no page. Phone
  support level does not change.
- **Rationale**: The author must build the steps in the editor (spec clarification, loop 2).

## R16. API example step

- **Decision**: The `PayloadDescriptions` entry of each new step in `PrimitiveActionSchemaFilter.cs`
  holds an example step. The example is the JSON of contracts/quiz-steps.md. A contract test checks
  that the example exists and passes the save-time validation.

## R12. Default category and game input

- **Decision**: `game` is a required parameter (clarification). `category` is required, no default.
  The validator rejects both when missing or blank.

## R13. Coverage check (FR-023)

- **Decision**: The final polish task runs the repository coverage run and reads the result for the
  new files (`GameBot.Domain/Quiz`, `GameBot.Service/Services/Quiz`, the quiz endpoints, the new
  validation branches). The targets are at least 80% line and 70% branch (Constitution II). The task
  adds tests for any gap.

## R14. Case rules for game and category

- **Decision**: The game match is an exact identifier match. The category match ignores case (invariant
  compare) in the lookup, in the learn key, and in the list filter. The store name also ignores case
  (the file hash uses the lower-case name). The list filters `store` and `category` ignore case; `game` is exact.

## R15. List API filters (FR-014)

- **Decision**: `GET /api/quiz/records` accepts `store`, `game`, and `category`. Filters combine with
  AND. The record shows its `store` name. See contracts/quiz-api.md.
