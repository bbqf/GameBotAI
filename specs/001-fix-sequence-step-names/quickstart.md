# Quickstart: Preserve Sequence Step Command Names

## Preconditions

- Work from `C:\src\GameBot` on branch `001-fix-sequence-step-names`.
- Ensure no `GameBot.Service` process is holding build outputs before validation.
- Use the existing command and sequence authoring UI plus execution-log detail page.

## 1. Reproduce the original bug

1. Start the backend and web UI.
2. Create at least two commands with distinct names.
3. Create a sequence with multiple command-backed steps using those commands.
4. Save the sequence, reopen it, and confirm the pre-fix behavior if still present:
   - command dropdowns revert to `Select command`
   - step identity is no longer obvious from the saved state
   - execution logs do not identify the selected command clearly

## 2. Validate sequence authoring round-trip after the fix

1. Create or edit a sequence with at least three steps.
2. Save the sequence.
3. Reload the editor for that sequence.
4. Confirm each step retains:
   - the same `stepId`
   - the same selected command
   - the same user-visible label where applicable
5. Save again without changes and confirm no command reassignment occurs.

## 3. Validate unresolved command behavior

1. Save a sequence with a command-backed step.
2. Delete or otherwise make the referenced command unavailable.
3. Reopen the sequence.
4. Confirm the affected step:
   - appears as unresolved rather than blank
   - shows the last saved command name when available
   - remains editable without corrupting other steps

## 4. Validate execution-log wording

1. Execute a sequence containing command-backed steps with recognizable step labels and command names.
2. Open the related execution-log detail view.
3. Confirm each command-backed step message includes both:
   - the step label
   - the command name
4. Confirm deep links still point to the correct sequence step when the step exists.

## 5. Run required validation commands

```powershell
dotnet build -c Debug
dotnet test -c Debug --logger trx
```

If tests fail, run:

```powershell
.\scripts\analyze-test-results.ps1
```

## 6. Targeted regression areas

- Contract tests for sequence create/get/update with per-step payloads
- Integration tests for saved step metadata round-trips
- Unit or integration tests for execution-log detail projection
- Web UI tests for reopening saved sequences and rendering unresolved command states