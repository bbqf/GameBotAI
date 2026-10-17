# Research: Gamepack command reference check

## R1. What does the existing check cover?

- Decision: Extend `GamepackReferences.CommandIds(CommandSequence)`. Do not add a parallel check.
- Finding: `CheckReferences` in `GamepackValidator.cs` builds `commandTargets` (gamepack command ids plus instance command ids, minus removed commands) and calls `ReportMissing` with `GamepackReferences.CommandIds(sequence)`. That method reads only `SequenceStep.CommandId` (through `Flatten`, so nested bodies are covered).
- Rationale: Same message, same code path as images (clarification 1).

## R2. Which step forms are missed?

| Form | Where | Checked today |
|------|-------|---------------|
| `commandId` | `SequenceStep.CommandId` | Yes |
| `commandReference.commandId` | `SequenceStep.CommandReference.CommandId` | No |
| Action payload `commandId` | `SequenceStep.Action.Parameters["commandId"]` when type is `command` | No |
| `commandReference.commandName` | `SequenceStep.CommandReference.CommandName` | No |

The gamepack step reader (`GamepackStepReader.ConvertAction`) makes the three id forms agree on install input. But a file in the stored form can carry a different value in each. `RewriteCommandIds` already rewrites all three id forms. The check must read the same three.

## R3. How does the run time resolve a command?

- Finding: By id. `SequenceStep.CommandId` is the key (see `SequencesEndpoints` `ExplicitCommandId` and `ValidateCommandReferencesExist`). `CommandReference.CommandName` is a label that the service saves for display (`EnrichCommandReferences`, `StepPath.DescribeLeaf`). It is not used to find a command.
- Decision: Check a name only when the step names no id in any id form (name-only reference). A name next to an id is a label and can be stale. Checking it would make existing valid gamepacks invalid and break SC-002.
- Alternatives: (a) check every name - rejected, breaks valid packs. (b) skip names - rejected, spec FR-002 requires the name form.

## R4. Name match rule

- Decision: Case-insensitive match (`StringComparer.OrdinalIgnoreCase`), trimmed, against gamepack command names and instance command names. Commands that the plan removes do not count (same rule as ids).
- Rationale: Clarification 2 and the existing validator rule.

## R5. Message and owner

- Decision: Reuse `ReportMissing`: "Command 'x' is not in the gamepack or in the installation." Owner is the sequence name. No step index (clarification 3).

## R6. Install

- Finding: Install uses the same validator result, so it reports the same problems once `CheckReferences` changes. Add an integration test only.
