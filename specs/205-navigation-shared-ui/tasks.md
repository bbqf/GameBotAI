# Tasks: Move the app navigation to the UI guidelines

**Input**: Design documents from `specs/205-navigation-shared-ui/`
**Prerequisites**: plan.md, spec.md, research.md, data-model.md, contracts/navigation-layout.md, quickstart.md

**Tests**: The spec asks for tests (FR-024, FR-025, FR-026).

**Paths**: Source paths are relative to `src/web-ui/` unless they start with `docs/` or `specs/`.

**Rules for each task**: Use only theme tokens in new CSS. Put each hover rule inside `@media (hover: hover)`. Write all comments and UI text in STE. Do not remove a behavior check from a test; change only names, selectors, and layout checks. Edit UTF-8 files with the Edit or Write tool, not with PowerShell `Set-Content`.

## Format: `[ID] [P?] [Story] Description`

---

## Phase 1: Setup

- [ ] T001 Run `npm --prefix "C:\src\GameBot\src\web-ui" test`, `npm --prefix "C:\src\GameBot\src\web-ui" run lint`, and `npm --prefix "C:\src\GameBot\src\web-ui" run build` before any change. Record the results in the implementation notes.
- [ ] T002 Search the code for each class and label that this feature removes or renames (`navbar`, `brand`, `top-nav`, `tabs`, `tab`, `authoring-nav`, `phone-sheet`, `phone-tabbar__icon`, "GameBot Web UI", "More areas", "Monitoring", "Notifications", "Execution Logs", `<h1>Authoring</h1>`) in `src/` and `tests/e2e/`. Keep the list of hits in the implementation notes; T030 and T031 use it.

---

## Phase 2: Foundational

- [ ] T003 [P] Add the icons `queue`, `monitor`, `alerts`, `authoring`, `logs`, `execution`, `gamepacks`, and `update` to `src/components/ui/icons.ts` per research.md R5 (copy the `d` values of the symbols `queue`, `monitor`, `bell`, `build`, `list`, `pkg`, `sys` in `docs/ui-guidelines/mockups/3-settings-images-backup-panel.html`; a symbol with more than one `path` gets one joined `d` string; `execution` uses the Material play-circle path). Search the tests for `ICON_NAMES` and `ICON_PATHS`; if a test has a fixed list or count of icons, add the 8 new names to it.
- [ ] T004 Change `src/types/navigation.ts` per data-model.md: add `'more'` to `NavigationAreaId`; add `icon` and `group` to `NavigationArea`; set the labels, icons, groups, and orders of the table; add `MainTabId`, `PhoneTabId`, `mainTabOf`, `phoneTabOf`, `hasMoreBack`, `desktopArea`, `SETTINGS_SUB_TABS`, `SETTINGS_FIRST`, `MORE_ROWS`, `MORE_DESKTOP_ONLY_ROWS`, `AUTHORING_TABS`, and the type `AuthoringTab` (moved from `src/components/Nav.tsx`). Add `MAIN_TABS` (id, label, icon for the 5 desktop top tabs) and `PHONE_TABS` (the 4 phone tabs).
- [ ] T005 Add `'more'` to `KNOWN_AREAS` in `src/lib/phoneHistory.ts`.
- [ ] T006 [P] Change `src/types/__tests__/navigation.test.ts` for the new labels, and add tests for `mainTabOf` (each of the 10 areas), `phoneTabOf`, `hasMoreBack`, `desktopArea`, the order of `SETTINGS_SUB_TABS`, `MORE_ROWS`, `MORE_DESKTOP_ONLY_ROWS`, `MAIN_TABS`, and `PHONE_TABS`. Add a `phoneHistory` test: `parseAddress({ search: '?area=more' })` gives the area `more`.
- [ ] T007 Create `src/components/ui/SubTabs.tsx` per plan.md ("Desktop", `SubTabs`) and export it from `src/components/ui/index.ts`. Add the `ui-subtabs*` CSS to `src/components/ui/ui.css` per plan.md ("CSS").
- [ ] T008 [P] Create `src/components/ui/__tests__/SubTabs.test.tsx`: the tabs show in order inside a tablist with the given label; the active tab has `aria-selected="true"` and `tabIndex` 0, the others -1; a click calls `onSelect` with the id (also on the active tab); ArrowRight, ArrowLeft (with wrap), Home, and End move the focus and do not call `onSelect`.
- [ ] T009 Run the unit tests and lint.

---

## Phase 3: User Story 1 - Use the same main areas on the desktop (Priority: P1)

**Goal**: The desktop app bar with 5 top tabs and the connection state.

**Independent Test**: contract D-01 to D-04, D-08 to D-10.

- [ ] T010 [US1] Create `src/components/AppBar.tsx` per plan.md ("Desktop", `AppBar`). Add the `ui-appbar*` CSS to `src/components/ui/ui.css` per plan.md ("CSS").
- [ ] T011 [P] [US1] Create `src/components/__tests__/AppBar.test.tsx`: "GameBot" shows; 5 tabs in order with names Queues, Monitor, Alerts, Authoring, Settings, each with one `svg`; the active tab has `aria-selected="true"`; a click calls `onSelect` with the tab id; `connectionState="online"` shows "Online"; `"lost"` shows "Connection lost" with the class `ui-appbar__conn--lost`. Delete `src/components/__tests__/Navigation.test.tsx` after you move each of its behavior checks to this file.
- [ ] T012 [US1] In `src/App.tsx`: replace the `Navbar` and `<Navigation>` with `<AppBar activeTab={mainTabOf(activeArea)} onSelect={handleMainTab} connectionState={connectionState} />` (plan.md, `App.handleMainTab`). Call `useConnectionState(..., true)` on both layouts. Keep `<main>` at the same child index (research.md R7). Delete `src/components/Navigation.tsx`.
- [ ] T012a [US1] Change the `<title>` in `index.html` to "GameBot" (FR-006a). In `tests/e2e/auth-passkey.spec.ts` change the 3 checks of the text "GameBot Web UI" to a check of the app bar brand "GameBot" (`page.locator('.ui-appbar__brand')` with the text "GameBot"), so that the check still proves that the app shell shows.
- [ ] T013 [US1] Change the page header titles: `src/pages/MonitoringPage.tsx` "Monitoring" → "Monitor"; `src/pages/NotificationsPage.tsx` "Notifications" → "Alerts". Keep the area ids and paths.
- [ ] T014 [US1] Update the unit tests that use the old desktop labels and parts (from the T002 list: `src/__tests__/navigation.spec.tsx`, `src/__tests__/configuration.spec.tsx`, `src/__tests__/App.desktopOnly.test.tsx`, `src/pages/__tests__/QueuesPage.execution.spec.tsx`, `src/components/ui/__tests__/PageHeader.test.tsx`, the Monitoring and Notifications page tests, and others in the list). If a desktop test counts `fetch` calls with fake timers, filter the `/health` calls (research.md R6). Run the full unit suite.

---

## Phase 4: User Story 2 - Use the sub-tabs of Authoring and Settings on the desktop (Priority: P1)

**Goal**: The shared sub-tab row for Authoring and Settings, and the page titles under Settings.

**Independent Test**: contract D-05 to D-07.

- [ ] T015 [US2] In `src/App.tsx` (desktop): render `SubTabs` in the fragment after `AppBar` when `mainTabOf(activeArea)` is `authoring` or `settings` (plan.md, "Desktop render in `App`"). Remove `<h1>Authoring</h1>` and `<Nav>` from `renderAuthoring`. Import `AuthoringTab` from `src/types/navigation.ts` in each file that imports it from `components/Nav` (search). Delete `src/components/Nav.tsx`.
- [ ] T016 [US2] In `src/App.tsx`: `getInitialArea` accepts `?area=more` and a path that starts with `/more` (both give `more`); on the desktop the start area is `desktopArea(area)`. Keep the default Authoring > Commands when the address names no area.
- [ ] T017 [US2] Page titles (FR-012): in `src/App.tsx` replace `{!phone && <h1>Configuration</h1>}`, `<h1>Update</h1>`, and `<h1>Gamepacks</h1>` with `{!phone && <PageHeader title="…" />}`; in `src/pages/Execution.tsx` replace `{!phone && <h1>Execution</h1>}` with `{!phone && <PageHeader title="Execution" />}`; in `src/pages/ExecutionLogs.tsx` replace `<h1>Execution Logs</h1>` with `<PageHeader title="Execution logs" />`. Make no other change to these pages.
- [ ] T018 [P] [US2] Add unit tests to `src/__tests__/navigation.spec.tsx` (desktop layout): a click on Settings opens Execution logs and shows "Settings Navigation" with 5 tabs; a click on Configuration keeps Settings selected; `?area=update` (and each other Settings area) selects Settings and the matching sub-tab; `?area=more` opens Execution logs; Authoring shows "Authoring Navigation" with 5 tabs and no heading "Authoring"; a click on the active Authoring sub-tab sends the reset (the current test of the re-click reset, moved to the new selectors). Update the tests that look for the old headings ("Execution Logs", "Execution", "Configuration", "Update", "Gamepacks" as `h1`).

---

## Phase 5: User Story 3 - Use the bottom tab bar and the More page on the phone (Priority: P1)

**Goal**: 4 icon tabs, the More page, the back arrow to More.

**Independent Test**: contract P-01 to P-09.

- [ ] T019 [US3] Rewrite `src/components/phone/PhoneTabBar.tsx` per plan.md ("Phone", `PhoneTabBar`). Remove the sheet.
- [ ] T020 [US3] Create `src/components/phone/MorePage.tsx` per plan.md ("Phone", `MorePage`) and the `phone-more*` CSS in `src/components/phone/phone.css` per plan.md ("CSS").
- [ ] T021 [US3] In `src/components/phone/PhoneShell.tsx`: the `PhoneHeader` back control becomes `<IconButton icon="back" label="Back" onClick={onBack} />`. In `phone.css`: `.phone-header` min-height 56 px; `.phone-tabbar` and `.phone-tabbar__item` per plan.md ("CSS"); check that `.app--phone .content` bottom padding is at least the tab bar height (64 px plus the safe area).
- [ ] T022 [US3] Change `src/components/phone/DesktopOnlyNotice.tsx`: the button is `<Button variant="secondary" onClick={onBack}>Back to More</Button>`.
- [ ] T023 [US3] In `src/App.tsx` (phone): render `<MorePage onOpen={handleAreaChange} />` for `activeArea === 'more'`; pass `onBack={hasMoreBack(activeArea) ? () => handleAreaChange('more') : undefined}` to `PhoneHeader`; pass `onBack={() => handleAreaChange('more')}` to `DesktopOnlyNotice`. Check that `phoneAreaOrQueues('more')` keeps `more` and that a phone address `?area=execution` still shows Queues.
- [ ] T024 [P] [US3] Change `src/components/phone/__tests__/PhoneTabBar.test.tsx`: 4 buttons in order (Queues, Monitor, Alerts, More), each with one `svg`; `aria-current="page"` on the tab of `phoneTabOf(activeArea)` (check `monitoring`, `notifications`, `more`, `configuration`, `authoring`); a tap on More calls `onChange('more')`; no dialog "More areas" exists; the bar hides while a text input has the focus (keep the current test). Change `src/components/phone/__tests__/PhoneShell.test.tsx` for the back IconButton.
- [ ] T025 [P] [US3] Create `src/components/phone/__tests__/MorePage.test.tsx`: the heading "More"; 3 buttons in order (Configuration, Gamepacks, Update), each with 2 `svg` (icon and chevron); a click calls `onOpen` with the id; the text "Desktop only"; 2 rows (Execution logs, Authoring) with `aria-disabled="true"`, the text "Use a computer", and no button inside; a click on them does not call `onOpen`.
- [ ] T026 [US3] Update the phone unit tests from the T002 list (`src/__tests__/App.phone.test.tsx`, `src/__tests__/App.phoneHistory.test.tsx`, `src/__tests__/App.desktopOnly.test.tsx`, `src/__tests__/PhoneEntryNavigation.test.tsx`): the More sheet steps become More page steps (tap More, then tap the row button). Add tests: the back arrow on Configuration opens More (one new history entry); the More page has no back arrow; `?area=authoring` on the phone shows the notice and "Back to More" opens More; history back after Queues → More → Configuration shows More, then Queues.

---

## Phase 6: Browser tests

- [ ] T027 [P] Create `tests/e2e/navigation-layout.spec.ts` (project `chromium`, 1280 px) for contract D-01 to D-14. Mock `GET /health` (200) and the list calls of the areas that open (use the existing helpers in `tests/e2e/helpers/` where they exist; return empty lists for the others). For D-04 "Connection lost": call `page.clock.install()` before `page.goto`, route `/health` to 503, call `page.clock.runFor(10_000)`, and expect "Connection lost". T011 is the main check of this state; if the browser check is not stable after 3 runs, record it in the implementation notes and keep the T011 check. For D-12 set the viewport to 1024 x 768. For D-14 count the clicks in each test (at most 2 from Queues).
- [ ] T028 [P] Create `tests/e2e/navigation-layout.mobile.spec.ts` (project `mobile`, 360 x 640) for contract P-01 to P-09. Each phone area that the More page or a tab opens must take at most 2 taps from Queues (spec SC-002). For P-06 set the viewport to 320, 360, and 390 px and check `document.documentElement.scrollWidth <= clientWidth` on Queues and on the More page.
- [ ] T029 Update the browser tests from the T002 list (`tests/e2e/navigation.spec.ts`, `tests/e2e/mobile.spec.ts`, `tests/e2e/auth-passkey.spec.ts`, and others) for the new labels and the More page. Use exact names for "More" (`{ name: 'More', exact: true }`).

---

## Phase 7: Polish

- [ ] T030 Remove unused CSS: in `src/styles.css` the rules `.navbar`, `.brand`, `.top-nav`, `.tabs`, `.tab`, `.tab.active` (and their hover rules); in `src/components/phone/phone.css` the `.phone-sheet*` rules, `.phone-tabbar__icon`, and the `.phone-sheet__item:hover` selector. Search each class first (T002 list); keep a rule that other code still uses.
- [ ] T031 Search again for the old labels and parts of T002 in `src/` and `tests/e2e/`. Each hit that remains must be correct (for example the area id `monitoring`, the path `/notifications`, or the document title in `index.html`). Record the hits that stay and why.
- [ ] T032 [P] Update `docs/ui-guidelines.md`: section 3 rule N4 (Settings: Execution logs, Execution, Configuration, Gamepacks, Update), rule N5 (the More rows: Configuration, Gamepacks, Update; group "Desktop only": Execution logs, Authoring; "Use a computer"; the back arrow to More), rule N1 (Settings / More icons); section 13 (`SubTabs` with its props and keyboard use; `AppBar`; the 8 new icons); "Last reviewed" line (feature 205).
- [ ] T033 [P] Update `docs/mobile-ui-principles.md` ("Last reviewed", the feature list: 205 navigation, More page) and add the line for 205 to `specs/STATUS.md`. Check `docs/architecture.md` for the navigation components (`Navigation`, `Nav`, the More sheet) and update the names.
- [ ] T034 Run `npm --prefix "C:\src\GameBot\src\web-ui" run lint`, `npm --prefix "C:\src\GameBot\src\web-ui" run build`, and `npm --prefix "C:\src\GameBot\src\web-ui" test` (the full suite, with the coverage limits). All must pass.
- [ ] T035 Run all Playwright tests per quickstart.md step 3 (both projects, all files, not only the new files). All must pass. If a test fails only with a page-load timeout, run it again once and record it.
- [ ] T036 Visual check (do not skip): with the preview server, take screenshots with Playwright at 1280 x 800 (Queues, Settings > Execution logs, Settings > Configuration, Authoring > Sequences) and at 360 x 640 (Queues, More, Configuration opened from More) to `C:\Users\anton\AppData\Local\Temp\gb-ui-mock\nav-*.png`. Open each screenshot and compare it with the mockups `docs/ui-guidelines/1-list-pattern.png` and `docs/ui-guidelines/3-settings-images-backup-panel.png`: one app bar of 56 px, icons visible, no second title, no text glyph icons, no overlap of the bottom bar and the content, the active tab marked. Fix each defect and repeat. Record the result.
- [ ] T037 Measure the time from a top-tab click to the new page header (Playwright `performance.now()` around the click and the wait; desktop, 5 runs). Record the median; the goal is less than 100 ms (plan.md "Performance Goals").

---

## Dependencies & Execution Order

- Phase 1 → Phase 2 → Phases 3, 4, 5 (Phase 4 needs T012 of Phase 3, because both change the desktop part of `App.tsx`; Phase 5 can run after Phase 2) → Phase 6 → Phase 7.
- T003 and T004 come before T007, T010, T019, T020 (they use the icons and the group module).
- T012 deletes `Navigation.tsx` only after T011 moved its checks. T012a comes after T012 (the brand must exist before the auth-passkey test looks for it).
- T015 deletes `Nav.tsx` only after each import of `AuthoringTab` uses `types/navigation.ts`.

## Parallel Example

- After T004: T006, T008 (tests) can run in parallel with T010 and T019.
- Phase 6: T027 and T028 in parallel.
- Phase 7: T032 and T033 in parallel.

## Implementation Strategy

1. Phases 1 and 2 (data, icons, SubTabs).
2. US1 (desktop app bar) and check D-01 to D-04.
3. US2 (sub-tabs) and check D-05 to D-07.
4. US3 (phone) and check P-01 to P-09.
5. Browser tests, cleanup, documents, visual check, measurement.
