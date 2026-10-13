# Quickstart: Emulator Monitoring

## Build and test

1. Build the service: `dotnet build C:\src\GameBot\GameBot.sln`.
2. Run the new backend tests: `dotnet test` with the filter `FullyQualifiedName~Monitoring|LdInstanceMapper|SessionRestart`.
3. Web UI: in `C:\src\GameBot\src\web-ui` run `npm run build` (Vite) and `npx jest` (the real green gate; lint and `tsc --noEmit` have old failures). This includes the three navigation test files: `src\components\__tests__\Navigation.test.tsx`, `src\types\__tests__\navigation.test.ts`, `src\__tests__\navigation.spec.tsx`.

## Manual check with a real emulator (LDPlayer, instance index 2, serial `emulator-5558`)

1. Start the service. Start a session for the emulator (`POST /api/sessions/start`).
2. Open the UI. Select Monitoring. Check that the session shows state "live".
3. Select the session. Check that a picture shows in 3 s. Change the emulator screen. Check that the picture changes in 5 s.
4. Switch to another browser tab. Check in the service log that snapshot requests stop. Switch back. Check that they resume.
5. Narrow the window to 360 px (or open on a phone). Check: one column, full-width picture, no sideways scroll, and the same session stays selected.
6. Select "Restart emulator". Check that the dialog names the emulator (and the queue, if one uses it). Confirm.
   Check: state "restarting" with steps, then "Emulator restarted", and the picture returns within 2 min.
7. Start a second restart during the first one (API: `POST /api/sessions/{id}/restart`). Check the 409 answer.
8. Close the emulator by hand. Check that the state changes to "no answer" and the last picture stays with the marker "No live picture".
9. Check that the queue (if one uses the session) recovers by its normal rules after the restart. The restart itself reconnects the session (FR-015); no manual rebind step.

Before the pull request, check these files too: `docs\architecture.md` (new routes, monitoring, restart, new "Last reviewed" date), `specs\STATUS.md` (row for 141), the `Status` line in `spec.md`, and `CHANGELOG.md` (entry under `[Unreleased]` > `Added`).

Steps 1 to 9 are ONE final verification step (FR-025). They cover SC-001, SC-002, SC-004, and SC-006. No code story depends on them.
The automated tests (step "Build and test") prove all code stories alone (FR-023, FR-024).

## API examples

```text
GET  http://localhost:8080/api/sessions/monitoring
GET  http://localhost:8080/api/sessions/{id}/snapshot
POST http://localhost:8080/api/sessions/{id}/restart
GET  http://localhost:8080/api/sessions/{id}/restart
```
