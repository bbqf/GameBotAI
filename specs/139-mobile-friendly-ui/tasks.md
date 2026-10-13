---

description: "Task list for the mobile-friendly UI feature"
---

# Tasks: Mobile-Friendly UI

**Input**: Design documents from `/specs/139-mobile-friendly-ui/`
**Prerequisites**: plan.md, spec.md, research.md, data-model.md, contracts/phone-ui-contract.md, quickstart.md

**Tests**: Tests are included. Constitution Principle II requires tests for executable logic.

**Organization**: Tasks are grouped by user story. Front-end paths are under `C:\src\GameBot\src\web-ui`. They are written relative to it (for example `src/hooks/useLayoutMode.ts`). Docs paths are relative to `C:\src\GameBot`.

**Quality gate**: `npm run build` (vite build) and `npm test` (jest) must be green. Lint and `tsc --noEmit` have known old failures. Do not add new ones.

## Format: `[ID] [P?] [Story] Description`

- **[P]**: Can run in parallel (different files, no dependency on an unfinished task)
- **[Story]**: The user story the task serves (US1 to US6)

---

## Phase 1: Setup

**Purpose**: Baseline and test tools.

- [X] T001 Record the baseline in `specs/139-mobile-friendly-ui/baseline.md`. Include the result of `npm test` and `npm run build`. Include the gzip size of the main JS bundle in `dist/assets`.
- [X] T002 [P] Add a `mobile` project to `playwright.config.ts`. Use a 360 x 640 viewport with touch on. Match `mobile.spec.ts` in this project. Make the `chromium` project ignore `mobile.spec.ts`.
- [X] T003 [P] Create the test helper `src/testUtils/viewport.ts`. It sets a mock `window.matchMedia` for a given width and height. It fires `change` events. It uses the query text `PHONE_MEDIA_QUERY` from the app.

---

## Phase 2: Foundational (blocks all user stories)

**Purpose**: Layout mode, support levels, and the phone shell.

- [X] T004 [P] Write the failing test `src/hooks/__tests__/useLayoutMode.test.ts`. Width 768 gives `phone`. Width 769 gives `desktop`. Height 500 gives `phone`. A `change` event updates the value (contract C-01).
- [X] T005 Create `src/hooks/useLayoutMode.ts`. Export `PHONE_MEDIA_QUERY = '(max-width: 768px), (max-height: 500px)'`. Export the hook `useLayoutMode()` that returns `'phone' | 'desktop'`. Make T004 pass.
- [X] T006 [P] Edit `src/types/navigation.ts`. Add `PhoneSupportLevel`. Add the required field `phone` to `NavigationArea`. Set the values from `data-model.md`. Add a test `src/types/__tests__/navigation.test.ts`. The test checks that each area has a level. It checks that `authoring` and `execution-logs` are `desktop-only`.
- [X] T007 [P] Create `src/components/phone/phone.css`. Scope all rules to the phone query. Add safe-area padding and `dvh` height. Add 44 px tap targets and a 16 px input font. Add `touch-action: manipulation`. Put every hover rule inside `@media (hover: hover)` (contract C-05). Import the file in `src/main.tsx`.
- [X] T008 [P] Write the test `src/components/phone/__tests__/phoneCss.test.ts`. It reads `phone.css` and fails if a `:hover` rule is outside `@media (hover: hover)` (FR-011). Depends on T007.
- [X] T009 [P] Create `src/components/phone/PhoneCard.tsx`. Add a test in `src/components/phone/__tests__/PhoneCard.test.tsx`. A card has a title, an optional status chip, and at most one primary action. A tap on the body calls `onOpen`. A long title ends with an ellipsis (contract C-07).
- [X] T010 [P] Create `src/components/phone/PhoneTabBar.tsx`. Add a test in `src/components/phone/__tests__/PhoneTabBar.test.tsx`. The items are `Queues`, `Execution`, `Notifications` (bell icon), and `More`. The current item has `aria-current="page"`. The `More` sheet lists Configuration, Update, and each desktop-only area with a "Desktop only" label. The bar hides while an input has focus (contract C-02).
- [X] T011 Create `src/components/phone/PhoneShell.tsx`. It has a header with the area title and a back control, a content region, and the tab bar. Add a test in `src/components/phone/__tests__/PhoneShell.test.tsx`. Depends on T010.
- [X] T012 Edit `src/App.tsx`. Use `useLayoutMode`. Keep one `<main>` element in both modes, so area content does not remount when the mode changes. In phone mode, render the phone shell parts around it. In phone mode with no `area` URL parameter, open `queues` (contract C-03). Add a test `src/__tests__/App.phone.test.tsx`. It checks the default area. It checks the mode switch with no reload. It checks that form state in the area survives the switch. Depends on T005, T006, T011.

**Checkpoint**: The app shows the phone shell on a narrow screen. Desktop is unchanged.

---

## Phase 3: User Story 1 - Monitor and control queues from a phone (P1) 🎯 MVP

**Goal**: The operator sees queue status, starts and stops a queue, and switches entries on a phone.

**Independent Test**: At 360 x 640, open the UI. Read the status of each queue. Start one, then stop it. Switch one entry. Do not zoom. Do not scroll sideways. A start or stop takes at most 3 taps (SC-002).

### Tests for User Story 1

- [X] T013 [P] [US1] Write the failing test `src/lib/__tests__/queueEntryToggle.test.ts`. The function flips one `enabled` value. It keeps the order of entries. It keeps `scheduleType`, `timerTimeOfDay`, `timerRelativeOffset`, and `parameterValues` of every entry (research R-05).
- [X] T014 [P] [US1] Write the failing test `src/hooks/__tests__/useConnectionState.test.ts`. An `offline` event gives `lost`. A failed `/health` call gives `lost`. A good call after a loss gives `online` and runs the reload callback. No action is sent again (contract C-09).
- [X] T015 [P] [US1] Write the failing test `src/components/queues/__tests__/PhoneQueuesView.test.tsx`. It checks these points:
  - A card shows the name, the status chip, and one `Start` or `Stop` button.
  - A long queue name ends with an ellipsis. The detail view shows the full name.
  - A tap opens the detail with the entry switches.
  - A switch saves the linked template.
  - The switches are read-only when no template is linked.
  - The hint "Changes apply when the queue starts again." shows.
  - Controls are disabled when the state is `lost`.
  - No create, edit, duplicate, delete, schedule, or reorder control exists (contract C-08).
- [X] T016 [P] [US1] Write the failing tests `src/components/phone/__tests__/ConnectionBanner.test.tsx` and `src/lib/__tests__/unauthorized.test.ts`. The banner text is "Connection lost". A 401 response shows "Access token needed. Open Configuration." with a button to Configuration (contract C-10).

### Implementation for User Story 1

- [X] T017 [P] [US1] Create `src/lib/queueEntryToggle.ts`. It is a pure function from a template detail and an entry index to a `SaveQueueTemplate` with `overwrite: true`. Make T013 pass.
- [X] T018 [P] [US1] Create `src/hooks/useConnectionState.ts`. It uses `navigator.onLine`, the `online` and `offline` events, and a `GET /health` call every 10 s while the page is visible. Make T014 pass.
- [X] T019 [US1] Create `src/components/phone/ConnectionBanner.tsx`. Add 401 detection in `src/lib/api.ts`. Show the token prompt in `src/App.tsx`. Make T016 pass. Depends on T012.
- [X] T020 [US1] Create `src/components/queues/PhoneQueuesView.tsx`. Use `listQueues`, `getQueue`, `startQueue`, `stopQueue`, `getQueueTemplate`, and `saveQueueTemplate` from `src/services`. Use `PhoneCard` and the existing status chip classes. Make T015 pass. Depends on T009, T017, T018.
- [X] T021 [US1] Edit `src/App.tsx`. In phone mode, render `PhoneQueuesView` instead of `QueuesPage`. Do this before `QueuesPage` mounts, so the desktop page does not load its data on a phone. Do not change `QueuesPage.tsx`. Keep the existing `QueuesPage` tests green. Depends on T019, T020.
- [X] T022 [US1] Write the Playwright test `tests/e2e/mobile.spec.ts` for Queues with mocked API routes. Check at widths 320, 360, 390, 480, and 768 px that the page has no sideways scroll. Check that visible controls are at least 44 x 44 px. Check that start and stop work. Check that a 640 x 360 landscape screen keeps the phone layout. Check that a 769 px screen shows the desktop layout.

**Checkpoint**: User Story 1 works alone. This is the MVP.

---

## Phase 4: User Story 2 - Other monitor-and-adjust areas (P2)

**Goal**: Execution, Notifications, Configuration, and Update are usable on a phone.

**Independent Test**: At 360 x 640, do one typical task in each area. Do not zoom. Do not scroll sideways.

### Tests for User Story 2

- [X] T023 [US2] Extend `tests/e2e/mobile.spec.ts` with checks for Execution, Notifications, Configuration, and Update. Test the widths 320, 360, 390, 480, 600, and 768 px (phone layout) and 769 px (desktop layout). Check that the page has no sideways scroll at each width. If the desktop CSS of an area scrolls sideways at 769 px, fix it. If the fix is not small, record the case in `baseline.md` and change the edge case in `spec.md` to "gets the desktop layout". Check the tap size. Check that the main task needs at most 2 screen heights of scroll (SC-004). Do one typical task in each area: see a running session, change a notification level, enter the token, read the update status.

### Implementation for User Story 2

- [X] T024 [P] [US2] Edit `src/pages/Execution.tsx` and add `src/pages/Execution.phone.css`. Show sessions and run details as cards. Put "Start a session", "Execute a command", and "Execute a sequence" in collapsible sections (`CollapsibleSection`). Show one primary action in each view. Review each panel at 360 px.
- [X] T025 [P] [US2] Edit `src/pages/NotificationsPage.tsx`, `src/components/notifications/QueueLevelTable.tsx`, and `src/components/notifications/TargetList.tsx`. In phone mode, show rows as cards instead of tables. Keep the desktop tables unchanged. Add or extend tests in `src/components/notifications/__tests__/`.
- [X] T026 [P] [US2] Edit `src/pages/Configuration.tsx`, `src/components/TokenGate.tsx`, `src/components/ConfigParameterRow.tsx`, and `src/components/ConfigFileSection.tsx`. Stack the fields. Give each field a full-width input. Keep the token field and the "remember" option easy to reach.
- [X] T027 [P] [US2] Edit `src/pages/UpdatePage.tsx`. Use a single column and one primary action.
- [X] T028 [US2] Run `npm test`. Fix any desktop regression in the files from T024 to T027.

**Checkpoint**: User Stories 1 and 2 work.

---

## Phase 5: User Story 3 - Areas that do not fit a phone (P2)

**Goal**: A desktop-only area shows a clear notice on a phone.

**Independent Test**: At 360 x 640, open Authoring and Execution Logs by the `More` sheet and by deep link. A notice shows with a way back. At 1280 px, both areas work as before.

- [X] T029 [P] [US3] Write the failing test `src/components/phone/__tests__/DesktopOnlyNotice.test.tsx`. It checks the heading with the area name. It checks the sentence "Use a larger screen for this area." It checks the button "Back to Queues" (contract C-04).
- [X] T030 [US3] Create `src/components/phone/DesktopOnlyNotice.tsx`. Make T029 pass.
- [X] T031 [US3] Edit `src/App.tsx`. In phone mode, an area with level `desktop-only` renders the notice and never renders the area content. The deep links `?area=authoring` and `/execution-logs` also show the notice. Add the test `src/__tests__/App.desktopOnly.test.tsx` for the phone case and for the desktop case. Depends on T021, T030.
- [X] T032 [US3] Extend `tests/e2e/mobile.spec.ts`. Check that the notice shows for Authoring and Execution Logs. Check that the back button goes to Queues. Check that both areas work at 1280 px. The gate in T031 stops the data calls of these areas on a phone. Check this too.

**Checkpoint**: No area shows a broken layout on a phone.

---

## Phase 6: User Story 4 - Reach the UI from a phone safely (P2)

**Goal**: The operator opens the UI on a phone with the token, and adds it to the home screen.

**Independent Test**: A person follows `docs/phone-access.md` and opens the UI on a phone in under 10 minutes. A default install refuses a phone connection.

- [X] T033 [P] [US4] Create icons in `public/icons/`: 192 px, 512 px, maskable 512 px, and a 180 px Apple touch icon. Create `public/manifest.webmanifest`. Set `name`, `short_name`, `display: standalone`, `start_url`, `theme_color`, `background_color`, and the icons (contract C-11).
- [X] T034 [US4] Edit `index.html`. Link the manifest. Set `theme-color`. Add the Apple standalone meta tags and the touch icon. Set `viewport-fit=cover`. Add a test `src/__tests__/indexHtml.test.ts` that reads the file and checks these tags. Depends on T033.
- [X] T035 [P] [US4] Write a unit test `tests/unit/Service/WebManifestContentTypeTests.cs`. It checks that `FileExtensionContentTypeProvider` returns `application/manifest+json` for `.webmanifest`. If the test fails, add the mapping through a small helper called from `src/GameBot.Service/Program.cs`. Keep `Program.cs` thin. Make the test pass.
- [X] T036 [P] [US4] Check that the installer copies the `public/` files from `dist/` into `web-ui/`. Extend `tests/integration/Installer/PayloadValidationTests.cs` to require `manifest.webmanifest`.
- [X] T037 [P] [US4] Confirm that a test exists for the default listen address `127.0.0.1:8080` (see `GameBotServiceSetup.ResolveListenAddress`). If no test exists, add one in `tests/unit/`. This proves that a default install refuses a phone connection (US4 scenario 1).
- [X] T038 [P] [US4] Write `docs/phone-access.md`. Describe these steps: set `BIND_HOST` or `GAMEBOT_BIND_HOST`; open the firewall port; set `GAMEBOT_AUTH_TOKEN`; open `http://<PC address>:8080`; enter the token; add the page to the home screen. Add a security section with three warnings. First: a service without a token is open to everyone on the network. Second: the token travels in clear text over plain HTTP. Third: use a private VPN link outside the home network. State the HTTPS limit from research R-07. Link the guide from `README.md` and `INSTALL.md`.

**Checkpoint**: The phone can reach the UI by the guide.

---

## Phase 7: User Story 5 - Principles for new features (P3)

**Goal**: Every new UI feature states its phone support level and follows the rules.

**Independent Test**: A reviewer applies the checklist to one old screen and one new screen. The reviewer gives a pass or a fail on each rule in under 10 minutes (SC-007).

- [X] T039 [P] [US5] Write `docs/mobile-ui-principles.md`. Include these parts:
  - The rules. Examples: layout mode, one primary action for each screen, 44 px targets, 16 px inputs, cards not tables. More rules: no hover-only control, no sideways scroll, safe areas, connection-lost behavior, and never repeat an action.
  - The three support levels and how to choose one.
  - The level of each current area.
  - A pass or fail checklist.
  - The real-phone sign-off steps (SC-008).
- [X] T040 [P] [US5] Edit `docs/ui-audit.md`. Add a "Phone checklist" section that links to `docs/mobile-ui-principles.md`. Keep the Execution Logs addendum. Change its phone line to say that the area is desktop-only.
- [X] T041 [US5] Edit `.specify/memory/constitution.md`. Add a bullet to Principle III: "Each UI feature MUST state its phone support level and follow docs/mobile-ui-principles.md." Raise the version to 1.4.0 (MINOR). Set the amended date. Update the Sync Impact Report.
- [X] T042 [P] [US5] Edit `.specify/templates/spec-template.md` and add a "Phone support level" requirement prompt. Edit `.specify/templates/plan-template.md` and add the phone check to the Constitution Check. Depends on T041.

**Checkpoint**: The rules are in the constitution and in the templates.

---

## Phase 8: User Story 6 - Desktop does not get worse (P3)

**Goal**: No desktop change.

**Independent Test**: All desktop tests pass. The desktop flows look the same.

- [X] T043 [US6] Run `npm test` and `npm run build` in `C:\src\GameBot\src\web-ui`. Run `npx playwright test --project=chromium`. Compare with the baseline in `specs/139-mobile-friendly-ui/baseline.md`. Fix any difference.
- [X] T044 [US6] Check that every phone rule is inside the phone query or the phone layout branch. Search `phone.css` and the edited page CSS files for rules outside the query. Check the main bundle size. It must be at most 10 KB (gzip) more than the baseline. Record the result in `baseline.md`.
- [X] T045 [P] [US6] Walk through the main desktop flows at 1280 px: queues list, execution, notifications, configuration, and the authoring tabs. Record "no difference" or the issue in `baseline.md`.

---

## Phase 9: Polish & Cross-Cutting

- [X] T046 [P] Review all new UI strings, code comments, and docs for STE (Constitution Principle VI). Fix each violation. Run the sentence-length check on `spec.md`, `plan.md`, `research.md`, and `tasks.md` again.
- [X] T047 [P] Add a Playwright timing test in `tests/e2e/mobile.spec.ts`. Use Chromium CPU and network throttling. Load the built UI. Check that the first screen is ready in under 3 s (SC-009). Record the measured time in `baseline.md`. Use one retry. In CI, report a time over 3 s as a warning and do not fail the build. A slow runner can cause a false failure.
- [X] T048 [P] Edit `docs/architecture.md`. Add the layout modes, the phone support levels, the manifest, and the connection behavior. Refresh the "Last reviewed" date.
- [X] T049 [P] Edit `specs/STATUS.md`. Set the `**Status**:` line in `specs/139-mobile-friendly-ui/spec.md` to Implemented. Add a changelog entry if the project keeps one.
- [X] T050 Check whether `useNavigationCollapse` and the select-menu branch of `src/components/Navigation.tsx` are now unreachable. The phone limit and the old collapse limit are both 768 px. Remove the dead code with its tests. If a test or a flow needs it, record the reason in `baseline.md`.
- [ ] T051 Do the real-phone check from `docs/mobile-ui-principles.md` (SC-005, SC-008). Record the date, the phone, and the result in `baseline.md`. This task needs the operator.
- [X] T052 Run the steps in `specs/139-mobile-friendly-ui/quickstart.md`. Fix any wrong step.
- [X] T053 Run the full test suites with coverage. Run web-ui Jest, Playwright `chromium` and `mobile`, and the .NET unit tests for the changed files. Check the coverage baseline for the touched code (80% line, 70% branch). All suites must be green before the commit and the pull request.

---

## Dependencies & Execution Order

### Phase dependencies

- Setup (Phase 1): no dependency.
- Foundational (Phase 2): needs T003 from Phase 1. It blocks all user stories.
- US1 (Phase 3): needs Phase 2. This is the MVP.
- US2 (Phase 4): needs Phase 2 and T009 (cards). It does not need US1.
- US3 (Phase 5): needs T021 (the last `App.tsx` edit of US1). Do the `App.tsx` edits in this order: T012, T019, T021, T031.
- US4 (Phase 6): needs no other story. It can start after Phase 1.
- US5 (Phase 7): needs no code. Finish T039 after US1 to US3, so it lists the final support levels.
- US6 (Phase 8): needs US1 to US4 done.
- Polish (Phase 9): needs all stories.

### Within each story

- Write the tests first and see them fail.
- Write pure functions and hooks before components.
- Write components before the page wiring.

### Parallel opportunities

- T002 and T003 together.
- T004, T006, T007, T009, and T010 together (different files).
- T013 to T016 together, then T017 and T018 together.
- T024 to T027 together (one file set for each page).
- US4 (T033 to T038) and US5 (T039 and T040) can run next to US1 to US3 with another worker.

## Parallel example: User Story 1

```text
Task: "Write queueEntryToggle test in src/lib/__tests__/queueEntryToggle.test.ts"
Task: "Write useConnectionState test in src/hooks/__tests__/useConnectionState.test.ts"
Task: "Write PhoneQueuesView test in src/components/queues/__tests__/PhoneQueuesView.test.tsx"
Task: "Write ConnectionBanner and 401 tests"
```

## Implementation Strategy

### MVP first (User Story 1)

1. Phase 1: Setup.
2. Phase 2: Foundational.
3. Phase 3: User Story 1.
4. Stop and check. Use the dev server with a 360 x 640 device profile. Run the Playwright `mobile` project.

### Incremental delivery

1. Add US3 next. It makes the whole app safe on a phone, because no area is broken.
2. Add US2 (more areas), then US4 (real phone access and install).
3. Add US5 (rules) and US6 (final desktop check).
4. Do T051 with the operator on a real phone.

## Notes

- The service API does not change. The only service-side tasks are the content-type test (T035) and the default-bind test (T037).
- Keep all phone rules in the phone layout. This keeps the desktop risk low.
- Commit after each task or group of tasks. Write commit messages in STE. Use `git commit -F` with a file.
