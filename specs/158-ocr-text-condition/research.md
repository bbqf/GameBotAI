# Research: OCR Text Condition for Sequences

All items from the Technical Context are resolved. No NEEDS CLARIFICATION remains.

## R1: Where does the new leaf plug in?

- **Decision**: Follow the `lastRun` leaf (feature 105). Add `OcrTextStepCondition` to `SequenceStepCondition.cs` with the discriminator `ocrText`. Add a case in each of these places: `CompositeConditionValidator.Walk`, `SequenceStepConditionEvaluator.EvaluateLeafAsync` and `Describe`, `SequenceRunner.DescribeBreakCondition`, `FileSequenceRepository` guard, `SequencesEndpoints` (domain to DTO, contract to domain), `SequenceStepContracts.cs`.
- **Rationale**: This is the newest leaf that needs data from outside the domain. All switches are known by search for `LastRunStepCondition`. The pattern passes review before.
- **Alternatives considered**: A generic leaf registry. Rejected: it is a large refactor and outside the scope.

## R2: How does the domain reach the OCR engine?

- **Decision**: An ambient scope, `OcrTextEvaluationScope` (an `AsyncLocal`), holds one delegate that the service supplies. The pattern is the same as `SequenceRunContext` and the device scope.
- **Rationale**: The `SequenceRunner` methods are large, and a new parameter would make them larger. The build analyzers degrade on large methods. The domain project must not use `System.Drawing`.
- **Alternatives considered**: (a) Reuse `SequenceRunContext`. Rejected: only a queue run pushes it, and an ad-hoc run also needs OCR. (b) Add a parameter to `EvaluateAsync` and to the runner. Rejected for the analyzer reason. (c) Map `ocrText` to the `Condition` image evaluator delegate. Rejected: that type is image-specific.

## R3: How is the read done, with no copied code?

- **Decision**: `OcrTextConditionReader` calls `ISessionFrameSource.Capture`, then `OcrRegionReader.Read`, then `OcrTextParsers.TryParse`. These are the same calls that `OcrReadService` and `OcrOffsetResolver` use. The reader repeats the region-inside-frame check. The check is a short expression. If review prefers, a task can move it to one shared static helper that `OcrReadService` also calls. That move does not change the endpoint behavior.
- **Rationale**: FR-007 and SC-005. The same crop and engine give the same raw text.
- **Alternatives considered**: Call `OcrReadService.Read` with a serial. Rejected: it looks up a session by serial, and it maps faults to HTTP status codes. The run already knows its session id.

## R4: Match rules

- **Decision**: `expected`: `string.Equals(text.Trim(), expected, StringComparison.Ordinal)`. The spec says white space at the start and end of the text is removed. The rule uses the `expected` value as stored. `pattern`: `Regex.IsMatch(text, pattern, RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1))`. A match time-out (`RegexMatchTimeoutException`) gives false as a plain mismatch with no reason code (FR-017). The run log says "pattern timed out". The text for `pattern` is the trimmed text.
- **Rationale**: The clarification session fixed these rules. A match time limit stops a bad pattern from blocking a run. No reason code for a time-out keeps the closed list in FR-013 small.
- **Alternatives considered**: Case-insensitive compare. Rejected by the clarification.

Note on empty text (FR-019): an empty read is compared like any text, with no special case. An empty `expected`, or a `pattern` that matches empty text, gives true. So an empty `expected` is allowed when the field is present (not null). The rule "exactly one of" tests for null, not for empty. A test covers it.

## R5: Parser

- **Decision**: With a parser, the compared text is the `value` that `OcrTextParsers.TryParse` returns (`hh:mm:ss`). If the parse fails, the leaf is false and the record says "text did not parse as hh:mm:ss". The parser name is checked at save time by `OcrTextParsers.TryGetName`, in the sequence endpoint only (FR-018). The match ignores case, as `POST /api/ocr/read` does. The system stores the canonical name that the method returns (`HH:MM:SS` is stored as `hh:mm:ss`). A contract test covers this. A sequence that enters by another path (for example gamepack import) with an unknown parser is not rejected. At run time the reader finds no parser, and the leaf is false with `parse-failed`. A test covers it.
- **Rationale**: FR-005, FR-010, FR-018. The domain does not reference the service, so the name check is in the endpoint.
- **Alternatives considered**: Move `OcrTextParsers` to the domain. Rejected: it changes a file of feature 128 with no need.

## R6: Run log and step-through

- **Decision**: The scope holds a list of `OcrTextEvaluationRecord`. The runner drains it after each condition evaluation and appends one line for each record to the step detail. In the step-through (dry) mode, the stepper has no scope. It shows the note `ocrText is not evaluated`, as it does for `lastRun`, and treats the leaf as true, as it does for `lastRun`. A stepper test covers the note (`SequenceStepperOcrTextTests`).
- **Rationale**: FR-008 needs a record for each evaluation, also for a child. A record from the leaf gives that for free. The step-through already has the note pattern.
- **Alternatives considered**: Log in the service delegate only. Rejected: the log of a step would not show the result of the compare, and a test could not read it from the run result.

## R7: Failure behavior

- **Decision**: The leaf never throws for a capture or engine fault. The delegate returns a failure reason. The list is closed (FR-013): `no-session`, `capture-failed`, `ocr-unavailable`, `region-outside-frame`, `region-invalid`, `ocr-error`, `parse-failed`. No other code is used. A `pattern` time-out is not a failure: it is a plain mismatch with no code, and the log says "pattern timed out" (FR-017). `no-session` also covers a run with no emulator session (dry run or no capture). When no scope is present, the evaluator gives false with the reason `no-session`. Every run entry point that has an emulator session pushes the scope (FR-020). The tasks list all entry points, and a test covers each one. The leaf is false (before `negate`). The record holds exactly one reason. A plain mismatch has no reason. See data-model.md for the meaning of each code. The `negate` flag still applies after the false result, as for `imageVisible`.
- **Rationale**: FR-009 and the clarification. An unavailable delegate is not a `ConditionEvaluationException`, because the spec requires false and a reason.
- **Alternatives considered**: Throw `ConditionEvaluationException`. Rejected: it would fail the step, which the spec forbids.

## R8: OpenAPI and documents

- **Decision**: Add `OcrTextConditionContract` with the discriminator `ocrText`. Add `OcrTextConditionSchemaFilter` for the descriptions. Register the schema in `ConditionalFlowSchemaDocumentFilter`. Update `docs/architecture.md`, `CHANGELOG.md`, and `specs/STATUS.md`.
- **Rationale**: FR-012 and the living documentation principle.
- **Alternatives considered**: None.

## R9: Web UI

- **Decision**: No change. The OpenAPI description says that the web UI does not know this type.
- **Rationale**: Clarification 3 and FR-016. The same decision was taken for `lastRun`.
- **Alternatives considered**: A condition editor in the web UI. Rejected as out of scope.

## R10: Gamepack files

- **Decision**: Add a test that a gamepack sequence with an `ocrText` condition loads. The gamepack loader uses the same polymorphic `SequenceStepCondition`, so the new derived type is covered. The check files are `src/GameBot.Domain/Gamepacks/GamepackTypeDiscriminatorCheck.cs` and `tests/unit/Gamepacks/GamepackTypeDiscriminatorCheckTests.cs`. The check reads the polymorphism data of the type metadata, so it needs no list change. A test covers that a sequence member with `ocrText` passes the check. If the check does hold a type list, the task updates it.
- **Rationale**: Feature 154 made the gamepack reader sensitive to the `type` member. The new type must not break it.
- **Alternatives considered**: None.
