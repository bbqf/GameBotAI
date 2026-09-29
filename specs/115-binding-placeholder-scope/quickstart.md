# Quickstart: Verify binding placeholder resolution

**Feature**: 115-binding-placeholder-scope | **Date**: 2026-09-29

## 1. Run the automated tests

Run the tests from the repository root. Use absolute paths.

```powershell
dotnet test "C:\src\GameBot\tests\unit\GameBot.UnitTests.csproj" --filter "FullyQualifiedName~ParameterScopeBinding|FullyQualifiedName~SequenceRunnerBindingScope|FullyQualifiedName~CommandStepResolver|FullyQualifiedName~ParameterScope"
dotnet test "C:\src\GameBot\tests\integration\GameBot.IntegrationTests.csproj" --filter "FullyQualifiedName~BindingPlaceholderScope|FullyQualifiedName~ParametrizedReferenceImage"
```

Then run the full unit, integration, and contract suites. All current tests must pass without change (SC-004).

## 2. Reproduce issue #246 on a running service (optional, manual)

Use the REST API on port 8080. The service must run a build that has this feature.

1. Make a command `ZZZ.NovaParamTap`. Declare the text parameter `novaOptionImage` (required). Add one `PrimitiveTap` step with `fieldTemplates` `{"primitiveTap.detectionTarget.referenceImageId": "{{novaOptionImage}}"}`.
2. Make a sequence. Declare `novaOptionImage` (required, no default). Add one step that runs the command with `parameterBindings: [{"name": "novaOptionImage", "value": "{{novaOptionImage}}"}]`.
3. Add a queue template entry for the sequence with `parameterValues: [{"name": "novaOptionImage", "value": "pns-alliance-nav-button"}]`.
4. Start the queue and let the entry run.
5. Read the execution log of the command.

Expected result:

- The `parameters` item shows `novaOptionImage=pns-alliance-nav-button` with `originLayer` `entry`.
- The tap step looks for image `pns-alliance-nav-button`. It does not report `template_not_found` when that image is on the screen.

## 3. Check the failure case

1. Change the sequence declaration of `novaOptionImage` to `required: false` with no default. The save accepts the binding, because the sequence declares the name. A required name with no value gives `409 missing_required_parameters` before the run, so this step makes the run start.
2. Run the sequence ad hoc with `POST /api/sequences/{id}/execute` and no `parameters` body.

Expected result:

- The step fails with the message "Step '<stepKey>': parameter 'novaOptionImage' used by field 'parameterBindings.novaOptionImage' could not be resolved from any scope. Do one of these to supply a value for 'novaOptionImage'. Supply the value in the queue template entry or in the run request. Give 'novaOptionImage' a default value in the sequence or in the calling command. Bind a literal value, or bind a value in the calling command."
- The command does not run, and no log item shows the text `{{novaOptionImage}}` as a value.

## 4. Check the controls

- A step with no `parameterBindings` gives the command the entry value with `originLayer` `entry`.
- A step with the literal binding value `pns-todo-radar` gives the command `pns-todo-radar` with `originLayer` `command`.
- A binding value `${novaOptionImage}` gives the command the literal text `${novaOptionImage}`.
