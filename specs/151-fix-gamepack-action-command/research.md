# Research: Keep the command action of an Action step in gamepack install

## R1: Why does the install drop the command action?

- **Decision**: The cause is a JSON shape mismatch in the gamepack file read. The validator and the applier are not the cause.
- **Rationale**: `GamepackFileSerializer.Parse` reads a sequence as the domain type `CommandSequence`. The
  domain `SequenceStep` has `Action` with `Parameters`. A gamepack author writes the API form,
  `primitiveAction` with `payload`, as in `POST /api/sequences`. The reader ignores unknown members, so
  `primitiveAction` is lost. `SequencesEndpoints.MapStepToDto` shows `commandReference` only for a stored
  command reference, so it reads as null as well. The install reports `valid: true` because no check sees the loss.
- **Alternatives considered**: A missing `JsonConverter`. Rejected: no converter exists for steps, and none is
  needed. An ignored member (`JsonIgnore`). Rejected: `Action` has no ignore attribute.

## R2: Where to convert the API form?

- **Decision**: Convert in a new `GamepackStepReader` on the `JsonObject` of each sequence, before `Deserialize<CommandSequence>`.
- **Rationale**: It keeps the domain type and the stored format unchanged. It keeps the stored form (export)
  readable. It runs in the Domain project, so the validator and the dry run use it.
- **Alternatives considered**: Reuse `SequencesEndpoints.MapSteps`. Rejected: it is private to the Service
  project and the Domain project cannot call it. Add `PrimitiveAction` to `SequenceStep`. Rejected: it changes
  the stored format and the domain model for one read path. Change the file format to the stored form only.
  Rejected: the issue reporter and the OpenAPI text use the API form. Change `POST /api/sequences`. Rejected by FR-008.

## R3: Which step lists does the reader cover?

- **Decision**: The reader walks every step list inside a step: the Loop `body`, and the `body` and `elseBody`
  of a conditional step. It applies the same rules at each depth.
- **Rationale**: FR-002 and FR-003 name these lists. `SequenceStep` has `Body` and `ElseBody`
  (`GamepackReferences.Flatten` already walks both).
- **Alternatives considered**: Walk only the root list. Rejected: a nested Action step would still lose its action.

## R4: How to stop a silent loss of other fields?

- **Decision**: The reader checks each step member against a list of known names: the members of the stored
  step, and the aliases `primitiveAction`, `commandId`, and `commandReference`. Names match without case. It
  adds a `structure` problem for each unknown member. The message has the sequence name, the step ID, and the field name.
- **Rationale**: FR-004 and the clarification say so. The check is at step level, for each step including
  nested steps. It does not check nested payloads (a condition, a `payload`, a `parameters` map), so
  conditions, loops, and `breakCondition` read as before (FR-007).
- **Alternatives considered**: `JsonUnmappedMemberHandling.Disallow` on the options. Rejected: it applies to
  all objects, including conditions, and it can break existing files. It also gives no step ID in the message.

## R5: Which form wins when several command fields are present?

- **Decision**:
  - `action` (stored form) and `primitiveAction` both present: `action` wins. If `primitiveAction` names a
    different type or command, the reader gives a `structure` problem for the step.
  - `commandReference` and `primitiveAction` (or `commandId`) name different commands: the reader gives a
    `structure` problem for the step. The install does not create the sequence.
  - Only `commandId`: the reader stores `Action` of type `command` and a `CommandReference` with that ID.
  - Only one source: the reader fills the other fields.
  - The stored form given as is stays as is.
- **Rationale**: Taken from the clarification session of 2026-10-17.
- **Alternatives considered**: Let one field win without a message. Rejected: it is a silent loss.

## R6: Does a `commandReference` keep `commandName` and `isResolved`?

- **Decision**: The reference keeps `commandId` and `commandName` (the stored `SequenceCommandReference` has both).
  `isResolved` is not a stored member. `GET /api/sequences` computes it (`MapCommandReferenceToDto`). The reader
  accepts `isResolved` inside `commandReference` and does not store it. It is not an unknown-member problem,
  because the check does not cover nested payloads.
- **Rationale**: The spec asks to keep `isResolved` "as in the file". A stored value would go out of date when
  the command is renamed or deleted. The computed value is correct for the installed command. A test shows the
  value that `GET` returns for the issue #321 file.
- **Alternatives considered**: Add `IsResolved` to the stored reference. Rejected: it changes the stored format (FR-008 intent).

## R7: Does the ID rewrite keep the fields equal?

- **Decision**: Extend `GamepackReferences.RewriteCommandIds` to also change `Action.Parameters["commandId"]`
  when `Action.Type` is `command`. It already walks `Body` and `ElseBody` through `Flatten`.
- **Rationale**: It already changes `CommandId` and `CommandReference.CommandId`. With the action now kept, the
  third copy must change too, or a step would run the old ID after an ID change. The install keeps the file
  ID when no other command uses it (existing rule).
- **Alternatives considered**: Rebuild the payload from `CommandId` at apply time. Rejected: it hides the rule
  in a second place.

## R8: Does export then install work?

- **Decision**: Yes for the stored form, and a test must prove it (FR-006).
- **Rationale**: `Serialize` writes with `SequenceOptions`, which gives `action` and `parameters`. The reader
  keeps this form as given (R5). A test compares the source step and the installed step, and runs the install a
  second time (update or take over).
- **Alternatives considered**: None.

## R9: What about other primitive action types such as `wait-for-image`?

- **Decision**: Out of scope. A test measures them: install a file with a `wait-for-image` action and compare
  with `POST /api/sequences`. If the install loses data, record the result in a new issue. Do not fix it here.
- **Rationale**: `MapWaitForImageConfig` builds `WaitForImage` from the payload in the API path. The spec
  clarification says only the command action is in scope.
- **Alternatives considered**: Port the rule now. Rejected: no requirement asks for it.

## R10: Where do the tests go?

- **Decision**: Unit tests in `tests\unit\Gamepacks` for the reader and the rewrite. A contract test for the
  issue #321 file. A shared fixture in a location that each user test project can reach. Tasks that write the
  same file are not parallel. A foundational test shows that a step problem prevents the creation of the sequence.
- **Rationale**: The spec "Notes for planning" and constitution Principle II.
- **Alternatives considered**: Copy the fixture into each project. Rejected: copies can drift.

## R11: Which step types accept the aliases?

- **Decision**: Only an Action step (`stepType` `Action`, or `stepType` absent, as `POST /api/sequences` does) accepts
  `primitiveAction`, `commandId`, and `commandReference` as aliases. On any other step type (Command, Conditional,
  Loop, Break, If) the reader treats these members as unknown and gives a `structure` problem. The message has the
  sequence name, the step ID, and the field name.
- **Rationale**: The clarification of this feature says so. The aliases exist only on an Action step. A Command step
  has no alias. Its `commandId` is a member of the stored step, and the reader reads it as the stored model does
  (the reader does not treat it as an alias and does not change it). On a Command step, `primitiveAction` and
  `commandReference` are unknown members. For Loop, Break, If, and Conditional steps, all three names are
  unknown members and give a `structure` problem.
- **Alternatives considered**: Accept the aliases on every step type. Rejected: it hides a mistake of the author.

## R12: How does an unresolvable command reference report?

- **Decision**: No new code. The reader sets `CommandId` from the alias. `GamepackReferences.CommandIds` returns it,
  and `GamepackValidator.CheckReferences` reports a `reference` problem when no command in the file or on the
  system has the ID. A test covers `commandReference`, `primitiveAction`, and `commandId`.
- **Rationale**: The existing reference check is the one gate for a dry run, an install, and a rollback.
- **Alternatives considered**: Check in the reader. Rejected: the reader has no access to the system commands.

## R13: Tests and the performance goal

- **Decision**: The full unit, contract, and integration suites run, including the `POST /api/sequences` tests and
  the existing gamepack performance tests (`GamepackPerformanceTests`, `GamepackTakeOverPerformanceTests`,
  `GamepackCatalogPerformanceTests`). The reader makes one pass over each sequence JSON object, so it adds no
  new loop over the store.
- **Rationale**: FR-008 and the performance goal of the install.
- **Alternatives considered**: Run only the Gamepack tests. Rejected by FR-008.
