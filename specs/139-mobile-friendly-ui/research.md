# Research: Mobile-Friendly UI

**Feature**: 139-mobile-friendly-ui | **Date**: 2026-10-13

All items from the Technical Context are resolved. No "NEEDS CLARIFICATION" remains.

## R-01: How to choose the phone layout

- **Decision**: Use one media query: `(max-width: 768px), (max-height: 500px)`. A React hook reads it with `matchMedia` and updates on change. CSS uses the same query. The 768 px limit matches the old collapse limit of the navigation, and it gives small tablets the phone layout.
- **Rationale**: The spec requires width-based choice, plus short screens for landscape (FR-001a). The code already uses `matchMedia` in `useNavigationCollapse`. The query needs no user setting and works with the Jest `matchMedia` mock.
- **Alternatives considered**: User-agent detection (fails on small desktop windows and on new devices). A manual switch (the operator rejected it).

## R-02: Phone navigation pattern

- **Decision**: A bottom tab bar with 4 items (Queues, Execution, Notifications, More). The Notifications item shows a bell icon with the full name.. `More` opens a sheet.
- **Rationale**: A bottom bar is in thumb reach and is the common app pattern. The existing top tabs and the select menu are hard to use on a phone. Seven areas do not fit in one bar, and two areas are desktop-only, so a short bar plus a sheet is clearer.
- **Alternatives considered**: A hamburger side drawer (hidden by default, two taps to reach an area). Keep the select menu (not app-like, and the native picker is slow).

## R-03: Which existing code to reuse

- **Decision**: Reuse all service modules (`services/*.ts`), `lib/api.ts`, the token logic, and the status chip. Add phone components for the shell, the cards, and the Queues view. Reflow other pages with CSS first, markup second.
- **Rationale**: The API and the state logic do not depend on screen size. This is the main benefit of Option A.
- **Alternatives considered**: Make `QueuesPage` itself responsive. The page has 600 lines, a table, modals, and edit forms. A separate small phone view is safer and keeps the desktop page unchanged.

## R-04: Tables on a phone

- **Decision**: In phone mode, a list shows cards (`PhoneCard`). A card shows the name, a status chip, and one primary action. A tap opens the detail.
- **Rationale**: Spec FR-005. Tables cause sideways scroll. Existing tables: the queue list, `QueueLevelTable`, `TargetList`, and the execution logs table (desktop-only).
- **Alternatives considered**: A horizontal scroll area for each table (spec forbids sideways scroll). Hide columns with CSS (loses data and breaks screen readers).

## R-05: Entry enable switch on the phone

- **Decision**: The switch reads the linked template (`getQueueTemplate`), flips `enabled` for one entry, and saves with `saveQueueTemplate` (`overwrite: true`). It keeps the schedule type, timers, and parameter values of every entry as they are.
- **Rationale**: `enabled` is a field of the template entry. The queue loads its runtime entries from the template when it starts, so the change applies at the next start. A queue with no linked template shows the switch as read-only with a short reason.
- **Alternatives considered**: Change the live queue entries (the API has no field for it). Reuse the desktop save function (it is part of a 600-line page and needs the full edit state).
- **Open check for implementation**: A test must prove that the save keeps all entry fields (round trip). Then a phone switch cannot erase a schedule.

## R-06: Touch and input rules

- **Decision**: In phone mode, use these rules. A tap target is at least 44 x 44 CSS px. An input font is at least 16 px, which stops the iOS zoom on focus. No control depends on hover. Controls use `touch-action: manipulation`. Layouts use safe-area padding and `dvh` for full height. A test checks that hover rules sit inside `@media (hover: hover)`.
- **Rationale**: These are the standard rules from the platform guidelines and match spec FR-003, FR-011, FR-012.
- **Alternatives considered**: A larger global button size (changes the desktop look).

## R-07: Home-screen install over plain HTTP

- **Decision**: Add `manifest.webmanifest`, icons, `theme-color`, and the Apple meta tags. Do not add a service worker. Document that offline mode and full install prompts need HTTPS.
- **Rationale**: Browsers allow service workers only on secure origins. A home-network service uses plain HTTP, so a shortcut and a standalone window are the realistic result. The spec excludes HTTPS (Assumptions).
- **Alternatives considered**: A service worker (would not register on plain HTTP). Build HTTPS into the service (out of scope; large change).
- **Open check for implementation**: ASP.NET static files must return `application/manifest+json` for `.webmanifest`. Verify with a contract test. If it fails, add the mapping to the static file options.

## R-08: Connection state

- **Decision**: `useConnectionState` combines `navigator.onLine`, the `online` and `offline` events, and a periodic `GET /health` call (every 10 s while the tab is visible). The banner shows when either signal says "lost". Control buttons read the same state and are disabled. The first successful call after a loss triggers a reload of the visible data.
- **Rationale**: `navigator.onLine` alone does not detect "Wi-Fi up, service down". `/health` is public (no token), so it works before the token is set.
- **Alternatives considered**: Rely on failed fetch calls only (the banner would show after an action fails, too late). Automatic retry of actions (rejected in the spec, FR-011a).

## R-09: Token on a new phone

- **Decision**: When an API call returns 401, show a prompt: "Access token needed. Open Configuration." The Configuration page already has the token field and the remember option.
- **Rationale**: The service sends the UI shell without a token, then the first API call fails. A blank error is not actionable (Constitution III).
- **Alternatives considered**: A full-screen login (a new screen for a rare case; the Configuration area already exists).

## R-10: Network exposure and security

- **Decision**: No code change to the service. Write `docs/phone-access.md`: set `BIND_HOST` (installer) or `GAMEBOT_BIND_HOST`, open the firewall port, set `GAMEBOT_AUTH_TOKEN`, and open the address on the phone. State the warning: without a token, anyone on the network has access. Recommend a private VPN link for use outside the home network.
- **Rationale**: Spec FR-015. The settings exist (feature 132). The default stays local-only.
- **Alternatives considered**: Refuse to start on a non-local bind without a token. This is a possible later hardening. It changes service behavior and is out of scope here. Record it as a follow-up idea in the guide.
- **Clear-text token**: The token travels in clear text over plain HTTP. The guide states this and advises a private VPN link outside the home network. The operator chose a guide-only warning, with no UI notice.

## R-11: Enforcing the principles for future features

- **Decision**: Three places: (1) `docs/mobile-ui-principles.md` holds the rules and the checklist. (2) The constitution (Principle III) and the spec and plan templates require each UI feature to state its phone support level. (3) `docs/ui-audit.md` links to the phone checklist.
- **Rationale**: Spec FR-016. A rule that is in the templates is seen at the start of every feature.
- **Alternatives considered**: A lint rule for tap size (cannot check layout in static code). A separate CI visual test for every page (cost is high; the Playwright phone project covers the supported areas only).

## R-12: Testing approach

- **Decision**: Use Jest and Testing Library for the hook, shell, notice, banner, and phone Queues. Use a Playwright project with a phone profile. Test widths: 320, 360, 390, 480, and 768 (phone layout), 769 (desktop layout), and 640 x 360 (landscape). The tests open each supported area with mocked API routes. They assert no sideways scroll (`scrollWidth <= clientWidth`). They assert tap targets of at least 44 px. They assert at most 2 screen heights of scroll for the main task (SC-004). One timing test uses CPU and network throttling to check the 3 s goal (SC-009). Desktop tests stay unchanged.
- **Rationale**: These checks map to SC-001 and SC-003 and run without a real phone. A real-phone check stays a manual sign-off (SC-008).
- **Alternatives considered**: Visual snapshot tests (fragile with fonts and chip colors).
