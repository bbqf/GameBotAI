# Contract: Sequence parameters in ocrText.pattern and ocrText.expected

No endpoint, no request shape, and no response shape changes. This file records the changed behavior and the new texts.

## 1. Sequence save and save dry run

Endpoints: `POST /api/sequences`, `PUT /api/sequences/{id}`, `PATCH /api/sequences/{id}`. Each one with or without `"dryRun": true` (the save dry run).

### 1.1 Unknown name (current code, new fields)

Request (part):

```json
{
  "parameters": [],
  "steps": [
    { "stepId": "s3", "stepType": "Action", "commandId": "tap-gather",
      "condition": { "type": "ocrText", "region": { "x": 10, "y": 20, "width": 300, "height": 400 },
                     "pattern": "(?s)(Gathering.*){{{missing}}}" } }
  ]
}
```

Response `400`. The error body is the existing parameter error body. One error:

| Field | Value |
|-------|-------|
| `code` | `unresolvable_parameter_reference` |
| `fieldPath` | `condition.pattern` |
| `parameterName` | `missing` |
| `message` | `Step 's3': 'missing' is not declared here and is not a queue built-in.` |

The same applies to `expected` (field path ends in `.expected`) and to a leaf in a composite (`condition.children[1].pattern`). The other prefixes are `if.condition`, `loop.condition`, and `breakCondition`. `{{iteration}}` is legal in `loop.condition`, in `breakCondition`, and in a condition inside a loop body.

A name of digits only has the same rule. The `pattern` `(Gathering.*){{3}}` with no parameter `3` gives the same error with `parameterName` `3` and `fieldPath` `condition.pattern`.

### 1.2 Bad pattern with sequence defaults (new code)

Request (part): a parameter `group` with the default `"("` and the `pattern` `{{group}}abc`. The result `(abc` is not a valid regular expression.

Note: some bad values give a valid pattern by chance. For example, `{2,x}` is a literal text in .NET, not an error. Tests must use a value that breaks the pattern, such as an open parenthesis.

Response `400`. One error:

| Field | Value |
|-------|-------|
| `code` | `invalid_resolved_pattern` |
| `fieldPath` | `condition.pattern` |
| `parameterName` | the first name in the pattern |
| `message` | `Step '<label>': '<fieldPath>' with the default values gives the pattern '<pattern>', which is not a valid regular expression: <reason>. Change the default value or the pattern.` |

The check runs only when each name in the pattern has a sequence default. A name with no default, a queue built-in, or `{{iteration}}` stops the check. The run time then checks the pattern.

This check uses only the sequence defaults. The save does not know the caller values. A dry-run execution uses the supplied values (section 2).

### 1.3 Pattern with a placeholder and no default

A `pattern` with a placeholder is not compiled at save time. Example: `[{{chars}}]` with no default for `chars` saves. A `pattern` with no placeholder has the same check as now: `ocrText pattern is not a valid regular expression: <reason>`.

## 2. Run, dry-run execution, and step-through

Endpoints: `POST /api/sequences/{id}/execute` (with or without `"dryRun": true`), the queue run, and the step-through endpoints.

| Case | Result |
|------|--------|
| Each name resolves, result valid | The OCR check uses the resolved text. |
| A name does not resolve | The step fails. Message: `Step '<label>': parameter '<name>' used by field '<fieldPath>' could not be resolved from any scope.` |
| The full result is empty or white space | The step fails with the same message as an unresolved name. |
| The resolved `pattern` is not valid | The step fails. Message: `Step '<label>': field '<fieldPath>' resolved to the pattern '<pattern>', which is not a valid regular expression. Change the value of '<name>' or the pattern.` |
| Error in a break condition (unresolved name, empty result, or invalid resolved pattern) | The step does not fail. The existing rule applies (feature 066 FR-002a): "No break". The run log records the error text. The loop continues. |

A dry-run execution and a step-through read no screen. They still resolve the condition, so a resolution error gives the same failure. A dry-run execution with a supplied value that makes a bad `pattern` fails the step with the invalid-pattern message. It does not give `invalid_resolved_pattern`.

`{{3}}` with no parameter `3` gives the unresolved-parameter message with the name `3`. The OCR check never gets the text `{{3}}`.

The resolution makes a new condition. The input condition and the stored sequence keep their `{{...}}` text after the run.

## 2a. Gamepacks

No change. A gamepack install and a gamepack dry run do not check parameter names in `ocrText`. They do not do the save dry run checks of section 1. They accept a `pattern` with a placeholder.

## 3. Run log

The existing description of an `ocrText` condition and each `ocrText:` log line show the resolved value. Example for `pattern` `(?s)(Gathering.*){{{marchTarget}}}` and `marchTarget` = `4`:

```text
ocrText(region=10,20,300x400, pattern=(?s)(Gathering.*){4})
ocrText: read='...' pattern='(?s)(Gathering.*){4}' result=true
```

These lines are examples. The exact form of each line is the existing form, and it does not change. Only the value changes. The log does not show the placeholder text.

## 4. OpenAPI

Schema `OcrTextStepCondition`. The shape does not change. New text in the field descriptions:

- `pattern`: "... It can hold {{name}} placeholders of sequence parameters. The run puts each value in the pattern with no escape. Write {{{name}}} to get the value between literal braces, for example (Gathering.*){{{count}}}."
- `expected`: "... It can hold {{name}} placeholders of sequence parameters. The run puts each value in the text before the compare."
