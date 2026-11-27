# Contract: Shared UI components (`src/web-ui/src/components/ui/`)

All components use only CSS variables of the theme. All hover rules are inside `@media (hover: hover)`. All class
names start with `ui-`.

## Icon set (`icons.ts`, `Icon.tsx`)

- `ICON_PATHS`: a 24 x 24 SVG path for each name. Names: `play`, `pause`, `stop`, `edit`, `duplicate`, `delete`, `more`,
  `back`, `close`, `drag`, `expand`, `collapse`, `upload`, `download`, `search`, `filter`, `add`, and the old names
  `undo`, `runNext`, `parameters`, `settings`, `remove`, `cancel`, `save`, `reload`, `link`, `unlink`.
- `IconName` = keys of `ICON_PATHS`. `ICON_NAMES` = the list of names.
- `<Icon name size? />`: an `<svg>` with `viewBox="0 0 24 24"`, `aria-hidden="true"`, `focusable="false"`, default size 20.

## Button

`<Button variant="primary|secondary|danger-outline|danger" icon? type? disabled? onClick>text</Button>`
- 40 px high, radius 8 px, font 14 px weight 600, padding 0 16 px. Class `ui-button ui-button--<variant>`.
- Off state: opacity 0.5, `cursor: not-allowed`. No movement on hover.

## IconButton

`<IconButton icon label title? tone="default|danger" size="default|touch" disabled? ariaExpanded? ariaHasPopup? onClick />`
- `label` is required (the `aria-label`). `title` is the tooltip; default is `label`.
- 36 x 36 px (`default`) or 44 x 44 px (`touch`); in the phone layout, `default` also has a 44 x 44 px size.
- No border. Icon 20 px, muted color. Hover (pointer only): primary-soft background, primary color. `danger`: red icon.
- Class `ui-icon-button`, `ui-icon-button--danger`, `ui-icon-button--touch`.

## PageHeader

`<PageHeader title onBack? status?={ChipState and text} actions? />`
- One row of 44 px; the title is `<h2>` (22 px desktop, 20 px phone); the back button is an `IconButton` with icon
  `back` and label "Back"; `actions` are on the right.

## StatusChip

`<StatusChip state text? />`
- A dot and one word. Desktop: 96 x 24 px, text centered. Phone: 20 px high, width from the text.
- Class `ui-chip ui-chip--<state>`, `role="status"`, `aria-label="Status: <text>"`.

## ListView<T>

`<ListView ariaLabel columns rows getRowKey onRowClick? rowActions? renderActions? renderPhoneActions? actionsColumn? menuLabel? openLabel? state? phone? mode? tableClassName? rowClassName? rowTestId? />`
- `mode`: `'desktop' | 'phone'`; default from `useLayoutMode()`.
- `rowActions(row)`: gives `RowActions` (visible actions and menu). `renderActions(row)` (desktop) and
  `renderPhoneActions(row)` (phone) are alternatives for a caller that needs a hook for each row; the caller then
  renders `IconButton` and `RowActionMenu` itself. When both are given, the render props win.
- `actionsColumn`: `{ header: string; width: number }`. It is required when the list has actions on the desktop.
- `menuLabel(row)`: the accessible name of "⋮". Default "More actions".
- `openLabel(row)`: the accessible name of the open button. Default "Open <phone title or first column text>".
- `ListColumn.cellTestId`: an optional `data-testid` for each cell of the column.
- `state.empty.text`: the empty text of the page. Default "No items.".
- Desktop:
  - `<table class="ui-list__table">` in `<div class="ui-list">` (card, radius 10 px, `overflow-x: auto`).
  - `<colgroup>` with the widths; `table-layout: fixed`; `min-width` = sum of fixed widths + 160 px.
  - Header row 40 px. Data row 56 px. Cells: one line, "…", `title` from `column.text`. Empty text shows "—".
  - When `rowActions` is set, the last column is the actions column. Its width is given by the caller.
  - Row click calls `onRowClick(row)`. The first cell holds a button `ui-list__open` for keyboard use. Clicks in the
    actions cell do not call `onRowClick`.
- Phone:
  - `<ul class="ui-list__phone">`; each `<li>` is 64 px high.
  - `phone.title(row)` on line 1 (15 px, weight 600, "…"); `phone.chip(row)` and `phone.meta(row)` on line 2.
  - On the right: `rowActions(row).visible[0]` as a touch `IconButton`, and the "⋮" `RowActionMenu` when `menu` is set.
  - A tap on the open area calls `onRowClick(row)`.
- `state`: shows one row (desktop: one cell over all columns; phone: one `<li>`) with "Loading…", the empty text, or the
  error text and a **Try again** `Button` (secondary).

## RowActionMenu

`<RowActionMenu label actions size? />`
- An `IconButton` with icon `more`, `aria-haspopup="menu"`, `aria-expanded`.
- The menu: `role="menu"`, items `role="menuitem"` (44 px high, icon and text). A `danger` item is red and is last.
  A `disabled` item is dimmed and cannot be selected.
- Open: the focus moves to the first item that is on. Up and Down move the focus. Enter and a click select. Escape
  closes and gives the focus back to the button. Tab and a click outside close the menu. Scroll and resize close it.
- `position: fixed`; opens upward when the space below is too small.
- Classes: `ui-menu` on the list, `ui-menu--up` when it opens upward, `ui-menu__item` and `ui-menu__item--danger` on
  the items.

## FooterBar

`<FooterBar left? right />` — a bar of 68 px, surface color, border on the top, `position: sticky; bottom: 0`.

## ConfirmDialog

`<ConfirmDialog open title message confirmText cancelText? danger? busy? onConfirm onCancel />`
- `role="dialog"`, `aria-modal="true"`, `aria-labelledby` = the title.
- The focus starts on **Cancel** (`cancelText`, default "Cancel"). Escape calls `onCancel`. The focus goes back on close.
- The confirm `Button` has the variant `danger` when `danger` is true, else `primary`. `busy` turns both buttons off.
