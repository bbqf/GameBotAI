# Research: List-valued sequence parameters

## Decision 1: Keep the scope engine text-based

- **Decision**: A list binding keeps `Value` as the items joined by `|`. A new `Values` member holds the list.
- **Rationale**: `ParameterScope`, `TemplateSubstitutor`, pass-through bindings, and defaults all use text. The text join gives the defined `{{name}}` result (spec FR-011) with no change to them.
- **Alternatives considered**: (a) Make `ParameterValue` carry a list. This changes about 18 files and every consumer. (b) Store a JSON array in `Value`. This breaks text substitution and old readers.

## Decision 2: Separator is the pipe character

- **Decision**: Join with `|`. Refuse a multi-value declaration with an item that holds `|`.
- **Rationale**: `|` is the alternation operator of regular expressions, so a joined list works in `ocrText.pattern`. The refusal makes the item count exact.
- **Alternatives considered**: Comma (common in ids). Newline (hard in single-line fields).

## Decision 3: Wire shape

- **Decision**: Add `values` (array) to the binding DTO. Keep `value`.
- **Rationale**: Old clients and old stored data keep working. A scalar `value` is read as a list of one (spec FR-007, FR-013a).
- **Alternatives considered**: Change `value` to a union type. Swagger and the existing clients do not handle it well.

## Decision 4: One-value field with several items

- **Decision**: Fail the step with a message that names the parameter, the field, and the count.
- **Rationale**: Use of the first item would hide a wrong choice. The failure is visible and early.
- **Alternatives considered**: Use the first item. Run the step once for each item. Both are outside the scope.

## Decision 5: Where each check runs

- **Decision**: Declaration rules in `ParameterNameRules`. Value rules in `ParameterValueRules` and `ParameterValidationService`. The one-value field check in `CommandStepResolver`.
- **Rationale**: These classes already own the same kind of rule for single values.

## Decision 6: Web UI

- **Decision**: Reuse `ImagePickerField` and `ChoiceField`. Add a multi-select mode that toggles items. Author controls for the flag and limits go in `ParameterDeclarationCard`.
- **Rationale**: Same look and same phone behavior as the single-value form (docs/mobile-ui-principles.md).

## Unknowns

No `NEEDS CLARIFICATION` item remains.
