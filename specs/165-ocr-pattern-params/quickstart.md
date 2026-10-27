# Quickstart: Sequence parameters in ocrText.pattern and ocrText.expected

Use these steps to see the feature work. All commands use PowerShell with absolute paths.

## 1. Build and run the tests

```powershell
dotnet build "C:\src\GameBot\GameBot.sln" -c Debug
dotnet test "C:\src\GameBot\GameBot.sln" -c Debug --filter "FullyQualifiedName~OcrText|FullyQualifiedName~SequenceStepConditionResolver|FullyQualifiedName~ParameterReferenceScanner|FullyQualifiedName~ParameterValidation"
```

Expected: all tests pass. Then run the full test set before each commit.

## 2. Save a sequence with a placeholder in a pattern

Send this body to `POST /api/sequences` (add `"dryRun": true` for a save dry run, which checks only):

```json
{
  "name": "march-check",
  "parameters": [ { "name": "marchTarget", "default": "3" } ],
  "steps": [
    { "stepId": "gather", "stepType": "Action", "commandId": "<an existing command id>",
      "condition": { "type": "ocrText",
                     "region": { "x": 0, "y": 0, "width": 400, "height": 600 },
                     "pattern": "(?s)(Gathering.*){{{marchTarget}}}" } }
  ]
}
```

Expected: `201` (or `200` with `dryRun: true`).

## 3. See the rejections

| Change to the body of step 2 | Expected result |
|------------------------------|-----------------|
| Remove the `marchTarget` parameter | `400`, `unresolvable_parameter_reference`, field path `condition.pattern` |
| Use `"expected": "Level {{lvl}}"` in place of `pattern`, with no `lvl` parameter | `400`, `unresolvable_parameter_reference`, field path `condition.expected` |
| Parameter `group` with default `"("` and `pattern` `{{group}}abc` | `400`, `invalid_resolved_pattern`, field path `condition.pattern` |
| Pattern `(Gathering.*){{3}}` with no parameter `3` | `400`, `unresolvable_parameter_reference`, name `3`, field path `condition.pattern` |
| Pattern `(Gathering.*){3}` with no placeholder | Saves as before |

These results are the same in the save dry run (`"dryRun": true`). The save dry run uses only the sequence defaults.

## 4. Run with a value

Run the saved sequence on a session with the value `marchTarget` = `2` (queue template entry value, or the parameter values of the run request). Show a screen with two "Gathering" lines in the region.

Expected: the condition is true. The run log line of the condition shows `pattern=(?s)(Gathering.*){2}`. It does not show `{{marchTarget}}`.

With `marchTarget` = `3` on the same screen, the condition is false.

After the run, get the sequence with `GET /api/sequences/{id}`. Expected: the `pattern` is still `(?s)(Gathering.*){{{marchTarget}}}`.

## 5. See the run-time failures

| Value | Expected result |
|-------|-----------------|
| No value and no default for a name in the pattern | The step fails: `parameter '<name>' used by field 'condition.pattern' could not be resolved from any scope.` |
| `countPattern` = `"("` with `pattern` `{{countPattern}}` | The step fails: `field 'condition.pattern' resolved to the pattern '(', which is not a valid regular expression.` |
| `countPattern` = `" "` with `pattern` `{{countPattern}}` | The step fails with the unresolved-parameter message. |
| One of these errors in a break condition | The step does not fail. The result is "No break". The run log records the error, and the loop continues. |

Do the same with `POST /api/sequences/{id}/execute` and `"dryRun": true`. Expected: the same step failures. A bad supplied value gives the invalid-pattern message, not `invalid_resolved_pattern`.

## 6. Step-through

Start a step-through of the same sequence with the same values. Expected: the step condition resolves in the same way. A bad value fails the step with the same message as a run.

## 7. Docs and spec status

At the end of the work, make sure of these items (FR-017):

- The `**Status**:` line of `spec.md` is `Implemented`.
- `specs/STATUS.md` shows feature 165 as `Implemented`. The row is added in the last task, with this status.
- `docs/architecture.md` names `ocrText.pattern` and `ocrText.expected` as fields that resolve placeholders.
- `CHANGELOG.md` has an entry for feature 165 (issue #347).
- The PR description gives the performance statement of SC-006: same instance and no new allocation with no placeholder, and less than 1 ms with a placeholder.
