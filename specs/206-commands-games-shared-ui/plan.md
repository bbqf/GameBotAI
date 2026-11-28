# Implementation Plan: Move the Commands and Games pages to the shared UI components

**Branch**: `206-commands-games-shared-ui` | **Date**: 2026-11-28 | **Spec**: [spec.md](spec.md)
**Input**: Feature specification from `specs/206-commands-games-shared-ui/spec.md`

## Summary

Move the Games and Commands pages to `PageHeader`, a filter bar, `ListView` with Edit and a "⋮" menu, form pages with a
back arrow, a 720 px card form, and `FooterBar`, and the shared `ConfirmDialog` for Delete (with a new
`confirmDisabled` prop and the 409 references in `details`). A small shared hook holds the delete flow of both pages.
`CommandForm` renders the footer bar. The old buttons, tables, and the old delete modal go away from these pages.

## Technical Context

**Language/Version**: TypeScript 5.6, React 18.3  
**Primary Dependencies**: Vite 8; shared components in `src/web-ui/src/components/ui/` (features 202 to 205); no new dependency  
**Storage**: N/A  
**Testing**: Jest 29 + Testing Library (`src/pages/__tests__/GamesPage.spec.tsx`, `CommandsPage.*.spec.tsx`, `commandsPageTestSetup.tsx`, `CommandsAndSequencesPrimitiveAuthoring.spec.tsx`, `src/components/commands/__tests__/`, `VisualStepPicker` tests); Playwright 1.49 (project `chromium`, 1280 px)  
**Target Platform**: Current desktop browsers (Authoring is desktop-only on the phone)  
**Project Type**: Web application (frontend only: `src/web-ui`)  
**Performance Goals**: The list shows less than 200 ms after the data arrives; the edit page opens with the same single `get` call as now; no new call.  
**Constraints**: No layout shift when the data changes. Tokens only in CSS. Hover rules only inside `@media (hover: hover)`. No backend change. No change of the payloads.  
**Scale/Scope**: 2 pages, 1 form component (`CommandForm`) and the step picker buttons, 1 shared component change (`ConfirmDialog`), 1 new hook, about 10 changed test files, 1 new browser test file.

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

*NON-NEGOTIABLE*: If `build` or required `test` runs are failing (local or CI), implementation progression is blocked until failures are fixed or a documented maintainer waiver exists.

*NON-NEGOTIABLE*: All text in this plan and in the artifacts it produces (research, data model, contracts, quickstart, tasks, code comments, user-facing messages) MUST obey Simplified Technical English (Constitution Principle VI).

*Phone check (Constitution Principle III)*: Authoring keeps the phone support level `desktop-only`. The phone does not render these pages. The feature follows `docs/ui-guidelines.md`.

| Gate | Status | Note |
|------|--------|------|
| I. Code quality | Pass | ESLint with `jsx-a11y`. One delete hook for both pages (no copy). The old table CSS goes away. |
| II. Testing | Pass | Changed page and form tests; new tests for the hook, `confirmDisabled`, and the 409 flow; one new browser test file. |
| III. UX consistency | Pass | This is the UX consistency work. Phone level stated. |
| IV. Performance | Pass | No new call. |
| V. Living documentation | Pass | `docs/ui-guidelines.md` section 13 (`ConfirmDialog.confirmDisabled`, the `ui-form` card style) and "Last reviewed"; `specs/STATUS.md` line 206; `docs/architecture.md` check in a task. |
| VI. STE | Pass | New UI text: "Create game", "Create command", "New game", "New command", "Delete game <name>?", "You cannot undo this.", "Referenced by:", "No games.", "No commands.", "Game created." and the other messages. |
| Version bump | Pass | Done in phase 2 before the push. |

## Project Structure

### Documentation (this feature)

```text
specs/206-commands-games-shared-ui/
├── plan.md
├── research.md
├── data-model.md
├── quickstart.md
├── contracts/commands-games-layout.md
└── tasks.md
```

### Source Code

```text
docs/ui-guidelines.md                                   # section 13, Last reviewed

src/web-ui/src/
├── components/ui/ConfirmDialog.tsx                     # confirmDisabled
├── components/ui/__tests__/ConfirmDialog.test.tsx      # confirmDisabled test
├── components/ui/ui.css                                # ui-form, ui-filter-bar, ui-message-area, ui-references
├── components/authoring/useDeleteFlow.ts               # new: dialog state, 409 references, call
├── components/authoring/DeleteReferences.tsx           # new: "Referenced by:" groups for the dialog details
├── components/authoring/__tests__/useDeleteFlow.test.tsx       # new
├── components/commands/CommandForm.tsx                 # FooterBar, onDelete, deleting; Button
├── components/commands/VisualStepPicker/*.tsx          # Button instead of btn classes
├── pages/GamesPage.tsx                                 # list page + form page
├── pages/CommandsPage.tsx                              # list page + form page
├── pages/CommandsPage.css                              # remove the commands-table rules
└── pages/__tests__/*.spec.tsx                          # changed
src/web-ui/tests/e2e/
├── helpers/authoringMock.ts                            # new: games and commands routes
└── authoring-commands-games-layout.spec.ts             # new (chromium)
```

## Design

### Shared changes

- `ConfirmDialog`: new optional `confirmDisabled?: boolean`. The confirm button is off when `busy || confirmDisabled`.
  Cancel is off only when `busy`.
- `ui.css` (tokens only):
  - `.ui-filter-bar` (flex, gap 8 px, margin 0 0 12 px; its input 40 px high, width 280 px, max-width 100%).
  - `.ui-message-area` (flex column, gap 8 px, margin 0 0 12 px).
  - `.ui-form` (max-width 720 px, margin 0 auto, width 100%). Inside it: `.form-section` is a card (surface, 1 px
    border, radius 10 px, padding 16 px 20px, margin 0 0 16 px); `.form-section__header h3` 15 px 600 margin 0 0 4 px;
    `.form-section__description` muted 13 px; `.field` flex column, gap 4 px, margin 0 0 12 px; `label` 13 px 600;
    inputs and selects 40 px high, width 100%; `[aria-invalid="true"]` border color danger; `.field-error` danger
    13 px. The class is only on the form pages of this feature, so the other pages that use `FormSection` do not
    change.
  - `.ui-references` (margin 12 px 0 0, 13 px; group name 600; list padding-left 20 px).
  - `.ui-footer-bar` stays as it is (sticky at the bottom, feature 202).

### Delete flow (`components/authoring/useDeleteFlow.ts`)

```ts
type DeleteTarget = { id: string; name: string };
useDeleteFlow({ remove: (id) => Promise<void>, onDeleted: (target) => void, onError: (message) => void })
  → { target, open(target), cancel(), confirm(), busy, conflict?: { message: string; references?: Record<string, {id,name}[]> } }
```

- `open` sets the target and clears the conflict. `confirm` sets `busy`, calls `remove`; on success it closes and
  calls `onDeleted`; on `ApiError` 409 it keeps the dialog open and sets `conflict` (message = the API message, or the
  default "Cannot delete: the <kind> is in use. Remove the links first."); on another error it closes and calls
  `onError(err.message ?? 'Failed to delete <kind>')`. A second `confirm` while `busy` does nothing.
- `DeleteReferences` renders the conflict: `<p role="alert">{message}</p>` and, when references exist,
  `<div className="ui-references"><strong>Referenced by:</strong>` and for each group `<div>` with
  `"{key} ({items.length})"` and `<ul>` with the names (the id in a muted span, as now).
- The page renders `<ConfirmDialog open={!!target} title={`Delete game ${target.name}?`} message="You cannot undo this."
  details={conflict && <DeleteReferences … />} confirmText="Delete" danger busy={busy}
  confirmDisabled={!!conflict} onConfirm={confirm} onCancel={cancel} />`.

### Games page

- List page (`!editorOpen`): `PageHeader title="Games" actions={<Button variant="primary" icon="add">Create game</Button>}`;
  `.ui-message-area` with the success message (`role="status"`) and the list error (`role="alert"`) only when the list
  has data; `.ui-filter-bar` with `<input aria-label="Filter by name" placeholder="Filter by name">`; `ListView` with
  `ariaLabel="Games table"`, `tableClassName="games-list"`, columns Name (flexible) and Package name (width 320,
  the value in `<span className="ui-mono">` or "—", `text` = the full value for the tooltip), `onRowClick = openEdit`, `rowTestId` `game-row-<id>`,
  `rowActions` Edit (icon `edit`, label "Edit <name>") and the menu with Delete (`danger`), `menuLabel` "More actions for <name>", `state` (`ListState`): `{ kind: 'loading' }`, `{ kind: 'error', text, onRetry: reload }` when the list has no data, `{ kind: 'empty', text: 'No games.' }`, else `'ready'`.
- Form page (`editorOpen`): `PageHeader onBack={backToList} title={creating ? 'New game' : loadedName}`;
  `<div className="ui-form"><form aria-label="Create game form" | "Edit game form">` with the `FormSection` "Basics"
  (fields as now) and the form error; `FooterBar left={…} right={<><Button variant="secondary" onClick={cancel}>Cancel</Button><Button type="submit" variant="primary">Save</Button></>}`
  inside the form. Left: in edit mode `<Button variant="danger-outline" onClick={() => del.open({ id, name: loadedName })}>Delete</Button>`;
  "Loading…" (`form-hint`) when loading.
- `loadedName` is set when the edit data loads and does not change with the name field.
- Messages: "Game created.", "Game updated.", "Game deleted.".

### Commands page and `CommandForm`

- List page as Games, with `ariaLabel="Commands table"`, `tableClassName="commands-list"`, columns Name (flexible) and
  Steps (width 96, `align: 'end'`, the value in `<span className="ui-num">`; add `.ui-num { font-variant-numeric: tabular-nums; }` to `ui.css`), `rowTestId` `command-row-<id>`, empty text "No commands.". The
  Game column, `filterGame`, and the Game select go away (`listGames` stays if `CommandForm` options need it; else it
  goes away).
- Form page: `PageHeader onBack={backToList} title={creating ? 'New command' : loadedName}`, `<div className="ui-form">`
  around `CommandForm`. `CommandForm` gets the optional props `onDelete?: () => void` and `deleting?: boolean`, and
  replaces `FormActions` with `FooterBar` inside its `<form>`: left = Delete (`danger-outline`, only with `onDelete`,
  off while `submitting || deleting`) and "Loading…" when `loading`; right = Cancel (secondary, `onCancel`) and Save
  (primary, `type="submit"`, off while `submitting`). The page passes `onDelete` only in edit mode. The separate
  `<div className="form-actions">` with Delete goes away.
- The other `btn` uses: `CommandForm` (the add step button) → `Button variant="secondary"`; `RecordedStepList`
  (run, remove) → `Button variant="secondary"` (keep the extra class names for the layout and the tests);
  `VisualStepPicker` (recapture, cancel → secondary; the confirm button → primary). Same texts, same `disabled`.
- Messages: "Command created.", "Command updated.", "Command deleted.".

### CSS removal

- `pages/CommandsPage.css`: remove the `.commands-table*` and `.command-name-cell` / `.command-name` rules when no code
  uses them (search first). Keep the rules that `CommandForm` or other code uses.
- `styles.css`: remove `.games-table` and `.link-button` rules only when no other code uses them (search; Sequences,
  Triggers, and other pages can still use `link-button`).

## Complexity Tracking

No constitution violations.
