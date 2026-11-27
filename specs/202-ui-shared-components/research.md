# Research: Shared UI components and design tokens

All items of the Technical Context are known. This file records the design decisions.

## R1. Where the shared components go

- Decision: one folder `src/web-ui/src/components/ui/` with one CSS file `ui.css` and an `index.ts`.
- Rationale: one place for later PRs to import from; one CSS file keeps the tokens in one place.
- Alternatives considered: a CSS file for each component (more files, same result); a UI library (a new dependency, and
  the guideline has its own design).

## R2. The global button rule

- Decision: make the global `button` rule flat (no gradient, no lift, hover inside `@media (hover: hover)`), but keep its
  height of 44 px. The shared `Button` component has 40 px.
- Rationale: many pages that this PR does not move put `<button>` elements in rows and forms. A change of the height
  changes their layout and can break their tests. The flat look is the change that the user asked for on all pages.
  The height changes when each page moves to the shared `Button`.
- Alternatives considered: 40 px for all buttons now (risk of layout and test changes on 10 pages that are out of scope).

## R3. Row height with a fixed layout table

- Decision: set `height: 56px` on `<tr>`, `padding: 0 16px` and `vertical-align: middle` on `<td>`, and make sure that no
  cell content is higher than 56 px (icon buttons 36 px, chips 24 px, one-line text). Use `box-sizing: border-box`.
- Rationale: a table row grows when content is higher than the row; one-line content and fixed control sizes keep it
  at 56 px. The browser test checks it.
- Alternatives considered: CSS grid rows instead of a table (loses table semantics for screen readers).

## R4. Row click and accessibility

- Decision: a click handler on `<tr>`, plus an "open" button with the name text in the first cell. The action cell
  stops the click event.
- Rationale: a `<tr>` cannot have the focus in a correct way; the button gives keyboard and screen-reader access. The
  rule `jsx-a11y/click-events-have-key-events` is met because the keyboard path is the button.
- Alternatives considered: `role="link"` and `tabIndex` on `<tr>` (not correct table semantics).

## R5. The "⋮" menu position

- Decision: `position: fixed` from the button rectangle, upward when the space below is too small; close on scroll and
  resize.
- Rationale: the list card has `overflow: hidden` / `overflow-x: auto`, which cuts an absolute menu.
- Alternatives considered: a React portal with a positioning library (a new dependency).

## R6. Compatibility with the pages that are not moved

- Decision: `components/queues/IconButton.tsx` keeps its props (`icon`, `label`, `title`, `tone`, `size`, `disabled`,
  `ariaExpanded`, `onClick`) and its CSS classes (`icon-button`, `icon-button--touch`, `icon-button--destructive`), but
  takes its paths from `ui/icons.ts`. `features/execution/StatusChip.tsx` does not change in this PR.
- Rationale: 7 queue components and other pages use them. Their tests stay green with no change. A wrapper of the old
  chip would put the old and the new chip CSS on one element (analyze finding I3).
- Alternatives considered: change all users now (out of scope).

## R7. Phone row actions with the `reduced` level

- Decision: main action Start or Stop; menu with Open and the current pause action. No Edit, Duplicate, or Delete.
- Rationale: see the spec clarification. This keeps the phone level and the current phone tests for Start and Stop.

## R8. Browser tests

- Decision: a new desktop file `queues-list-layout.spec.ts` (project `chromium`) and a new phone file
  `queues-list-layout.mobile.spec.ts` (project `mobile`, because its name matches `mobile\.spec\.ts`). Both mock the API
  as `queues-actions-width.spec.ts` does, with 5 queues (one name of 200 characters, one running, one paused).
- Rationale: jsdom cannot measure layout.
