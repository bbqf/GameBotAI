# Implementation Plan: Move the app navigation to the UI guidelines

**Branch**: `205-navigation-shared-ui` | **Date**: 2026-11-28 | **Spec**: [spec.md](spec.md)
**Input**: Feature specification from `specs/205-navigation-shared-ui/spec.md`

## Summary

Replace the desktop navigation (the "GameBot Web UI" bar, the row of 9 text tabs, and the Authoring tab row) with one
56 px app bar (brand, 5 top tabs with icons, connection state) and one shared `SubTabs` row for Authoring and Settings.
Replace the phone tab bar (3 tabs with text glyphs) and the More sheet with 4 icon tabs and a More page with menu rows.
The area ids and the addresses do not change. A new phone-only area `more` holds the More page. A small group module
maps each area to its main tab.

## Technical Context

**Language/Version**: TypeScript 5.6, React 18.3  
**Primary Dependencies**: Vite 8; shared components in `src/web-ui/src/components/ui/` (features 202 to 204); no new dependency  
**Storage**: N/A  
**Testing**: Jest 29 + Testing Library (`src/__tests__/`, `src/components/**/__tests__/`, `src/types/__tests__/`); Playwright 1.49 (projects `chromium` 1280 px on the dev server, and `mobile` 360 x 640 px for files that match `mobile\.spec\.ts`)  
**Target Platform**: Current desktop and phone browsers  
**Project Type**: Web application (frontend only: `src/web-ui`)  
**Performance Goals**: A tab change shows the new page in less than 100 ms (no extra call). The desktop health call is one `GET /health` each 10 s while the page is visible (the same as the phone).  
**Constraints**: The `<main>` element keeps its position among the children of `.app` in both modes (no remount of the area content on a mode change). No sideways scroll at 320 px. 44 px tap targets on the phone. Hover rules only inside `@media (hover: hover)`. Tokens only in CSS. No backend change.  
**Scale/Scope**: 1 new module, 3 new components (`AppBar`, `SubTabs`, `MorePage`), 4 changed components (`App`, `PhoneTabBar`, `PhoneShell`/`PhoneHeader`, `DesktopOnlyNotice`), 2 removed components (`Navigation`, `Nav`), 8 new icons, about 15 changed test files.

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

*NON-NEGOTIABLE*: If `build` or required `test` runs are failing (local or CI), implementation progression is blocked until failures are fixed or a documented maintainer waiver exists.

*NON-NEGOTIABLE*: All text in this plan and in the artifacts it produces (research, data model, contracts, quickstart, tasks, code comments, user-facing messages) MUST obey Simplified Technical English (Constitution Principle VI).

*Phone check (Constitution Principle III)*: The phone support levels do not change: Queues `reduced`; Monitoring, Configuration, Update, Gamepacks, Notifications `full`; Authoring and Execution logs `desktop-only`; Execution `hidden`. The new area `more` is `full` on the phone and is not shown on the desktop. The feature follows `docs/mobile-ui-principles.md` and `docs/ui-guidelines.md`.

| Gate | Status | Note |
|------|--------|------|
| I. Code quality | Pass | ESLint with `jsx-a11y`. `Navigation.tsx`, `Nav.tsx`, the More sheet, and the unused CSS go away (no dead code). |
| II. Testing | Pass | New unit tests for the group module, `SubTabs`, `AppBar`, `PhoneTabBar`, `MorePage`; changed navigation tests; two new browser test files. |
| III. UX consistency | Pass | This is the UX consistency work. Phone levels stated above. |
| IV. Performance | Pass | Goals above. The desktop health call is the one cost; it is the same call as on the phone. |
| V. Living documentation | Pass | `docs/ui-guidelines.md` sections 3 and 13; `docs/mobile-ui-principles.md` "Last reviewed" and the feature list; `specs/STATUS.md` line 205; `docs/architecture.md` check in a task. |
| VI. STE | Pass | New UI text: "GameBot", "Monitor", "Alerts", "Settings", "More", "Execution logs", "Desktop only", "Use a computer", "Back to More", "Online", "Connection lost". |
| Version bump | Pass | Done in phase 2 before the push. |

## Project Structure

### Documentation (this feature)

```text
specs/205-navigation-shared-ui/
├── plan.md
├── research.md
├── data-model.md
├── quickstart.md
├── contracts/navigation-layout.md
└── tasks.md
```

### Source Code

```text
docs/ui-guidelines.md                                   # section 3 (N4, N5), section 13 (SubTabs, icons)
docs/mobile-ui-principles.md                            # Last reviewed, feature 205

src/web-ui/src/
├── App.tsx                                             # AppBar + SubTabs (desktop), MorePage, back arrow, titles
├── styles.css                                          # remove .navbar .brand .top-nav .tabs .tab rules
├── types/navigation.ts                                 # labels, icons, area 'more', groups
├── types/__tests__/navigation.test.ts                  # changed + group tests
├── lib/phoneHistory.ts                                 # KNOWN_AREAS gets 'more'
├── components/AppBar.tsx                               # new: desktop app bar
├── components/Navigation.tsx                           # removed (replaced by AppBar)
├── components/Nav.tsx                                  # removed; AuthoringTab type moves to types/navigation.ts
├── components/__tests__/Navigation.test.tsx            # removed; replaced by AppBar.test.tsx
├── components/__tests__/AppBar.test.tsx                # new
├── components/ui/SubTabs.tsx                           # new
├── components/ui/__tests__/SubTabs.test.tsx            # new
├── components/ui/icons.ts                              # 8 new icons
├── components/ui/index.ts                              # export SubTabs
├── components/ui/ui.css                                # ui-appbar*, ui-subtabs*
├── components/phone/PhoneTabBar.tsx                    # 4 icon tabs, no sheet
├── components/phone/PhoneShell.tsx                     # PhoneHeader back arrow uses IconButton
├── components/phone/MorePage.tsx                       # new
├── components/phone/DesktopOnlyNotice.tsx              # "Back to More", shared Button
├── components/phone/phone.css                          # header 56 px, tab bar 64 px, More rows; remove sheet rules
├── components/phone/__tests__/PhoneTabBar.test.tsx     # changed
├── components/phone/__tests__/MorePage.test.tsx        # new
├── pages/Execution.tsx                                 # <h1>Execution</h1> → PageHeader
├── pages/ExecutionLogs.tsx                             # <h1>Execution Logs</h1> → PageHeader "Execution logs"
├── pages/MonitoringPage.tsx                            # title "Monitor"
├── pages/NotificationsPage.tsx                         # title "Alerts"
└── __tests__/*.tsx, pages/__tests__/*.tsx              # changed labels

src/web-ui/tests/e2e/
├── navigation-layout.spec.ts                           # new (chromium)
├── navigation-layout.mobile.spec.ts                    # new (mobile)
├── navigation.spec.ts, mobile.spec.ts, auth-passkey.spec.ts, ...   # changed labels
```

## Design

### Group module (`types/navigation.ts`)

- `NavigationAreaId` gets `'more'`. `NavigationArea` gets `icon: IconName` and `group: 'main' | 'settings'`.
- `navigationAreas` (labels and icons of the data model): Queues, Monitor, Alerts, Authoring (`main`); Execution logs,
  Execution, Configuration, Gamepacks, Update (`settings`); More (`'more'`, `phone: 'full'`, `group: 'settings'`).
  The desktop does not show `more` because the desktop renders `MAIN_TABS` and `SETTINGS_SUB_TABS`, not the full
  `navigationAreas` list.
- `MainTabId = 'queues' | 'monitoring' | 'notifications' | 'authoring' | 'settings'`.
- `mainTabOf(area)`: the Settings areas and `more` → `'settings'`; else the area id.
- `phoneTabOf(area)`: `queues`, `monitoring`, `notifications` → the area id; else `'more'`.
- `SETTINGS_SUB_TABS`: the 5 Settings areas in order. `SETTINGS_FIRST = 'execution-logs'`.
- `MORE_ROWS`: `configuration`, `gamepacks`, `update`. `MORE_DESKTOP_ONLY_ROWS`: `execution-logs`, `authoring`.
- `hasMoreBack(area)`: true for `configuration`, `gamepacks`, `update`, `execution-logs`, `authoring` (the pages and
  notices that the More page opens or lists).
- `desktopArea(area)`: `more` → `execution-logs`; else the area.
- `AuthoringTab` and the list of Authoring tabs move here from `Nav.tsx`.

### Desktop

- `AppBar` (`components/AppBar.tsx`): `<header className="ui-appbar">` with `<div className="ui-appbar__brand">GameBot</div>`,
  `<nav aria-label="Primary Navigation">` with a `role="tablist"` of 5 `role="tab"` buttons (`aria-selected`,
  `aria-controls` of the active panel, `Icon` 20 px + name), and on the right
  `<span role="status" className="ui-appbar__conn ui-appbar__conn--online|--lost">` with a dot and "Online" or
  "Connection lost". Props: `activeTab: MainTabId`, `onSelect(tab: MainTabId)`, `connectionState`.
- `App.handleMainTab(tab)`: a click on the Settings top tab always opens Execution logs (FR-010):
  `'settings'` → `handleAreaChange(SETTINGS_FIRST)`; another tab → `handleAreaChange(tab)`. When the active area is
  already `execution-logs`, this is the current re-click reset.
- `SubTabs` (`components/ui/SubTabs.tsx`): a generic component `SubTabs<T extends string>` with props `ariaLabel`,
  `tabs: { id: T; label: string }[]`, `active: T`, `onSelect(id: T)`. Renders `<nav aria-label={ariaLabel} className="ui-subtabs"><div role="tablist">` with `role="tab"`
  buttons (`aria-selected`, `tabIndex` 0 on the active tab and -1 on the others). ArrowLeft and ArrowRight move the focus
  to the previous or next tab (wrap at the ends); Home and End move to the first and last tab; Enter and Space select
  (the native button click). A click on the active tab calls `onSelect` too (the parent sends the reset).
- Desktop render in `App`: one fragment before `<main>` with `AppBar`, and `SubTabs` when `mainTabOf(activeArea)` is
  `authoring` (`ariaLabel="Authoring Navigation"`, the 5 Authoring tabs, `onSelect = handleTabChange`) or `settings`
  (`ariaLabel="Settings Navigation"`, the 5 Settings areas, `onSelect = handleAreaChange`). The fragment keeps `<main>`
  at the same child index as the phone `PhoneHeader`.
- `renderAuthoring` loses `<h1>Authoring</h1>` and `<Nav>`.
- Titles: `renderConfiguration`, `renderUpdate`, `renderGamepacks` replace `{!phone && <h1>…</h1>}` with
  `{!phone && <PageHeader title="…" />}`. `Execution.tsx` replaces `{!phone && <h1>Execution</h1>}` with
  `{!phone && <PageHeader title="Execution" />}`. `ExecutionLogs.tsx` replaces `<h1>Execution Logs</h1>` with
  `<PageHeader title="Execution logs" />`. `MonitoringPage` title "Monitor"; `NotificationsPage` title "Alerts".
- Document title: `index.html` `<title>` becomes "GameBot" (one name, spec FR-006a).
- Connection: `useConnectionState(onReconnect, true)` on both layouts. `ConnectionBanner` stays phone only.
- Desktop `area=more`: `getInitialArea` accepts `more`; the desktop start state uses `desktopArea(area)`.

### Phone

- `PhoneTabBar`: props stay (`areas`, `activeArea`, `onChange`). 4 buttons: Queues (`queue`), Monitor (`monitor`),
  Alerts (`alerts`), More (`more`), each `<Icon size={24}>` + name. `aria-current="page"` on
  `phoneTabOf(activeArea)`. More calls `onChange('more')`. The sheet, `sheetOpen`, and `aria-expanded` go away. The
  input-focus hide stays.
- `MorePage` (`components/phone/MorePage.tsx`): `PageHeader title="More"`; `<ul className="phone-more">` with one
  `<li><button className="phone-more__row">` for each of `MORE_ROWS` (Icon 22 + name + `Icon name="open"`), then
  `<p className="phone-more__group">Desktop only</p>` and `<ul>` with `<li><div className="phone-more__row
  phone-more__row--off" aria-disabled="true">` (Icon + name + `<span className="phone-more__status">Use a
  computer</span>`) for `MORE_DESKTOP_ONLY_ROWS`. Prop: `onOpen(id)`. The dimmed rows are not buttons, so a tap does
  nothing.
- `App` phone render: `activeArea === 'more'` → `<MorePage onOpen={handleAreaChange} />`. `PhoneHeader` gets
  `onBack={hasMoreBack(activeArea) ? () => handleAreaChange('more') : undefined}`.
- `PhoneHeader`: the back control becomes `<IconButton icon="back" label="Back" />` (no text glyph). Height 56 px.
- `DesktopOnlyNotice`: button `<Button variant="secondary">Back to More</Button>`; `App` passes
  `onBack={() => handleAreaChange('more')}`.
- `phoneHistory.KNOWN_AREAS` gets `'more'`. `phoneAreaOrQueues('more')` keeps `more` (it is phone visible).

### CSS

- `ui.css` (tokens only): `.ui-appbar` (height 56 px, flex, gap 12 px, padding 0 16 px, surface, bottom border),
  `.ui-appbar__brand` (700, 16 px), `.ui-appbar__tabs` (flex, height 100%, gap 4 px), `.ui-appbar__tab` (flex, gap 8
  px, padding 0 14 px, muted, 600, 14 px, 3 px transparent bottom border, no background, no radius, height 100%),
  `.ui-appbar__tab[aria-selected="true"]` (primary color and border), `.ui-appbar__conn` (margin-left auto, 13 px, 600,
  dot 8 px; `--online` success color, `--lost` danger color); `.ui-subtabs` (height 44 px, padding 0 24 px, surface,
  bottom border), `.ui-subtabs [role=tablist]` (flex, gap 4 px, height 100%), `.ui-subtabs__tab` (13 px, same tab
  look). Hover rules inside `@media (hover: hover)`. Focus: `:focus-visible` outline 2 px primary.
- `phone.css`: `.phone-header` min-height 56 px; `.phone-tabbar` height 64 px plus the safe area; `.phone-tabbar__item`
  min-height 64 px, font 12 px; `.phone-more*` rules (row 56 px, gap 14 px, padding 0 8 px 0 16 px, bottom border,
  15 px; icon muted 22 px; `--off` opacity 0.55; group label 12 px muted, margin 20 px 0 8 px). Remove `.phone-sheet*`
  and `.phone-tabbar__icon` rules and the `.phone-sheet__item:hover` selector.
- `styles.css`: remove `.navbar`, `.brand`, `.top-nav`, `.tabs`, `.tab`, `.tab.active` and their hover rules after a
  search shows no other use.

## Complexity Tracking

No constitution violations.
