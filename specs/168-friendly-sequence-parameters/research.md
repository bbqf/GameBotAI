# Research: Friendly Sequence Parameters

All unknowns of the Technical Context are resolved below.

## Decision 1: Where the friendly fields live

- **Decision**: Add optional members to `ParameterDeclaration` (domain) and `ParameterDeclarationDto` (wire): `label`, `helpText`, `kind`, `options`, `min`, `max`, `unit`, `pattern`.
- **Rationale**: One declaration model serves commands and sequences (spec Q4). The mapper already serves three endpoint groups. Old files have none of the new members, so they load with no change (FR-018).
- **Alternatives**: A separate "presentation" object next to each declaration. Rejected: it needs a second key and can drift from the declaration.

## Decision 2: What a binding stores

- **Decision**: A binding keeps the technical value (spec FR-014). No new member in `ParameterBinding`.
- **Rationale**: Run path, API clients, and stored templates stay the same. The form finds the label by matching the value to `options`.
- **Alternatives**: Store an option ID. Rejected in the clarification (spec Q1).

## Decision 3: Where the number pattern is applied

- **Decision**: `ParameterScope.TryResolve` applies the pattern of the innermost declaration with that name. The result is marked `Mapped` in `ParameterValue`, so a whole-placeholder binding (feature 115) does not apply the pattern a second time.
- **Rationale**: Only the scope knows both the value and the declaration. One place covers every consumer (step fields, conditions, binding values, `ToSubstitutionContext`).
- **Alternatives**: (a) Apply the pattern when the form saves. Rejected: the stored value would no longer be the number, and the range check would break. (b) Apply it in each consumer. Rejected: many call sites, high drift risk.
- **Note**: The default of a pattern parameter is the plain number, as for any number. The pattern applies to it too.

## Decision 4: Image source (spec conflict)

- **Finding**: The first spec text said the picker shows the images of the linked game. The data model has no link from a game to an image. `queue.gameId` is only a text value.
- **Decision (user, 2026-10-29)**: The author lists the allowed image IDs on the declaration (`allowedImages`). The value form shows only these images. The author chooses them in the editor from `GET /api/images`, with previews.
- **Rationale**: It needs no new link or migration. The author knows which images fit the sequence. The person sees a short and safe list.
- **Alternatives**: (a) Show all images. Rejected by the user. (b) Add a game field to each image. Rejected: new data, a migration, and a change to gamepack files.
- **Consequence**: A gamepack that carries a sequence with an image parameter must also carry the allowed images, or the declaration warns that an ID has no stored image. The gamepack install check for image references (feature 143) covers the IDs in `allowedImages` as references. Task: check that rule.

## Decision 5: Where value checks run

- **Decision**: The service checks a value against its declaration when it saves a queue-template entry, a sequence step binding, or a command step binding. It checks choice membership, range, whole number, allowed image, and image existence. A mismatch gives a **warning**, not an error (analysis fix, user choice "warn only"). It does not check at run start (spec FR-016). The UI form uses the same rules and does not accept a new invalid value while the person edits (spec FR-012). A value that is a `{{name}}` placeholder is not checked (spec FR-028).
- **Rationale**: A declaration can change after a value was saved. A stored value that no longer fits must not block later saves of the entry.
- **Alternatives**: Refuse the save (rejected: one stale entry blocks all edits of a template). Refuse only changed values (rejected: needs the old value; more code).
- **Risk**: An API client can save a value that does not fit. The step fails at run time and the log shows the value. The spec lists this in Assumptions.

## Decision 5a: Plain-text values with `{{` and pass-through bindings

- **Decision (braces)**: An option value or a pattern may contain `{{` inside longer text. An option value that is exactly one placeholder is refused at declaration save, because it collides with a pass-through binding. `ParameterValue.Mapped` (decision 3) marks a pattern result. A binding value that equals an option value of the callee declaration is also marked plain text, so `TryBindChild` does not resolve it. The scope gets the callee declarations for this check.
- **Decision (pass-through)**: The step form offers "Use a parameter of this sequence" with the parameters of the same type. The binding stores `{{name}}`, which feature 115 already resolves. The value check skips such a value.
- **Decision (numeric fields)**: `ParameterValidationService.ValidateCommand` refuses a text-typed parameter (text choice, or number with a pattern) in a numeric field. It uses the numeric field list of `CommandStepFieldPaths`.
- **Alternatives**: Refuse `{{` in declarations (simpler; rejected by the user).

## Decision 6: Execution log

- **Decision**: The log entry of a resolved parameter keeps the technical value. A new `label` member shows the option label when the declaration is a choice. Image and number show the technical value only. Names that match the secret-name rule stay masked.
- **Rationale**: FR-017. The log code already carries `ResolvedParameter` (name, value, origin). One optional member is enough.
- **Alternatives**: Show the label only. Rejected: diagnosis needs the technical value.

## Decision 7: Phone

- **Finding**: No phone screen lets a person edit values today. The queue detail on a phone is read-only. Execution is hidden on a phone.
- **Decision**: Build the value form so it works at 375 px (single column, touch-size controls, no horizontal scroll). Show "Use a larger screen" in the declaration editor on a phone. Add no new phone screen. Test the form in the phone layout mode.
- **Rationale**: This keeps the phone level **reduced** with no new scope.
- **Open**: Spec user story 5 says a person can fill values "on a phone". It is true for the component, not yet for a phone screen. The user can ask for a phone screen as a later feature.

## Decision 8: UI presentation

- **Decision**: Use these controls.
  - Image (person): a grid of thumbnails of the allowed images, with names. A search box shows for more than 12 images. The selected image shows large.
  - Image (author): an "Allowed images" list with thumbnails and a "Choose images" dialog that lists all stored images, grouped by owner, with search and check boxes.
  - Choice: radio-style buttons for 6 options or fewer; a drop-down with search for more than 6; a search box for more than 12 (FR-008).
  - Number: a number field with the unit label, the range hint, and the error text. The author sees a live "technical result" line; the person does not.
  - Editor: one card per parameter with kind settings and a live preview of the person's form. The name is suggested from the label (slug of the label, valid identifier, unique).
- **Rationale**: Matches the suggestions given to the user in the specify step. All controls are keyboard-operable and named (FR-025).
- **Alternatives**: A wizard dialog. Rejected: slower for authors who add many parameters.

## Decision 9: Thumbnails

- **Decision**: Load a thumbnail with the existing `GET /api/images/{id}` call when the tile scrolls into view (lazy loading). Cache blob URLs for the life of the form.
- **Rationale**: No new endpoint. Lazy loading keeps a 200-image grid fast.
- **Alternatives**: A new thumbnail endpoint. Rejected for now; add it if measurements show a problem.
