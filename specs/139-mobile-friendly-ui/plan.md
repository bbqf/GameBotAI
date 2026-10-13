# Implementation Plan: Mobile-Friendly UI

**Branch**: `139-mobile-friendly-ui` | **Date**: 2026-10-13 | **Spec**: [spec.md](spec.md)
**Input**: Feature specification from `/specs/139-mobile-friendly-ui/spec.md`

## Summary

The operator wants to monitor and control GameBot from a phone with an app look and feel. The operator chose Option A: optimize the current React web UI. No new UI, no native app.

The plan has eight parts:

1. A layout mode (`phone` or `desktop`) that follows screen width and height (spec FR-001a).
2. A phone shell: a bottom tab bar, a compact header, and a "More" sheet. The shell replaces the top tabs and the select menu in phone mode.
3. A phone support level (`full`, `reduced`, `desktop-only`) for each area. A desktop-only area shows a notice in phone mode.
4. Phone views for Queues (cards, start/stop, entry enable switch) and phone reflow for Execution, Notifications, Configuration, and Update.
5. Home-screen install (web app manifest and icons).
6. A connection-lost banner and a 401 prompt for the token.
7. A phone access guide.
8. A design-principles document with a review checklist.

The service code stays unchanged, except to check that it serves the manifest file with the correct type.

## Technical Context

**Language/Version**: TypeScript 5.6, React 18.3, Vite 8 (existing)  
**Primary Dependencies**: No new runtime dependency. Existing: react, react-dom, @dnd-kit (not used in phone mode).  
**Storage**: N/A. The browser keeps the token as today (`localStorage` when "remember" is on).  
**Testing**: Jest 29 with Testing Library (unit and component tests; `window.matchMedia` mock). Playwright (existing) with a phone viewport project for e2e and layout checks (no sideways scroll, tap target size).  
**Target Platform**: iOS Safari and Android Chrome, current versions. The service serves the UI from the same origin as the API.  
**Project Type**: Web application (React front end served by the .NET service)  
**Performance Goals**: First screen ready in under 3 s on a throttled mobile profile (SC-009). A Playwright timing test measures it. The phone shell adds no more than 10 KB (gzip) to the JS bundle. Layout mode change has no reload and no visible flicker.  
**Constraints**: Plain HTTP on a home network (no service worker, no offline mode). No change to the API contract. No change to desktop behavior. The service listens on `127.0.0.1` by default and this stays.  
**Scale/Scope**: 7 areas. Reflow work on 5 areas (Queues, Execution, Notifications, Configuration, Update, about 1,600 lines of page code). 2 areas get a notice only (Authoring, Execution Logs).

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

*NON-NEGOTIABLE*: If `build` or required `test` runs are failing (local or CI), implementation progression is blocked until failures are fixed or a documented maintainer waiver exists.

*NON-NEGOTIABLE*: All text in this plan and in the artifacts it produces MUST obey Simplified Technical English (Constitution Principle VI).

| Principle | Status | Note |
|-----------|--------|------|
| I. Code Quality | Pass | New files are small and single-purpose. No new dependency. Lint and `vite build` run in CI. Existing lint and `tsc` failures are known (see memory note on the quality gate); the green gate is `vite build` and `jest`. |
| II. Testing | Pass | Jest tests for the layout hook, the shell, the notice, the phone Queues view, and the connection banner. Playwright phone project checks no sideways scroll and tap size. Desktop tests must stay green (SC-006). |
| III. UX Consistency | Pass | The phone shell uses the same area names and status chips. Error messages are actionable (401 prompt names the Configuration area). |
| IV. Performance | Pass | Budgets are in Technical Context. Bundle size is measured before and after. |
| V. Living Documentation | Pass, with tasks | Update `docs/architecture.md` (UI layout modes, phone support levels, manifest) and refresh its "Last reviewed" date. Set the spec `Status` line and update `specs/STATUS.md`. |
| VI. STE | Pass, with tasks | All new text is STE: UI strings, comments, docs, commit messages. |

No violation. The Complexity Tracking table is not needed.

**Post-design re-check (after Phase 1)**: Pass. The design adds no project and no dependency. It keeps one code base.

## Project Structure

### Documentation (this feature)

```text
specs/139-mobile-friendly-ui/
├── plan.md                  # This file
├── research.md              # Phase 0 output
├── data-model.md            # Phase 1 output
├── quickstart.md            # Phase 1 output
├── approach-comparison.md   # Decision record (Option A chosen)
├── contracts/
│   └── phone-ui-contract.md # Layout modes, support levels, UI behavior rules
├── checklists/
│   └── requirements.md
└── tasks.md                 # Phase 2 output (/speckit-tasks, not created here)
```

### Source Code (repository root)

```text
src/web-ui/
├── index.html                          # edit: theme-color, apple meta tags, manifest link, viewport-fit=cover
├── public/                             # new
│   ├── manifest.webmanifest            # new: name, icons, display standalone, start_url
│   └── icons/                          # new: 192, 512, maskable, apple-touch icon
├── src/
│   ├── App.tsx                         # edit: choose shell by layout mode; phone default area; desktop-only gate
│   ├── types/navigation.ts             # edit: add phone support level to each area
│   ├── hooks/
│   │   ├── useLayoutMode.ts            # new: phone or desktop from matchMedia
│   │   └── useConnectionState.ts       # new: online and service reachability
│   ├── components/
│   │   ├── phone/
│   │   │   ├── PhoneShell.tsx          # new: header, content, bottom tab bar
│   │   │   ├── PhoneTabBar.tsx         # new: bottom tabs and "More" sheet
│   │   │   ├── DesktopOnlyNotice.tsx   # new: notice and way back
│   │   │   ├── ConnectionBanner.tsx    # new: "connection lost" banner
│   │   │   ├── PhoneCard.tsx           # new: tap-to-open card for list rows
│   │   │   └── phone.css               # new: phone layout rules and tap targets
│   │   └── queues/
│   │       └── PhoneQueuesView.tsx     # new: queue cards, start/stop, entry enable switch
│   ├── pages/                          # edit: Execution, NotificationsPage, Configuration, UpdatePage get phone reflow (CSS and small markup changes)
│   │   └── Execution.phone.css         # new: phone rules for Execution
│   ├── lib/
│   │   ├── queueEntryToggle.ts         # new: pure function that flips one entry enabled value in a template save
│   │   └── api.ts                      # edit: detect 401 responses
│   ├── testUtils/viewport.ts           # new: matchMedia mock for width and height
│   ├── theme/global.css                # edit: safe-area variables, touch rules
│   └── styles.css                      # edit: move scattered phone rules into phone.css
├── src/**/__tests__/                   # new tests next to the code they test
└── tests/e2e/mobile.spec.ts            # new: phone viewport checks and load-time check
tests/unit/Service/
└── WebManifestContentTypeTests.cs      # new: manifest content type check (and the default bind test, if none exists)
specs/139-mobile-friendly-ui/baseline.md  # new: baseline numbers, check results, and the real-phone sign-off
docs/
├── mobile-ui-principles.md             # new: design principles and review checklist
├── phone-access.md                     # new: how to reach the UI from a phone
├── architecture.md                     # edit: UI layout modes and phone support levels
└── ui-audit.md                         # edit: add the phone checklist reference
.specify/memory/constitution.md         # edit: Principle III gains a phone-support bullet (MINOR version bump)
.specify/templates/spec-template.md     # edit: add "Phone support level" prompt
.specify/templates/plan-template.md     # edit: add phone check to Constitution Check
specs/STATUS.md                         # edit: add this feature
```

**Structure Decision**: One front-end project (existing `src/web-ui`). All phone code lives in `components/phone/` and in small edits to existing pages. The .NET service gets no code change in the base plan. Task T035 checks the `.webmanifest` content type. A small static-file option is added only if the check fails.

## Design Decisions (summary)

- **Layout mode**: `window.matchMedia('(max-width: 768px), (max-height: 500px)')`. A hook returns `phone` or `desktop` and updates on change. CSS uses the same query so the first paint is right (spec FR-001a).
- **Shell**: In phone mode the app renders `PhoneShell`. In desktop mode the app renders the current layout. The old select menu used the same 768 px limit. It is now unreachable. Task T050 removes it, or records why it stays.
- **Default area**: In phone mode, with no `area` in the URL, the app opens Queues. Desktop keeps Authoring.
- **Tab bar**: `Queues`, `Execution`, `Notifications` (bell icon), `More`. `More` opens a sheet with Configuration, Update, and links to desktop-only areas (labelled "Desktop only").
- **Support levels**: Queues `reduced`; Execution `full`; Notifications `full`; Configuration `full`; Update `full`; Authoring `desktop-only`; Execution Logs `desktop-only`. See [data-model.md](data-model.md).
- **View switch**: `App.tsx` renders `PhoneQueuesView` in phone mode, before `QueuesPage` mounts. The desktop page then does not load its data on a phone.
- **Phone Queues**: The phone shows cards. The primary action is Start or Stop. A tap on the card opens the detail with the entry list and an enable switch for each entry. The switch saves the linked template with the existing API. Create, edit form, duplicate, delete, schedule, and reorder are not shown. The detail shows "Use a larger screen" for them.
- **Connection**: A banner shows when the browser is offline or when the health call fails. Control buttons are disabled. The UI never retries an action. When the connection returns, the UI reloads the state (spec FR-011a).
- **Token (401)**: A 401 response shows a prompt that links to Configuration. This helps a new phone that has no token.
- **Install**: A web app manifest with `display: standalone`, icons, and theme color. iOS meta tags give a standalone window. Offline mode is out of scope (no HTTPS).
- **Principles and enforcement**: `docs/mobile-ui-principles.md` holds the rules. The constitution and the spec and plan templates point to it, so every new UI feature must set a phone support level.

## Risks

| Risk | Mitigation |
|------|------------|
| Phone queue switch changes the template, not the live queue. The change applies at the next queue start. | The detail view says so in plain words. Research R-05 records the reason. |
| Existing pages use fixed widths and wide tables. The reflow could break desktop. | All phone rules are scoped to the phone query. The desktop Jest and Playwright suites run unchanged. |
| The `Execution` page is 467 lines with many panels. | Phase tasks review it panel by panel on a 360 px viewport. A panel that cannot fit moves behind a collapsible section. |
| iOS Safari has quirks (100vh, safe area, input zoom below 16 px font). | Use `dvh` units, safe-area variables, and a 16 px minimum input font in phone mode. Test on a real phone (SC-008). |
| Plain HTTP limits install. | State this in `docs/phone-access.md`. A private HTTPS link is an optional step. |
