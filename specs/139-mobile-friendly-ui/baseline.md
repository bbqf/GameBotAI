# Baseline: Mobile-Friendly UI

**Date**: 2026-10-13

## Before the change (T001)

- `npm test`: 121 suites passed, 795 tests passed.
- `npm run build`: passes.
- Main JS bundle: `index-*.js`, 463,554 bytes raw, 125,080 bytes gzip (Vite reports 126.10 kB).
- CSS bundle: 37.24 kB raw, 6.88 kB gzip.
- Playwright `chromium` project on `master` (built UI on port 4173): 3 tests passed, 12 tests failed in the files
  `navigation`, `commands.steps`, `images/*`, and `sequences-loop-steps`. CI does not run Playwright. These failures
  exist before this feature. The passing tests were: "authoring items reachable in one click from landing",
  "collapsed navigation select works on small screens", and "execution placeholder loads and returns to authoring".

## After the change

### Gate results (T043, T053)

- `npm run build`: passes.
- `npm test` with coverage: 137 suites. 136 passed. 1 test in `StepThroughHistory.test.tsx` failed in one full run
  ("keeps the text that the author types when a poll brings the ..."). The test passes when run alone and in the
  other full runs. The host PC was at 96% CPU from the emulator, so this is a timing flake in code that this feature
  does not change. The other full runs had 861 and 866 passing tests with 0 failures.
- Coverage of the new files: `PhoneCard`, `ConnectionBanner`, `DesktopOnlyNotice`, `unauthorized` 100% lines.
  `PhoneTabBar` 97%, `useConnectionState` 95%, `PhoneQueuesView` 86%, `useLayoutMode` 100%, `queueEntryToggle` 100%.
- Playwright `mobile` project: 48 of 48 passed (widths 320, 360, 390, 480, 600, 768; 769 desktop; 640 x 360 landscape;
  tap size; no sideways scroll; at most 2 screen heights of scroll; desktop-only notice; connection banner).
- Playwright `chromium` project with the changed code: the 3 tests that passed on `master` still pass. The old select
  test is replaced by "bottom tab bar replaces the tabs on small screens", and it passes. The other failures are the
  same failures as on `master`.
- .NET: `WebManifestContentTypeTests` (2 tests), `DefaultsApplyWhenNothingIsSet` (existing test for the
  `127.0.0.1:8080` default, T037), and `PayloadValidationTests` (2 tests) pass.

### Phone rules and bundle size (T044)

- All rules in `components/phone/phone.css` and `pages/Execution.phone.css` sit inside the phone media query. The
  hover rules sit inside `@media (hover: hover)`. `phoneCss.test.ts` checks both rules for both files.
- No other CSS file changed, except the removal of the old collapsed-select rule in `styles.css`.
- Main JS bundle: 474,521 bytes raw, 127,790 bytes gzip. This is +2,710 bytes gzip against the baseline (limit: 10 KB).
- CSS bundle: 43.61 kB raw, 7.87 kB gzip.

### Desktop flows at 1280 px (T045)

Checked by Playwright screenshots of the built UI with mocked data: Queues, Authoring, Execution, Notifications, and
Configuration. The layout is the same as before: top tabs, tables, and the collapsed "Backend Connection" block.
No difference found. The Authoring tabs were not opened one by one. Existing Jest tests cover them.

### First screen time (T047)

- Built UI, CPU slowdown 4x, slow 4G profile, mocked API: 1,400 to 1,570 ms (goal: under 3,000 ms).

### Dead code removal (T050)

- Removed `useNavigationCollapse`, the select-menu branch and the `isCollapsed` prop of `Navigation`, and the
  `.top-nav--collapsed` rule. The 768 px limit of the old hook is now the phone limit. The Jest test and the
  Playwright test that used the select now test the bottom tab bar.

### Real-phone check (T051, SC-005, SC-008)

Not done. This check needs the operator and a real phone. Follow "Real-phone sign-off" in
`docs/mobile-ui-principles.md`, then record the date, the phone, and the result here.

| Date | Phone | Result |
|------|-------|--------|
| | | |
