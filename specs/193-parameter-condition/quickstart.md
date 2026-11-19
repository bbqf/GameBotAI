# Quickstart: Parameter Condition (193)

## 1. Save a sequence with a parameter condition

```json
POST /api/sequences
{
  "name": "Train fighters",
  "parameters": [
    { "name": "fighterBatch", "type": "number", "default": "250" },
    { "name": "fighterTier", "type": "text", "kind": "choice",
      "options": [ { "label": "Tier 7", "value": "t7" }, { "label": "Max", "value": "max" } ], "default": "max" }
  ],
  "steps": [
    { "stepId": "open-camp", "commandId": "<open camp command id>",
      "condition": { "type": "parameter", "name": "fighterBatch", "operator": "greaterThan", "value": 0 } },
    { "stepId": "tier", "stepType": "If",
      "if": { "condition": { "type": "parameter", "name": "fighterTier", "operator": "notEquals", "value": "max" } },
      "body": [ { "stepId": "tap-tier", "commandId": "<tap tier command id>" } ] }
  ]
}
```

Expected: 201. A `GET` returns `"value": 0` (a number) and `"value": "max"` (a string).

## 2. Check an unknown name with dryRun

Send the same body with `"dryRun": true` and the name `fighterBach`. Expected: 400 with the message that names `fighterBach` and the field path `condition`.

## 3. Run the sequence

- Run with `fighterBatch` = 0: the step `open-camp` does not run.
- Run with `fighterBatch` = 250: the step `open-camp` runs.
- Run with `fighterTier` = `MAX`: the `If` step does not run its body.
- Run with `fighterTier` = `t7`: the `If` step runs `tap-tier`.

The execution log shows a line such as `parameter(name=fighterBatch, operator=greaterThan, value=0) actual=0 compare=number`.

## 4. Tests

```powershell
dotnet test "C:\src\GameBot\tests\unit\GameBot.UnitTests.csproj" --filter "FullyQualifiedName~ParameterCondition"
dotnet test "C:\src\GameBot\tests\contract\GameBot.ContractTests.csproj" --filter "FullyQualifiedName~ParameterCondition"
npm --prefix "C:\src\GameBot\src\web-ui" test -- parameterCondition
```
