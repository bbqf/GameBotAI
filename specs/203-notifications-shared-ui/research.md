# Research: Move the Notifications area to the shared UI components

## R1. Side panel without a backdrop

- Decision: the desktop side panel has no backdrop and `aria-modal="false"`. The list stays usable, so a click on another
  row changes the target in the panel (spec clarification 3).
- Rationale: rule D2 says that the list stays visible. A modal panel would block the row click.
- Alternatives considered: a modal panel with a backdrop (blocks the list); an inline form under the list (the current
  layout, which the guideline replaces).

## R2. The footer inside the form

- Decision: `TargetForm` renders the body and the `FooterBar` inside one `<form>`. `SidePanel` gets the whole form as
  `children` and has a layout where the last child (`.ui-footer-bar`) stays at the bottom (flex column, body scrolls).
- Rationale: **Save** stays a submit button, so Enter in a field saves, as now.
- Alternatives considered: the footer as a `footer` prop of `SidePanel` with `form="target-form"` on the buttons (works,
  but splits the form logic).

## R3. Phone page of the form

- Decision: `SidePanel` with `mode="phone"` renders a full page in the normal page flow. The page hides the header, the
  guide, and the lists while the editor is open. No history entry (spec clarification 5).
- Rationale: one component for both layouts, as the guideline says (rule D2: "On the phone the same form is a full page").

## R4. A select in a fixed row

- Decision: the level cell renders a native select of 40 px. On the phone, the select is the right part of the row
  through `renderPhoneActions`.
- Rationale: a native select fits the 56 px and 64 px rows and works with touch. The guideline gets the exception in
  rule F6.

## R5. Field checks

- Decision: check on submit; show the error under the field; clear the error of a field on its change. Trim the values.
- Rationale: rule F5. A check on each key press shows errors too early.

## R6. Browser tests

- Decision: one mock helper `tests/e2e/helpers/notificationsMock.ts` for `/api/notification-targets` (list, create,
  update, delete, test) and `/api/queues` (list, level). The desktop and the phone files use it.
- Rationale: the same pattern as `helpers/queuesMock.ts` of feature 202.
