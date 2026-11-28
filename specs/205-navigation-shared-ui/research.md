# Research: Move the app navigation to the UI guidelines

## R1. Keep the area ids, change only the labels

- **Decision**: The ids `monitoring` and `notifications` stay. Only the labels change to "Monitor" and "Alerts".
- **Rationale**: The ids are in the addresses, in `phoneHistory`, in the paths, and in many tests. Old links must work (FR-002, SC-003).
- **Alternatives**: Rename the ids and add an alias map. Rejected: more risk, no user value.

## R2. Settings is a group, not an area

- **Decision**: Settings (desktop) and More (phone) are a group of the 5 current areas. A new id is added only for the phone More page (`more`). On the desktop `more` maps to `execution-logs`.
- **Rationale**: Each Settings page keeps its address and its own render function. The group module maps an area to its main tab with no change of the page code.
- **Alternatives**: One `settings` area with a sub-tab state, as Authoring has. Rejected: it changes 5 addresses and the history model.

## R3. The back arrow of a page opened from More

- **Decision**: The arrow calls `handleAreaChange('more')` (one new history entry with `pushState`).
- **Rationale**: The page can be the first entry (a direct address); `history.back()` would leave the web UI. The leave guard applies because `handleAreaChange` checks it.
- **Alternatives**: `history.back()` when it is not the first entry. Rejected: two behaviors for one control.

## R4. Dimmed More rows are not buttons

- **Decision**: The desktop-only rows are `div` elements with `aria-disabled="true"`, not buttons.
- **Rationale**: A tap must do nothing (FR-015). A disabled `button` has no focus and the 44 px phone rule for buttons does not apply; a `div` row of 56 px is the same height as the active rows. Screen readers read the name and "Use a computer".
- **Alternatives**: A disabled button. Rejected: the 44 px rule and the hover style of buttons make it look active.

## R5. Icons

- **Decision**: Copy the path data of the mockup symbols `queue`, `monitor`, `bell` (→ `alerts`), `build` (→ `authoring`), `list` (→ `logs`), `pkg` (→ `gamepacks`), and `sys` (→ `update`) from `docs/ui-guidelines/mockups/*.html` into `icons.ts`. `execution` uses the Material "play circle" path `M12 2a10 10 0 100 20 10 10 0 000-20zm-2 14.5v-9l6 4.5-6 4.5z`. The Settings tab uses the current `settings` icon; More uses `more`; the chevron uses `open`.
- **Rationale**: The user approved the mockups; the paths are Material style 24 px drawings, the same as the current set.

## R6. Desktop connection state

- **Decision**: Call `useConnectionState` with `enabled = true` on both layouts. The desktop app bar shows the state.
- **Rationale**: Rule N7. The hook already exists and has tests. The reload signal is useful on the desktop too (Monitoring uses it).
- **Risk**: Desktop unit tests that mock `fetch` now also see `GET /health` after 10 s. Most tests end before 10 s; tests with fake timers that count `fetch` calls must filter the `/health` calls. A task runs the full suite and fixes these tests.

## R7. `<main>` stays at the same child index

- **Decision**: The desktop renders one fragment (`AppBar` + optional `SubTabs`) before `<main>`, and the phone renders `PhoneHeader` before `<main>`. React compares the children by index, so `<main>` (index 1) does not remount on a mode change.
- **Rationale**: The current comment in `App.tsx` and the current tests require this.

## R8. Keyboard use of the sub-tabs

- **Decision**: Roving focus: the active tab has `tabIndex=0`, the others `-1`. Arrow keys, Home, and End move the focus; Enter and Space select.
- **Rationale**: The WAI-ARIA tabs pattern with manual selection: the operator can move the focus with no page change (a page change can start calls and the unsaved-changes check).
