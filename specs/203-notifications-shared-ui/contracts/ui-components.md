# Contract: New shared UI parts (feature 203)

All parts are in `src/web-ui/src/components/ui/` and are exported from `index.ts`. They use only theme tokens. Hover
rules are inside `@media (hover: hover)`. Class names start with `ui-`.

## Icon `send`

`ICON_PATHS.send` = the Material "send" path (`M2.01 21L23 12 2.01 3 2 10l15 2-15 2z`).

## SidePanel

`<SidePanel open title onClose mode? testId? children />`

- `mode`: `'desktop' | 'phone'`; default from `useLayoutMode()`.
- Nothing renders when `open` is false.
- Desktop: `<aside class="ui-side-panel" role="dialog" aria-modal="false" aria-labelledby="<id of the title>">`;
  `position: fixed; top: 0; right: 0; bottom: 0; width: var(--gb-size-side-panel)`; surface color; border on the
  left; shadow; `z-index` above the page and below the `ConfirmDialog`.
  - Header 56 px: the title (`<h2>`, 16 px, weight 700) and an `IconButton` (icon `close`, label "Close").
  - Body: `flex: 1; overflow-y: auto; padding: 16px 20px; background: var(--gb-color-bg)`.
  - A `.ui-footer-bar` that is a child of the body content stays at the bottom: the panel is a flex column, and the
    form inside is a flex column with the footer as its last child (`margin-top: auto`, `position: sticky; bottom: 0`).
- Phone: `<section class="ui-side-panel ui-side-panel--page" role="dialog" aria-modal="false" aria-labelledby>` in the
  page flow, full width. Header: an `IconButton` (icon `back`, label "Back") and the title. The footer is sticky at the
  bottom of the window.
- Open: the focus moves to the first focusable element of the body. If there is none, the focus moves to the panel
  (`tabIndex={-1}`).
- Escape (keydown inside the panel) calls `onClose`. The close and back buttons call `onClose`.
- Close (`open` changes to false, or unmount): the focus goes back to the element that had it at open, if it is still
  in the document.

## Toggle

`<Toggle checked onChange label id? disabled? />`

- `<span class="ui-toggle"><button type="button" role="switch" aria-checked aria-labelledby id class="ui-toggle__switch">
  <span class="ui-toggle__knob"/></button><span id="<id>-label" class="ui-toggle__label">label</span></span>`.
- Track 40 x 24 px, radius 12 px; off: `--gb-color-border-strong`; on: `--gb-color-primary`. Knob 18 px, white.
- A click on the switch or on the label calls `onChange(!checked)`. Space and Enter work (native button).
- Phone: the row is at least 44 px high, and the switch has a 44 x 44 px hit area.
- `disabled`: opacity 0.5, no change.

## SectionHeader

`<SectionHeader title actions? id? />`

- `<div class="ui-section-header"><h3 id>title</h3><div class="ui-section-header__actions">actions</div></div>`.
- `<h3>`: 18 px, weight 700. Margin-top 24 px, margin-bottom 12 px. Flex row, actions on the right.

## Form field classes

- `ui-form-card`: a card (surface, border, radius 10 px, padding 16 px 20 px, margin-bottom 12 px) with an `<h4>` of
  15 px.
- `ui-field`: margin-bottom 14 px. `ui-field__label`: block, 13 px, weight 600, margin-bottom 6 px.
- `ui-field__input`: width 100%, height 40 px, padding 0 12 px, border `--gb-color-border-strong`, radius 8 px, 14 px;
  16 px on the phone.
- `ui-field--error .ui-field__input`: border `--gb-color-danger`. `ui-field__error`: 12 px, `--gb-color-danger`,
  margin-top 4 px. `ui-field__help`: 12 px, muted.
