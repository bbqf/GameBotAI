# Implementation Plan: Move the Monitoring area to the shared UI components

**Branch**: `204-monitoring-shared-ui` | **Date**: 2026-11-27 | **Spec**: [spec.md](spec.md)
**Input**: Feature specification from `specs/204-monitoring-shared-ui/spec.md`

## Summary

Change the Monitoring page from a desktop split view to a list page and a detail page on both layouts. The list uses
`ListView` and the shared `StatusChip`. The detail page uses `PageHeader` (back arrow, name, chip), a grid with the
picture and a key-value card, and `FooterBar` on the phone. The restart dialog becomes the shared `ConfirmDialog`, which
gets an optional `details` prop for the queue warning. Old CSS and the old dialog go away.

## Technical Context

**Language/Version**: TypeScript 5.6, React 18.3  
**Primary Dependencies**: Vite 8; shared components in `src/web-ui/src/components/ui/` (features 202, 203); no new dependency  
**Storage**: N/A  
**Testing**: Jest 29 + Testing Library (`src/pages/__tests__/MonitoringPage.*.test.tsx`, `src/components/monitoring/__tests__`); Playwright 1.49 (projects `chromium` 1280 px and `mobile` 360 x 640 px for files that match `mobile\.spec\.ts`)  
**Target Platform**: Current desktop and phone browsers  
**Project Type**: Web application (frontend only: `src/web-ui`)  
**Performance Goals**: The list shows less than 200 ms after the data arrives (same as now). The detail page opens less than 100 ms after the click (no extra call before it shows). Polling intervals stay (5 s list, 2 s restart, 2 s picture).  
**Constraints**: No layout shift when the states change. No sideways scroll at 320 px. 44 px tap targets on the phone. No backend change. The desktop history stays `replaceState`.  
**Scale/Scope**: 1 page, 2 changed components, 1 removed component, 1 changed shared component (`ConfirmDialog`).

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

*NON-NEGOTIABLE*: If `build` or required `test` runs are failing (local or CI), implementation progression is blocked until failures are fixed or a documented maintainer waiver exists.

*NON-NEGOTIABLE*: All text in this plan and in the artifacts it produces (research, data model, contracts, quickstart, tasks, code comments, user-facing messages) MUST obey Simplified Technical English (Constitution Principle VI).

*Phone check (Constitution Principle III)*: Monitoring keeps the phone support level `full`. The feature follows `docs/mobile-ui-principles.md` and `docs/ui-guidelines.md`.

| Gate | Status | Note |
|------|--------|------|
| I. Code quality | Pass | ESLint with `jsx-a11y`. The old dialog and the unused CSS go away (no dead code). |
| II. Testing | Pass | Changed page tests; a `ConfirmDialog` test for `details`; two new browser test files; a coverage check. |
| III. UX consistency | Pass | This is the UX consistency work. Phone level stated. |
| IV. Performance | Pass | Goals above; no new call. |
| V. Living documentation | Pass | `docs/ui-guidelines.md` section 13; `specs/STATUS.md` line 204; `docs/mobile-ui-principles.md` "Last reviewed" and the feature list; `docs/architecture.md` check in a task. |
| VI. STE | Pass | New UI text: "Restart emulator <name>?", "The whole emulator stops and starts again.", "No queue", "Live". |
| Version bump | Pass | Done in phase 2 before the push. |

## Project Structure

### Documentation (this feature)

```text
specs/204-monitoring-shared-ui/
├── plan.md
├── research.md
├── data-model.md
├── quickstart.md
├── contracts/monitoring-layout.md
└── tasks.md
```

### Source Code

```text
docs/ui-guidelines.md                                   # section 13: ConfirmDialog details
docs/mobile-ui-principles.md                            # Last reviewed, feature 204

src/web-ui/src/
├── App.tsx                                             # remove <h1>Monitoring</h1>
├── components/ui/ConfirmDialog.tsx                     # optional details prop
├── components/ui/__tests__/ConfirmDialog.test.tsx      # details test
├── components/ui/ui.css                                # key-value card classes, detail grid
├── components/monitoring/SessionList.tsx               # ListView; SessionStateChip over StatusChip
├── components/monitoring/sessionView.ts                # new: state → chip, device text, started text
├── components/monitoring/RestartEmulatorDialog.tsx     # removed
├── components/monitoring/__tests__/RestartEmulatorDialog.test.tsx   # removed
├── components/monitoring/__tests__/SessionList.test.tsx              # changed
├── components/monitoring/__tests__/sessionView.test.ts               # new
├── pages/MonitoringPage.tsx                            # list page / detail page
├── pages/Monitoring.phone.css                          # remove unused rules (keep screenshot rules)
└── pages/__tests__/MonitoringPage.*.test.tsx           # changed

src/web-ui/tests/e2e/
├── helpers/monitoringMock.ts                           # new
├── monitoring-layout.spec.ts                           # new (chromium)
└── monitoring-layout.mobile.spec.ts                    # new (mobile)
```

## Design

- `sessionView.ts`: `sessionChip(state)` → `{ state: ChipState; text }` per FR-004; `deviceKind(liveness)` →
  `'live' | 'not_live' | 'none'`; `startedText(iso, locale?)` → `Intl.DateTimeFormat(locale, { dateStyle: 'short',
  timeStyle: 'short' }).format(new Date(iso))`, and "—" for a value that is not a valid date.
- `SessionStateChip` keeps its name and props (`state`) and renders
  `<StatusChip {...sessionChip(state)} testId="session-state" />`.
- `SessionList`: props `sessions`, `state` (`ListState`), `onSelect(id)`, `mode?`. `ListView` with
  `ariaLabel="Sessions"`, `tableClassName="session-list"`, the columns of the contract, `onRowClick`,
  `rowTestId={(s) => \`session-row-${s.id}\`}`, `openLabel={(s) => \`Open ${s.name}\`}`, and the phone fields. The
  `selectedId` prop goes away (no split view).
- `MonitoringPage`: the state and the hooks stay. The render changes:
  - `current === null`: `PageHeader title="Monitoring"`, the message area (list error `role="alert"`, "Session ended"
    `role="status"` with `data-testid="session-ended"`), and `SessionList` with `state` from `sessions` and
    `listError` (`onRetry: loadList`).
  - `current !== null`: `<section data-testid="monitoring-detail">` with `PageHeader` (`onBack`, `title`, `status`),
    desktop actions (the Restart `Button`), and the body `ui-detail-grid` (picture column and `ui-detail-side` column
    of 320 px). Phone: the same parts in one column and `FooterBar` with the Restart `Button` (class
    `ui-button--block`, full width).
  - `onBack`: desktop `select(null)`; phone `closeDetail()`.
  - The dialog: `ConfirmDialog` with `open={dialogOpen}`, the texts of FR-016, `danger`, and `details` = the queue
    warning (FR-018) when `ownerQueueId` is set.
  - A list error while the detail shows: the detail stays (the list call failed, not the session).
- New CSS in `ui.css` (tokens only): `ui-kv` (card: surface, border, radius 10 px, padding 4 px 16 px),
  `ui-kv__row` (grid `120px 1fr`, min-height 40 px, border-bottom, last row no border), `ui-kv__label` (muted,
  13 px), `ui-kv__value` (14 px, one line with "…"); `ui-detail-grid` (grid `minmax(360px, 1fr) 320px`, gap 16 px;
  one column in the phone query); `ui-detail-side` (flex column, gap 12 px); `ui-button--block` (width 100%).
- CSS removal: the split view and the old list and detail rules in `Monitoring.phone.css` and `styles.css` when no
  code uses them (search each class first). Keep the `monitoring-screenshot*` rules.

## Complexity Tracking

No constitution violations.
