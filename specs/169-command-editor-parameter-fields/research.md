# Research: Command Editor Parameter Fields

All items are resolved. No NEEDS CLARIFICATION remains.

## R1. Editor drops fieldTemplates today

- **Finding**: `stepsFromDto` and `stepsToDto` in `src/web-ui/src/pages/CommandsPage.tsx` do not read or write `fieldTemplates`. A command with templates from the API loses them when the person saves it in the editor.
- **Decision**: Add `fieldTemplates?: Record<string, string>` to `StepEntry`. Copy it in `stepsFromDto`. Write it in `stepsToDto` for every step type. Send nothing when the map is empty.
- **Rationale**: SC-005 and FR-007 need a lossless round trip. The fix is in the UI only.
- **Alternatives**: Leave the gap. Rejected, because the control would write templates that the next edit removes.
- **Step type change (one rule)**: When the type of a step changes, the editor drops each template key that does not belong to the new step type (the key prefix of the old type, for example `swipe.`). It keeps all other keys. When the type stays the same, the editor keeps all keys, also keys with no control. Rationale: a key of another type has no meaning on the new step (spec F1, FR-008).

## R2. Where the list of parametrizable paths comes from

- **Finding**: `CommandStepFieldPaths` is a C# class. No API or OpenAPI schema exposes the list. FR-010 forbids an API change.
- **Decision**: Add `fieldTemplatePaths.ts` in the web UI. It mirrors the 14 numeric paths (with integer or decimal kind) and the 2 image paths. A C# test, `FieldTemplatePathMirrorTests`, reads the TypeScript file and compares it with `CommandStepFieldPaths`. The test fails when the lists differ.
- **Rationale**: The spec assumption says the editor does not define its own list. The mirror is the smallest way to follow it without an API change. The drift test keeps one source of truth in effect. `QUEUE_BUILT_INS` in `types.ts` uses the same pattern.
- **Alternatives**: New endpoint (rejected: API change, non-goal). Generate the file at build time (rejected: more build work for 16 strings).

## R3. Which controls the editor shows

- **Finding**: The panels cover these paths: Swipe (`startX`, `startY`, `endX`, `endY`, `durationMs`), Tap (`confidence`, `offsetX`, `offsetY`, image), WaitForImage (`timeoutMs`, image), EnsureEmulatorRunning (`instanceIndex`). The paths `ensureGameRunning.readinessTimeoutMs` and `ensureGameRunning.readinessImage.*` have no field in the editor. `waitForImage.detectionTarget.confidence` is not a supported path.
- **Decision**: Add the control only to a field that has a path in the mirror list AND exists in the editor. That gives 12 controls: Swipe 5, Tap 3 numeric and 1 image, WaitForImage 1 numeric and 1 image, EnsureEmulatorRunning 1. The 4 `ensureGameRunning` numeric paths get no control. Their templates stay unchanged on save (FR-008). The `holdMs` field has no path, so it gets no control.
- **Rationale**: FR-001 (corrected in analysis loop 1) limits the control to fields that exist. A new panel is a new feature and is out of scope.
- **Location**: `CommandStepFieldPaths` is in `src/GameBot.Domain/Commands/CommandStep.cs`.

## R4. How a parameter field looks and works

- **Decision**: A new component, `ParameterSourceControl`, wraps one field. It takes the path, the label, the parameter list, the current template, and a change callback. In plain mode it shows the normal input plus a button "Use a parameter for {label}". In parameter mode it hides the input and shows the chip `{name}` plus a button "Remove parameter from {label}". The button opens a list. Each row shows the label, the name, and a hint when the type does not fit.
- **Rationale**: This follows the picker of `ParameterizableField` (feature 078) and gives FR-004 and FR-005. The list is a `role="listbox"` with options that a keyboard can reach. Escape closes it and returns focus to the button (FR-011).
- **Alternatives**: Type `{{name}}` into the input (rejected: the input is `type=number` and hand-typed braces are error prone). Reuse `ParameterizableField` (rejected: it is a text input that inserts inline text, but a template must replace the whole value and clear the plain input).

## R5. Fit rules and hints

- **Decision**: One pure function, `parameterFit(declaration, path)`, returns `fits` or a hint text. A queue built-in has no declaration, so it does not use this function and shows no hint. Rules from the spec (analysis loop 1):
  - Numeric path: a number type or number kind fits. A choice fits by the type of its option values: number options fit. A text type, a pattern, an image kind, or a choice with text options shows the hint "This parameter gives text. A numeric field needs a number. The save will fail."
  - Image path: the image picker kind fits. A text parameter that is not the image picker kind, a number parameter, and a choice (number or text options) show the hint "This parameter is not an image picker."
- **Rationale**: The rules mirror the service rule `YieldsText` and feature 168. The hint is advice only. The person can still select the parameter (FR-006). The service stays the only gate (FR-009).
- **Alternatives**: Block the choice (rejected by the clarification).

## R6. Plain value while a parameter is in use

- **Finding**: The service needs a plain value in the DTO. `swipe.startX` is a required integer. A tap step needs an inline `referenceImageId`. The service overlays the template at run time.
- **Decision**: Each panel keeps the plain value in local state while a template is active (edge case 4 of the spec). On save, the panel sends the kept plain value (the value the person had before). For an empty required whole-number field it sends `0`. For an empty image it sends the image that was last chosen, or an empty string. Panel validation treats a field with a template as valid.
- **Order (binding)**: The first task of the image story (Phase C) checks what the service does with an empty inline image together with an image template. This check runs BEFORE any image UI work. The task records the result in a code comment and in the PR description. Its test asserts the real behavior of the service. If the service accepts it, the image control may send an empty inline image. If the service refuses it, the control asks the person to select a stored image first, then to select the parameter (spec US2 scenario 4).
- **Rationale**: No service change is allowed. This keeps the DTO shape the same as the shape that API clients send today.

## R7. Error display

- **Finding**: `toSaveErrors` in `CommandsPage.tsx` already calls `parseParameterErrors` and shows a summary. The service issue for `text_parameter_in_numeric_field` has a message in the form `Step {order}: parameter '{name}' gives text, but field '{path}' needs a number. ...` and also carries the path and the parameter name in its details. The path alone does not name the step: a command can have two steps of the same type with the same path.
- **Decision**: Map the error to the right step. Parse the step order from the `Step {order}:` prefix of the service message. Read the field path and the parameter name from the issue details. Find the step with that order in the form, and pass the message to the field of that step only. If the editor cannot find the step (no prefix, no match, or no field for the path), it shows the error in the form summary only. The summary always shows the message, which names the field and the parameter. No new error code and no service change.
- **Rationale**: FR-009 and SC-004. The message must never point to the wrong step. The message text comes from the service, so the service stays the one source of truth.
- **Alternatives**: Match by path only (rejected: it marks the wrong step when two steps have the same type). Change the service to return a step index in a new member (rejected: API change, non-goal). Show the error in the summary only (rejected: weaker than the field-level error that FR-009 asks for).

## R8. Missing or renamed parameter

- **Decision**: If a template names a parameter that the command does not declare (and that is not a queue built-in), the chip shows the placeholder name and the text "Parameter not found". The control still lets the person remove or replace it. The editor does not block the save. The service decides.
- **Rationale**: Edge case 2 of the spec.

## R9. Queue built-ins

- **Decision**: The list shows the parameters of the command. It also shows the queue built-ins from `buildEditorScope`, in a separate group "Queue values" below the parameters (FR-002). A built-in has no declared type, so it shows no type hint.
- **Rationale**: The placeholder syntax and the service accept built-ins (feature 078). The sequence step form of feature 168 shows them in the same way.

## R10. Docs and phone level

- **Decision**: Phone level is desktop-only (spec FR-012). Update `docs/architecture.md` only if it lists editor behavior. Add a CHANGELOG entry and the version bump.

## R11. Feature 168 files and performance

- **Decision**: Do not change `specs/168-friendly-sequence-parameters/demo-data.ps1` or the quickstart of feature 168. The manual check of this feature shows that the Check 1 and Check 5a Part B flows of feature 168 run in the UI alone (SC-003). Performance target: the control opens in under 100 ms with up to 50 parameters. A component test or a reasoned target shows it.
- **Rationale**: The spec marks a change to the 168 files as out of scope.
