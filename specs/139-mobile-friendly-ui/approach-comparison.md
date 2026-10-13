# Approach Comparison: Phone Access to the GameBot UI

**Feature**: 139-mobile-friendly-ui  
**Created**: 2026-10-13  
**Purpose**: Give the operator the facts to choose a delivery approach (spec FR-014, FR-018).

## Facts About the Current Code

- The web UI is React 18 with TypeScript, built with Vite. It has no router library and no UI library.
- The only runtime dependencies are react, react-dom, and @dnd-kit. Styling is plain CSS (about 1,500 lines in total) with design tokens.
- The service serves the built UI from its own folder. The UI and the API share one origin.
- The UI already has a viewport tag and a collapsible navigation. It has only 4 media queries, and only one is for phones (480 px).
- The UI has about 13 pages. Two pages are very large: the sequence editor (about 2,300 lines) and the queue page (about 640 lines).
- The image crop editor uses mouse events only. It does not work on touch.
- The sequence editor and some lists use drag and drop (@dnd-kit). Touch support is not checked.
- There is no web app manifest and no service worker.
- The service listens on `127.0.0.1:8080` only. The access token is optional. Without a token, there is no access control.

## Options

### Option A: Optimize the current web UI (responsive + installable)

Make the existing React UI responsive. Add a phone navigation shell (bottom tabs). Convert tables to cards. Fix touch handling. Add a web app manifest so the UI can sit on the home screen. Mark heavy editors as desktop-only on small screens.

- **Effort (relative)**: Small to medium. About 1x.
  - New: one phone shell, a responsive-layout base, a "desktop-only" notice, and a manifest with icons.
  - Rework: Queues, Execution, Notifications, Configuration, and Update pages (together about 1,900 lines of page code).
  - Not reworked: sequence editor, crop editor, backup and restore, execution logs (desktop-only).
- **Pros**:
  - Reuses all API code, the token logic, the state logic, and the tests.
  - One code base. A new feature appears on desktop and phone at once.
  - No store, no signing, no install step. An update to the service is an update to the UI.
  - Lowest risk to the desktop UI, because the changes can be limited to small-screen rules.
- **Cons**:
  - It is still a web page. Browser limits apply (no real background work, no push on iOS without install).
  - Full "install as app" and offline mode need HTTPS. Plain HTTP on a home network gives a home-screen shortcut only.
  - The existing CSS has few conventions. Some pages may need structural changes, not only CSS.

### Option B: Build a new, separate mobile web UI

Create a second web front end that targets the phone only. It shares the API with the desktop UI.

- **Effort (relative)**: Medium to large. About 2x to 3x of A.
  - New: project setup, build and serve step, navigation, API client, token handling, all phone screens, tests, and CI work.
  - The service must serve two UIs, or a second site.
- **Pros**:
  - A clean design with no desktop constraints. Best control of the phone feel.
  - The desktop UI is not touched, so desktop regression risk is near zero.
- **Cons**:
  - Two UIs to maintain. Every new feature needs work in both. The two will drift.
  - Duplicate code for API calls, types, and token handling (unless a shared package is built, which adds more work).
  - Same browser limits as Option A, so the gain over A is mostly freedom in layout.

### Option C: Native or cross-platform app

C1 is a thin wrapper around the web UI (for example, a Capacitor-style shell). C2 is a full native or cross-platform app (for example, React Native or Flutter).

- **Effort (relative)**:
  - C1: Medium. About 1.5x of A (A is needed first, plus the wrapper, signing, and distribution).
  - C2: Large. About 4x to 6x of A. All screens, the API client, and tests are written again.
- **Pros**:
  - Best system integration: real push notifications, secure token storage, background tasks, home-screen presence without HTTPS limits.
  - C2 gives the best touch feel and animation.
- **Cons**:
  - Distribution is costly. Android needs a signed APK. iOS needs a paid developer account, or a 7-day test install. App store rules apply for public release.
  - The app version must match the service version. The API must stay stable between them.
  - A separate build pipeline, test setup, and release step are needed. The project is Windows-based, and iOS builds need a Mac.
  - C2 is a third code base.
  - For one operator on a home network, most of this cost has little return.

## Comparison Summary

| Criterion | A: Optimize current UI | B: New mobile web UI | C1: Web wrapper app | C2: Full app |
|-----------|------------------------|----------------------|---------------------|--------------|
| Effort | Lowest (1x) | 2x to 3x | About 1.5x | 4x to 6x |
| Code bases to maintain | 1 | 2 | 1 plus wrapper | 2 |
| Reuse of current code | High | Low | High | None |
| Phone look and feel | Good | Very good | Good | Best |
| Push and background work | Limited | Limited | Yes | Yes |
| Distribution effort | None | None | High | High |
| Risk to desktop UI | Low to medium | None | Low | None |
| Time to first usable phone screen | Short | Long | Medium | Very long |

## Important Limits (all web options)

- **HTTPS**: Browsers allow service workers, full install, and push only on secure origins. A home network with plain HTTP gets a home-screen shortcut and a standalone window on some phones, but no offline mode. A private remote link with HTTPS (for example, a private mesh VPN) removes this limit.
- **Network exposure**: Any option needs the service to accept connections from the phone. This is off by default. The access token MUST be set first. The documentation must say this clearly.
- **Touch**: The image crop editor and the drag-and-drop lists need pointer-event or touch work before they can work on a phone. The spec keeps the whole Authoring area desktop-only to avoid this cost.

## Recommendation

Choose **Option A** now. It reaches the goal (an app-like phone experience for monitor and control) at the lowest cost and risk, and it keeps one code base. It also keeps Option C1 open. Push notifications or true app install can become important later. Then a thin wrapper can be added on top of the same UI. Option B gives more layout freedom, but the cost of two UIs is high for one operator. Option C2 has the highest cost and the least return for this use.

**Decision (2026-10-13)**: The operator chose Option A.
