# Implementation Plan: List-valued sequence parameters

**Branch**: `179-list-valued-parameters` | **Date**: 2026-11-09 | **Spec**: [spec.md](spec.md)
**Input**: Feature specification from `/specs/179-list-valued-parameters/spec.md`

## Summary

A parameter declaration of kind `image` or `choice` gets an optional `multiple` flag, with optional `minItems` and `maxItems`. A binding (entry layer, template entry, queue entry) can hold an ordered list of unique values in a new `values` member. The domain keeps the scope engine text-based. A list binding also sets `Value` to the items joined by `|`. So the scope, `{{name}}` substitution, and pass-through bindings work with no change to their text model. Two new checks are added: a list of two or more items in a one-value field fails the step, and list limits are validated. The web UI value form gets a multi-select mode for the image picker and the choice field.

## Technical Context

**Language/Version**: C# on .NET (existing solution), TypeScript and React in `src/web-ui`  
**Primary Dependencies**: ASP.NET Core minimal API, Swashbuckle (Swagger), Vitest and Testing Library  
**Storage**: Existing file-based JSON stores. New members are optional and omitted when null, so old files stay valid.  
**Testing**: xUnit (Domain unit tests, Service integration and contract tests), Vitest for the web UI  
**Target Platform**: Windows service plus browser UI (phone and desktop)  
**Project Type**: web-service with web UI  
**Performance Goals**: No measurable change. Lists hold at most 50 items. Joining and splitting are linear in the item count.  
**Constraints**: Old data and single-value parameters keep their behavior. No change to `text` and `number` kinds.  
**Scale/Scope**: About 10 source files in Domain and Service, 4 to 6 web UI files, docs, and tests.

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

- Code quality (I): new code stays small and cohesive. A new helper class `ParameterListRules` holds list logic. Public members get XML docs. Method names use CamelCase with no underscore. PASS.
- Testing (II): unit tests for the rules, the scope, and the resolver. Integration tests for the API, the stores, and Swagger. Web UI tests for the form. PASS.
- UX consistency (III): messages say what is wrong and what to do. Phone support level is **full**: the icon selection is a touch-sized toggle grid and fits a phone width. The change follows docs/mobile-ui-principles.md. PASS.
- Performance (IV): goals declared above. PASS.
- Living documentation (V): update `docs/architecture.md` (API surface and domain model, refresh "Last reviewed"), add `specs/STATUS.md` row, and set the spec `Status`. PASS.
- STE (VI): all new text in this plan and the artifacts is written in STE. PASS.
- Version bump: bump `patch` in `installer/versioning/version.override.json` before the PR. Add a CHANGELOG line.
- No violations. The Complexity Tracking table is not needed.

Post-design re-check: no new violation. The design adds no project, no dependency, and no new store.

## Project Structure

### Documentation (this feature)

```text
specs/179-list-valued-parameters/
├── plan.md
├── research.md
├── data-model.md
├── quickstart.md
├── contracts/
│   └── parameter-list-contract.md
└── tasks.md
```

### Source Code (repository root)

```text
src/GameBot.Domain/
├── Parameters/
│   ├── ParameterDeclaration.cs        # add Multiple, MinItems, MaxItems
│   ├── ParameterBinding.cs            # add Values (ordered unique list)
│   ├── ParameterListRules.cs          # NEW: normalize, join, split, limits, read scalar as list
│   ├── ParameterValueRules.cs         # check each item of a list; limit check
│   ├── ParameterNameRules.cs          # declaration rules for multiple, minItems, maxItems, "|" in items
│   ├── ParameterScope.cs              # carry list-ness for the one-value field check
│   └── CommandStepResolver.cs         # fail a one-value field that gets 2+ items
└── Services/ParameterValidationService.cs   # empty list, limits, item warnings, binding-to-single warning

src/GameBot.Service/
├── Models/Commands.cs                 # ParameterDeclarationDto: multiple, minItems, maxItems; ParameterBindingDto: values
├── Endpoints/ParameterDtoMapper.cs    # map the new members both ways; normalize lists
├── Endpoints/QueueTemplatesEndpoints.cs, QueuesEndpoints.cs, SequencesEndpoints.cs  # pass the values member through
├── Contracts/QueueTemplates/*         # values on the entry DTOs
└── Swagger/                           # schema for the new members (examples and descriptions)

src/web-ui/src/components/parameters/
├── types.ts                           # multiple, minItems, maxItems; binding.values
├── fields/ImagePickerField.tsx        # multi-select mode
├── fields/ChoiceField.tsx             # multi-select mode
├── fields/ParameterValueField.tsx     # route by declaration.multiple
├── ParameterDeclarationCard.tsx       # author controls: multiple flag, limits
└── ParameterBindingForm.tsx           # read and write binding.values

tests/ (existing test projects under the repository)
docs/architecture.md, CHANGELOG.md, specs/STATUS.md
```

**Structure Decision**: Keep the existing layers. Put list logic in one new Domain class. Keep the scope engine text-based by storing the joined text in `Value`.

## Key design decisions

1. **Wire form.** `ParameterBindingDto` gets an optional `values` array. `value` stays. A request may send `value` (text, read as a list of one for a multi-value parameter) or `values`. A response for a list binding returns `values` and also `value` (the joined text), so an older client still sees a non-null value.
2. **Domain form.** `ParameterBinding.Values` holds the normalized list. When `Values` is set, `Value` is the items joined by `|`. A null `Values` and a null `Value` mean "inherit", as before. An empty `Values` list sets `Value` to null (not `""`), so an empty list means "no value". A multi-value `Default` may be the items joined by `|`.
3. **Normalization.** Remove duplicates, keep first occurrences, drop empty items, trim nothing else. This runs in the mapper on every write.
4. **Scalar read.** `ParameterListRules.ReadAsList(declaration, binding)` returns `[Value]` for a multi-value declaration when `Values` is null and `Value` is not null.
5. **Scope.** No change to the text model. A new check in the resolver finds a multi-value declaration with `TryFindDeclaration`, and counts items by splitting the text on `|`. Items cannot contain `|` for a multi-value parameter, so the count is exact.
6. **Validation.** Declaration errors are blocking. List value problems: empty required list and limit violations are errors for known values at save time and block the queue start. Not-allowed items stay warnings (code `invalid_parameter_value`).
7. **No new condition type.** The any-of image condition stays out of scope.

## Complexity Tracking

No violations to justify.
