# Implementation Plan: Move the Notifications area to the shared UI components

**Branch**: `203-notifications-shared-ui` | **Date**: 2026-11-27 | **Spec**: [spec.md](spec.md)
**Input**: Feature specification from `specs/203-notifications-shared-ui/spec.md`

## Summary

Move the Notifications page to the shared components of feature 202: `PageHeader`, `ListView` for the targets and the
queue levels, `ConfirmDialog` for Delete, and `FooterBar` for the form. Add three shared components (`SidePanel`,
`Toggle`, `SectionHeader`) and the icon `send`. On the desktop the target form opens in a 480 px side panel; on the
phone it opens as a full page. Add field checks for Name and Chat ID. Update the guideline (rule F6 and section 13).

## Technical Context

**Language/Version**: TypeScript 5.6, React 18.3  
**Primary Dependencies**: Vite 8; the shared components in `src/web-ui/src/components/ui/` (feature 202); no new dependency  
**Storage**: N/A  
**Testing**: Jest 29 + Testing Library (`src/**/__tests__`, the notification tests use the `.spec.tsx` names); Playwright 1.49 (`tests/e2e`, project `chromium` at 1280 px, project `mobile` at 360 x 640 px for files that match `mobile\.spec\.ts`)  
**Target Platform**: Current desktop and phone browsers  
**Project Type**: Web application (frontend only: `src/web-ui`)  
**Performance Goals**: The page shows the lists less than 200 ms after the data arrives (same as now). The side panel opens in less than 100 ms after the click. The new CSS adds less than 5 KB to `ui.css`.  
**Constraints**: No layout shift when the data changes. No sideways scroll at 320 px. 44 px tap targets on the phone. No backend change.  
**Scale/Scope**: 3 new shared components, 4 changed notification components, 1 page.

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

*NON-NEGOTIABLE*: If `build` or required `test` runs are failing (local or CI), implementation progression is blocked until failures are fixed or a documented maintainer waiver exists.

*NON-NEGOTIABLE*: All text in this plan and in the artifacts it produces (research, data model, contracts, quickstart, tasks, code comments, user-facing messages) MUST obey Simplified Technical English (Constitution Principle VI).

*Phone check (Constitution Principle III)*: Notifications keeps the phone support level `full`. The feature follows `docs/mobile-ui-principles.md` and `docs/ui-guidelines.md`.

| Gate | Status | Note |
|------|--------|------|
| I. Code quality | Pass | ESLint with `jsx-a11y`. Small components. The old text buttons and the use of `ConfirmDeleteModal` in the area go away. |
| II. Testing | Pass | Unit tests for each new component and for the field checks; changed notification tests; two new browser test files; coverage check of the new files. |
| III. UX consistency | Pass | This is the UX consistency work. Phone level stated. |
| IV. Performance | Pass | Goals above. No hot path. |
| V. Living documentation | Pass | `docs/ui-guidelines.md` changes (F6, section 13). `specs/STATUS.md` gets line 203. `docs/architecture.md` lists the shared UI folder since feature 202; check it in a task. |
| VI. STE | Pass | All new text in STE. New UI text: "Enter a name.", "Enter a chat ID.", "Delete target <name>?", "The target gets no more messages. You cannot undo this.". |
| Version bump | Pass | Done in phase 2 before the push. |

## Project Structure

### Documentation (this feature)

```text
specs/203-notifications-shared-ui/
├── plan.md
├── research.md
├── data-model.md
├── quickstart.md
├── contracts/
│   ├── ui-components.md          # SidePanel, Toggle, SectionHeader, icon send
│   └── notifications-layout.md   # columns, rows, actions, form, texts
└── tasks.md
```

### Source Code

```text
docs/ui-guidelines.md                          # changed: rule F6 exception, section 13

src/web-ui/src/components/ui/
├── SidePanel.tsx                              # new
├── Toggle.tsx                                 # new
├── SectionHeader.tsx                          # new
├── icons.ts                                   # changed: icon send
├── index.ts                                   # changed: exports
├── ui.css                                     # changed: styles of the new parts, form field styles
└── __tests__/SidePanel.test.tsx, Toggle.test.tsx, SectionHeader.test.tsx   # new

src/web-ui/src/components/notifications/
├── TargetList.tsx                             # changed: ListView, row actions, ConfirmDialog
├── TargetForm.tsx                             # changed: cards, Toggle, field checks, FooterBar
├── TargetEditor.tsx                           # new: SidePanel (desktop) or full page (phone) around TargetForm
├── QueueLevelTable.tsx                        # changed: SectionHeader, ListView
└── __tests__/*.spec.tsx                       # changed

src/web-ui/src/pages/NotificationsPage.tsx     # changed: PageHeader, message area, editor state, delete

src/web-ui/tests/e2e/
├── helpers/notificationsMock.ts               # new: mocked API for both files
├── notifications-layout.spec.ts               # new (chromium)
└── notifications-layout.mobile.spec.ts        # new (mobile)
```

**Structure Decision**: Frontend only. New shared parts go in `components/ui/`. The notification components stay in
`components/notifications/`.

## Design

### Shared parts (contracts/ui-components.md)

- `SidePanel`: props `open`, `title`, `onClose`, `footer?`, `children`, `mode?`. Desktop: `<aside role="dialog"
  aria-modal="false" aria-labelledby>`, `position: fixed`, right 0, top 0, bottom 0, width `var(--gb-size-side-panel)`,
  header 56 px (title and close `IconButton` with icon `close`, label "Close"), body with `overflow-y: auto`, footer.
  Phone: a full page in the normal flow (`ui-side-panel--page`), header with a back `IconButton` (icon `back`, label
  "Back") and the title, the body, and the footer at the bottom (`position: sticky`). On open: save
  `document.activeElement`, move the focus to the first focusable element of the body (else to the panel). Escape
  (keydown on the panel) calls `onClose`. On close: give the focus back. The page under the desktop panel stays usable
  (no backdrop).
- `Toggle`: props `checked`, `onChange`, `label`, `id?`, `disabled?`. A `<button role="switch" aria-checked>` with a
  track of 40 x 24 px and a knob, and the label text next to it (`<label>` linked by `aria-labelledby`). Space and Enter
  change it (native button). Tap area 44 px high on the phone.
- `SectionHeader`: props `title`, `actions?`, `id?`. `<div class="ui-section-header"><h3>…</h3><div>actions</div></div>`,
  18 px, margin-top 24 px.
- Form field styles in `ui.css` (for this and later forms): `ui-form-card` (card with an `<h4>` heading),
  `ui-field` (label above), `ui-field__input` (40 px, radius 8 px, border token, 16 px font on the phone),
  `ui-field--error` (red border), `ui-field__error` (red text, 12 px), `ui-field__help`. The old `FormField` component
  stays for other pages.

### Notifications

- `NotificationsPage`: `PageHeader` (title "Notifications", primary `Button` icon `add` "Add target"). A message area
  under the header (`role="status"` for notices, `role="alert"` for errors). State `editing: target | 'new' | null`,
  `deleteTarget`, `testingId`. The delete logic and the test logic stay in the page; `TargetList` and the editor call
  them. On the phone, while `editing !== null`, the page shows only the editor (full page), not the header, the guide,
  and the lists.
- `TargetList`: `ListView` per contracts/notifications-layout.md; `rowActions` gives the visible actions (desktop:
  Send test message, Edit; phone: Send test message) and the menu (desktop: Delete; phone: Edit, Delete). The
  `ConfirmDialog` is in the page (one dialog for row Delete and footer Delete).
- `TargetEditor`: chooses `SidePanel` with the form. It passes `onDelete` (edit only) to the footer.
- `TargetForm`: the cards "Target" and "Status", the `Toggle`, the field checks, and the `FooterBar` (given to the
  `SidePanel` as `footer`; to make this simple, `TargetForm` renders its own body and footer, and `SidePanel` takes the
  form as `children` with the footer inside the form, so that **Save** stays a submit button). The submit logic stays.
- `QueueLevelTable`: `SectionHeader`; `ListView` with the column Queue (flex) and Level (240) where the cell renders the
  select (class `ui-field__input`, 40 px); phone: `phone.title` = name, no chip and no meta, `renderPhoneActions` = the
  select (160 px). State from `loading`, `error` (with **Try again** = `load`), and the empty text.

## Complexity Tracking

No constitution violations.
