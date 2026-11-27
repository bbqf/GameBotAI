# Implementation Plan: Shared UI components and design tokens, with the Queues page as the first user

**Branch**: `202-ui-shared-components` | **Date**: 2026-11-27 | **Spec**: [spec.md](spec.md)
**Input**: Feature specification from `specs/202-ui-shared-components/spec.md`

## Summary

Add the design tokens of `docs/ui-guidelines.md` (sections 1 and 2) to the theme, make the global button style flat, and
remove the web font. Add a set of shared React components in `src/web-ui/src/components/ui/` (Icon, Button, IconButton,
PageHeader, StatusChip, ListView, RowActionMenu, FooterBar, ConfirmDialog). Move the desktop Queues table and the phone
Queues list to `ListView`, with fixed column widths, fixed row heights, three fixed row icons, and a "⋮" menu. The old
`IconButton` keeps its look and takes its paths from the shared icon set. The old `StatusChip` does not change. Add browser tests that measure the
column widths and the row heights.

## Technical Context

**Language/Version**: TypeScript 5.6, React 18.3  
**Primary Dependencies**: Vite 8, no UI library; `@dnd-kit` (not used by this feature)  
**Storage**: N/A  
**Testing**: Jest 29 with jsdom and Testing Library (unit tests in `src/**/__tests__`); Playwright 1.49 (`tests/e2e`, projects `chromium` at 1280 px and `mobile` at 360 x 640 px, the `mobile` project runs files that match `mobile\.spec\.ts`)  
**Target Platform**: Current Chrome, Edge, and Safari on desktop; Chrome and Safari on phones  
**Project Type**: Web application (frontend only: `src/web-ui`)  
**Performance Goals**: The Queues list with 50 queues shows in less than 200 ms after the data arrives (same as now). The CSS of the shared components is less than 15 KB (not minified). No new runtime dependency.  
**Constraints**: No layout shift when the data changes (SC-002). No sideways scroll at 320 px. Tap targets of 44 x 44 px on the phone. No backend change.  
**Scale/Scope**: 9 shared components, 1 page (2 layouts), about 25 changed or new source files.

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

*NON-NEGOTIABLE*: If `build` or required `test` runs are failing (local or CI), implementation progression is blocked until failures are fixed or a documented maintainer waiver exists.

*NON-NEGOTIABLE*: All text in this plan and in the artifacts it produces (research, data model, contracts, quickstart, tasks, code comments, user-facing messages) MUST obey Simplified Technical English (Constitution Principle VI).

*Phone check (Constitution Principle III)*: Queues keeps the phone support level `reduced`. The shared components support both layouts. The feature follows `docs/mobile-ui-principles.md`; `docs/ui-guidelines.md` replaces its rules 5 and 11, and the change of `mobile-ui-principles.md` records this.

| Gate | Status | Note |
|------|--------|------|
| I. Code quality | Pass | ESLint (with `jsx-a11y`) must pass. Each component is small and has one task. No dead code: the old icon paths move, they are not copied. |
| II. Testing | Pass | A unit test for each shared component; changed unit tests for the Queues list; new Playwright tests for widths and heights (desktop and phone). |
| III. UX consistency | Pass | This feature is the UX consistency work. Phone level stated. |
| IV. Performance | Pass | Goals above. No hot path changes. No new dependency. |
| V. Living documentation | Pass | `docs/ui-guidelines.md` is new. `docs/architecture.md` has no UI component section, so it does not change (check in task). `specs/STATUS.md` gets the new line. |
| VI. STE | Pass | All new text in STE. UI text: "Create queue", "Delete queue <name>?", "No items.", "Try again". |
| Version bump | Pass | Done in phase 2 before the push. |

## Project Structure

### Documentation (this feature)

```text
specs/202-ui-shared-components/
├── plan.md
├── research.md
├── data-model.md
├── quickstart.md
├── contracts/
│   ├── ui-components.md      # props and behavior of each shared component
│   └── queues-list-layout.md # column widths, row heights, action positions of the Queues list
└── tasks.md
```

### Source Code

```text
docs/
├── ui-guidelines.md                    # new (already written)
├── ui-guidelines/                      # new: 3 PNG images + mockups/ (HTML sources)
└── mobile-ui-principles.md             # changed: link to the guideline

src/web-ui/src/
├── theme/
│   ├── tokens.ts                       # changed: new tokens, system font
│   └── global.css                      # changed: same tokens, no font @import, --gb-color-text-muted
├── styles.css                          # changed: flat global button, hover inside @media (hover: hover)
├── components/
│   ├── ui/                             # new
│   │   ├── icons.ts                    # one icon set (paths), IconName type
│   │   ├── Icon.tsx
│   │   ├── Button.tsx
│   │   ├── IconButton.tsx
│   │   ├── PageHeader.tsx
│   │   ├── StatusChip.tsx
│   │   ├── ListView.tsx
│   │   ├── RowActionMenu.tsx
│   │   ├── FooterBar.tsx
│   │   ├── ConfirmDialog.tsx
│   │   ├── ui.css                      # styles of all shared components, tokens only
│   │   ├── index.ts
│   │   └── __tests__/                  # one test file for each component
│   └── queues/
│       ├── IconButton.tsx              # changed: wrapper of ui/IconButton, same props
│       ├── QueueRowActions.tsx         # changed: 3 fixed icons + RowActionMenu
│       ├── queueStatus.ts              # new: queue -> chip state and text
│       └── PhoneQueuesView.tsx         # changed: list uses ListView (phone mode)
└── pages/QueuesPage.tsx                # changed: PageHeader, ListView, ConfirmDialog for Delete

src/web-ui/tests/e2e/
├── queues-actions-width.spec.ts        # changed: new selectors and widths
├── queues-list-layout.spec.ts          # new: desktop widths and heights
└── queues-list-layout.mobile.spec.ts   # new: phone row heights and sideways scroll
```

**Structure Decision**: Frontend only. The shared components go in one new folder `components/ui/` with one CSS file.
The Queues components stay in `components/queues/`.

## Design

### Tokens (FR-001 to FR-004)

- `tokens.ts` gets new groups `size` (`appBar`, `control` 40 px, `iconButton` 36 px, `iconButtonTouch` 44 px, `icon`
  20 px, `row` 56 px, `rowPhone` 64 px, `headerRow` 40 px, `keyValueRow` 40 px, `footer` 68 px, `sidePanel` 480 px,
  `formWidth` 720 px, `chipWidth` 96 px, `chipHeight` 24 px, `thumbnail` 40 px) and new colors (`dangerSoft`,
  `successSoft`, `warningSoft`, `idle`, `idleSoft`, `loop`, `ifBlock`). Each new value gets a CSS variable in `cssVarMap`
  and in `global.css`. A unit test checks that each variable of `cssVarMap` is in `global.css` with the same value.
- `--gb-color-text-muted` is an alias of `--gb-color-muted`.
- The font is `'Segoe UI', system-ui, -apple-system, Roboto, sans-serif`. The `@import` of Google Fonts goes away. The
  font mock in the e2e tests can stay (it does no harm).
- The global `button` rule in `styles.css`: background `--gb-color-surface`, no gradient, no `transform`, no shadow on
  hover; the hover rule moves into `@media (hover: hover)`. The height stays 44 px for pages that are not moved (see
  research R2).

### Shared components

The props and the behavior are in [contracts/ui-components.md](contracts/ui-components.md). Main points:

- `ListView<T>` takes `columns` (header, width in px or `'flex'`, align, render), `rows`, `getRowKey`, `onRowClick`,
  `rowActions` (a function that gives the visible actions and the menu actions for a row), `state`
  (`loading` / `empty` / `error` with `onRetry`), and `phone` (title, chip, meta, main action) for the phone mode. It reads
  `useLayoutMode()` to choose the layout. A `mode` prop can force the layout (for tests).
- Desktop: `<table class="ui-list__table">` with `<colgroup>`; `table-layout: fixed`; each `<td>` has
  `white-space: nowrap; overflow: hidden; text-overflow: ellipsis` and a `title` with the text value; row height by
  `height: var(--gb-size-row)` on `<tr>` and a fixed content height in the cell (icons 36 px). The flexible column has
  `min-width` by a table `min-width` (sum of fixed widths + 160 px); the card has `overflow-x: auto`.
- Row click: the `<tr>` has `onClick`; the action cell stops the event (`stopPropagation`). For the keyboard, the first
  cell has a button (styled as text, `ui-list__open`) with the name, so that Tab and Enter work and `jsx-a11y` passes.
- Phone: `<ul class="ui-list__phone">` with `<li>` of 64 px; the open area is a button (title and line 2); the main
  action is an `IconButton` with `size="touch"`; the menu is `RowActionMenu`.
- Actions do not move: `rowActions` gives always the same number of visible slots for a list; an action that does not
  apply has `disabled: true`.
- `RowActionMenu`: the menu is `position: fixed`, placed from the `getBoundingClientRect()` of the button; it opens upward
  when the space below is less than the menu height. Keyboard rules from the spec clarification.
- `ConfirmDialog`: `role="dialog"`, `aria-modal`, `aria-labelledby`; focus on **Cancel** at open; Escape calls
  `onCancel`; the focus goes back to the element that had it before.
- Hover rules of all shared components are inside `@media (hover: hover)`.

### Queues page

The layout values are in [contracts/queues-list-layout.md](contracts/queues-list-layout.md).

- `QueuesPage`: `PageHeader` (title "Queues", primary button "Create queue"); `ListView` with the five columns; the old
  `table.queues-table` class stays on the table element so that the old selectors in tests still find it; the row keeps
  the class `queues-row`.
- `QueueRowActions` changes to a function `queueRowActions(queue, handlers, busy)` that gives the 3 visible actions and
  the 2 menu actions, plus the current component that renders them (for its unit tests). The pause calls and the busy
  state stay in a small hook `useQueuePauseCalls`.
- Delete: a new state `deleteTarget: { id, name }`. The menu action sets it. `ConfirmDialog` (danger) calls
  `deleteQueue(id)` with the same messages and the same 409 text as now. The old `ConfirmDeleteModal` stays for the
  Delete button inside the edit form.
- `queueStatus.ts`: `queueChipState(queue)` gives `running` / `paused` / `stopped` / `unknown` and the text.
- `PhoneQueuesView`: the list uses `ListView` with `mode` from the layout; main action Start or Stop (the same
  `toggleRun`); menu: Open, and for a running queue the pause action (from `useQueuePauseCalls`). The "Old data" mark
  and the "Not live" mark stay in line 2. `data-testid="queue-card-<id>"` stays on the `<li>`.

## Complexity Tracking

No constitution violations.
