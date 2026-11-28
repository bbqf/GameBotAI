# Research: Move the Commands and Games pages to the shared UI components

## R1. Card style only on the migrated forms

- **Decision**: A wrapper class `ui-form` gives the card look to `FormSection` and the field styles. Only the form pages of this feature use it.
- **Rationale**: `FormSection` is also used by Sequences and Triggers. A change of `FormSection` itself would change pages that are not part of this PR.
- **Alternatives**: Change `FormSection` for all pages. Rejected: out of scope; Sequences comes in the next PR.

## R2. One delete flow for both pages

- **Decision**: A hook `useDeleteFlow` and a `DeleteReferences` view in `components/authoring/`.
- **Rationale**: Both pages have the same flow (open, confirm, 409 references, other error). One hook removes the copy and is easy to test. The next PRs (Sequences, Images) can use it too.

## R3. `confirmDisabled` on `ConfirmDialog`

- **Decision**: A new optional prop that turns off only the confirm button.
- **Rationale**: After a 409 the delete cannot succeed, but the operator must close the dialog. `busy` turns off both buttons.

## R4. The footer bar of the command form

- **Decision**: `CommandForm` renders `FooterBar` inside its `<form>`, with `onDelete` and `deleting`.
- **Rationale**: Save must stay a submit button of the form (Enter in a field submits; the tests use the submit). The page cannot put a submit button inside the form of a child component without a `form` attribute and an id.

## R5. The Game column of Commands

- **Decision**: Remove it and the disabled Game filter.
- **Rationale**: The command data has no game; the column always shows "—" and the filter is always off. Rule L2 asks for useful fixed columns; an always-empty column is noise.
