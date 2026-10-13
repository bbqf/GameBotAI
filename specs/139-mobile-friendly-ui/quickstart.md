# Quickstart: Mobile-Friendly UI

**Feature**: 139-mobile-friendly-ui | **Date**: 2026-10-13

Use these steps to build, test, and try the phone UI.

## Build and test

1. Open a PowerShell window. Use absolute paths in all commands.
2. Install packages: run `npm ci` in `C:\src\GameBot\src\web-ui`. Use the npm version that CI uses.
3. Run the unit tests: `npm test`.
4. Run the build: `npm run build`. A green `vite build` and a green `jest` run are the quality gate.
5. Install the Playwright browser one time: `npx playwright install chromium`.
6. Run the phone e2e project: `npx playwright test --project=mobile`. The project builds the UI and serves it on port 4174. It needs no running service, because it mocks the API.

## Try it in a desktop browser

1. Start the dev server: `npm run dev` in `C:\src\GameBot\src\web-ui`.
2. Open `http://localhost:5173` in a browser.
3. Open the browser developer tools. Turn on the device toolbar. Pick a phone profile or set 360 x 640.
4. Check these items:
   - The bottom tab bar shows `Queues`, `Execution`, `Notifications`, and `More`.
   - The page has no sideways scroll.
   - Open Authoring from the `More` sheet. The notice shows.
   - Turn the profile to landscape (640 x 360). The phone layout stays.
   - Widen the window past 768 px. The desktop layout returns with no reload.

## Try it on a real phone

1. Read `docs/phone-access.md`.
2. Set the bind address to the PC address and set the access token. Open the firewall port.
3. Open `http://<PC address>:8080` in the phone browser.
4. Enter the token in Configuration.
5. Add the page to the home screen.
6. Do the sign-off steps in `docs/mobile-ui-principles.md` (hands-on check, SC-008).

## Check the install metadata

1. Run the service with the built UI.
2. Request `/manifest.webmanifest`. The content type must be `application/manifest+json`.
