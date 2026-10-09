# Implementation Notes: Auto-Update (as built)

This file describes the feature as it exists in the code. The plan, research, data model, and contracts in this
folder are the design. Where they differ from this file, this file wins. `docs/architecture.md` has the
current-state summary.

## Parts and files

| Part | Files | Job |
|------|-------|-----|
| Domain logic | `src/GameBot.Domain/Updates/` | `UpdateVersionSelector` (tag parse, "greater" rule), `ChecksumVerifier` (SHA-256), `UpdateManifest` (parser, schema 1), and the types `ReleaseInfo`, `UpdateCheckResult`, `UpdateAttempt`, `UpdateState`, `UpdateError`. |
| Service logic | `src/GameBot.Service/Services/Updates/` | `GitHubReleaseClient`, `UpdateHttp` (host list, redirect steps), `UpdateCheckService`, `UpdateDownloader`, `UpdateCoordinator`, `UpdaterLauncher`, `UpdateResultStore`, `UpdateResultReporter`, `UpdateQueueStopper`, `LoopbackGuard`, `InstallLocationGuard`, `InstalledVersionProvider`, `UpdateServiceRegistration`. |
| Routes | `src/GameBot.Service/Endpoints/UpdateEndpoints.cs`, `Models/UpdateModels.cs` | `GET /api/update/status`, `POST /api/update/check`, `POST /api/update/install`. |
| Start-up | `src/GameBot.Service/Hosted/UpdateResultReportingService.cs` | Reads the result of the last attempt before the first request. |
| Updater | `src/GameBot.Updater/` | Console program: wait for the bot, run `msiexec`, start the bot, write the result. |
| Web UI | `src/web-ui/src/pages/UpdatePage.tsx`, `services/update.ts`, `lib/reload.ts` | The top-level area "Update". |
| Release | `.github/workflows/release-installer.yml`, `scripts/new-update-manifest.ps1` | Publish a GitHub Release on request. |
| Gates | `scripts/check-coverage-thresholds.ps1`, `.github/workflows/dotnet.yml` | Coverage gates for the three new areas. |

## Settings

| Setting | Default | Use |
|---------|---------|-----|
| `Update:Repository` | `bbqf/GameBotAI` | The GitHub repository (owner/name) that the bot checks. |
| `Update:ApiBaseUrl` | `https://api.github.com` | The base address of the GitHub REST API. Tests and forks change it. |

## Behavior, step by step

### Check

1. `POST /api/update/check` returns `409 update_in_progress` while an attempt runs. Otherwise it continues.
2. `GitHubReleaseClient` reads `releases/latest`, then the asset `update-manifest.json`, with no token.
3. The tag must be `v<major>.<minor>.<patch>.<build>`. The manifest `version` must equal the tag. The MSI file must be
   an asset of the same release.
4. Every URL must be https on `github.com` or `*.githubusercontent.com`. The bot follows redirects by hand (at most 5)
   and checks the host of each step.
5. `UpdateVersionSelector` gives `updateAvailable` only when the release version is greater than the installed version.
6. A failure is a result: HTTP 200 with `status: "checkFailed"` and an error with a code and a hint. The codes are
   `update_network_error`, `update_rate_limited` (HTTP 403 or 429), `update_no_release` (HTTP 404, a tag that does not
   parse, or a bad `Update:Repository`), `update_manifest_invalid`, and `update_manifest_unsupported`.

The installed version is the `InformationalVersion` of the service assembly without any `+` suffix. A build from source
has no 4-part version. It reads as `0.0.0.0`, so each release is newer. It cannot install anyway (see the guards).

### Install

The route checks in this order. The first failed check decides.

| Order | Answer | Code |
|-------|--------|------|
| 1 | 403 | `update_local_only` (the socket address is not loopback; a header such as `X-Forwarded-For` has no effect) |
| 2 | 409 | `update_not_installed` (the folder of the `HKCU\...\Run` value `GameBot` is not the folder of the service) |
| 3 | 400 | `update_confirmation_required` (`confirmStopQueues` is not `true`, or the body is not valid JSON) |
| 4 | 400 | `update_not_available` (no check yet, a version that differs from the last checked newer release, or not greater) |
| 5 | 409 | `update_in_progress` |
| 6 | 422 | `update_disk_space` (free space on the data drive is below 3 times the MSI size) |
| 7 | 202 | `{ attemptId, state: "downloading" }` |

After the 202, `UpdateCoordinator` runs the attempt in the background:

1. **Downloading.** `UpdateDownloader` streams the MSI to `<data root>\updates\GameBot-<version>.msi`. A file that
   `HttpClient` writes has no Mark of the Web, so Windows shows no SmartScreen prompt. A wrong size, a stop in the
   middle, or a bad host deletes the partial file (`update_download_failed`).
2. **Verifying.** The downloader compares the SHA-256 with the manifest. A mismatch deletes the file
   (`update_checksum_mismatch`).
3. **Installing.** The coordinator writes `update-result.json` with the state `installing`. `UpdaterLauncher` copies
   `updater\` from the install folder to `<data root>\updates\updater` and starts the copy detached. The copy lets the
   MSI replace the installed updater files. The command line has the bot PID, the MSI path, the install folder, the
   data root, the result path, the log path, the attempt id, both versions, the start time, and (when known) the port
   and the bind host.
4. **Stop.** The coordinator calls `StopApplication()` first. Then `UpdateQueueStopper` stops all running queues at
   once, with no wait for idle. The host stopping token is already cancelled at that time, so the queue engine keeps
   the "running" record of each queue. Queues with `resumeOnServiceStart` start again after the restart (feature 098).
5. **Failure.** An error before the launch sets the attempt to `failed` in memory, removes the `installing` file, and
   frees the gate. The old version keeps running.

### Updater program

1. Wait up to 60 s for the bot PID to exit. If it does not exit, write `failed` with `update_install_failed`. Do not
   run `msiexec`. The old bot is still running.
2. Run `msiexec /i "<msi>" /qn /norestart /l*v "<log>" APPLICATIONFOLDER="<folder>" MSIINSTALLPERUSER=1`. It adds
   `PORT` and `BIND_HOST` when the bot passed them. It does not pass `DATA_ROOT`. The installer keeps the saved
   network values by itself. The exit codes 0 and 3010 are success.
3. On any other exit code (or if `msiexec` does not start), write `failed` with `update_install_failed`, the exit code,
   a message and a hint (special texts for 1603, 1618, and 1925), and the log path. Start the old bot. Windows
   Installer has already rolled back.
4. On success, write `restarting`, start the new bot, and write `succeeded` after the process starts. If the bot does
   not start, write `failed` with `update_restart_failed`.

The result file is written to a temporary file first and then moved, so the bot never reads half a file.

### Result after the restart

`UpdateResultReportingService` calls `UpdateResultReporter` before the host accepts requests.

- If the file has a final state (`succeeded` or `failed`), the reporter keeps it in memory as `lastResult` for the
  rest of the bot run, renames the file to `update-result.reported.json`, and (after `succeeded`) deletes the old
  downloads, the updater copy, and `msiexec` logs older than 30 days.
- The new bot can start before the updater writes the final state. The reporter then waits up to 10 s, polling every
  250 ms. If there is still no final state, it decides from the running version: the target version means
  `succeeded`, any other version means `failed` with `update_install_failed`.

### Status route

`GET /api/update/status` returns the installed version, `lastCheck`, the active `attempt` (also a failed one, until the
next attempt), `lastResult`, `canInstallHere`, and `installBlockedReason`. The reason is `remote` or `notInstalled`.
`remote` has priority.

## Web UI

- The page is the top-level area **Update**, after **Configuration**, at `/update` (or `?area=update`).
- The page loads the status, shows the installed version and the result of the last attempt, and has the button
  **Check for Update**. The button is off while a check runs.
- After a check, the page shows "up to date", the failed-check message with its hint, or the new version. The release
  notes show as plain text in a `<pre>` (React escapes the text). The notes area is hidden when the notes are empty.
- **Install update** shows only when `canInstallHere` is not false. Otherwise the page says "Install from the bot PC."
  or "Update works only for an installed bot."
- A confirm dialog says that all active queues stop at once. The request carries `confirmStopQueues: true` only after
  the user accepts.
- While the attempt runs, the page polls the status every 2 s and shows the step. When the bot is down, it shows
  "GameBot restarts" and keeps trying. It ignores any `lastResult` that has another attempt id.
- When the `lastResult` of its own attempt has the state `succeeded`, the page reloads the whole browser page
  (`reloadPage()`), so the browser loads the new web UI. After the reload the page shows "The update to ... is done."
  and does not reload again. A failed attempt shows the error code, message, hint, exit code, and log path, and does
  not reload.

## Release process

- `release-installer.yml` has only the trigger `workflow_dispatch`, with the input `publish_release` (default false).
  A push or a merge starts nothing. `cancel-in-progress` is false.
- With `publish_release: true`, the build job copies `GameBot.msi` and `GameBotInstaller.exe` to `release-assets/`,
  runs `scripts/new-update-manifest.ps1`, and uploads the artifact `release-assets`. The script refuses a version that
  differs from `payloadVersion` in `payload-manifest.json`.
- The job `publish-release` runs only for `workflow_dispatch` with the input true on `refs/heads/master`. It has
  `contents: write`, downloads the artifact, and runs `gh release create v<version> ... --generate-notes`. It does not
  build again.
- The minor version in `installer/versioning/version.override.json` is 8, so the builds are `1.8.0.<run number>`.
- The installer payload script publishes `GameBot.Updater` (framework-dependent) into `service\updater`.

## Tests and coverage

| Level | Where | Notes |
|-------|-------|-------|
| Unit | `tests/unit/Updates/` | Domain logic, client with a fake HTTP handler, downloader, launcher, store, reporter, coordinator, queue stopper, guards, registration, log, and the updater runner. |
| Contract | `tests/contract/Update/UpdateEndpointsContractTests.cs` | All routes and codes. A test filter sets the remote address. A fake lifetime keeps the test host running. |
| Integration | `tests/integration/Updates/` | Check and install with the real client, downloader, store, and launcher; only the release server and the process start are fakes. Also the resume of queues after the restart. |
| Web UI | `pages/__tests__/UpdatePage.test.tsx`, `services/__tests__/update.spec.ts`, `__tests__/updateArea.spec.tsx` | Jest. |

`scripts/check-coverage-thresholds.ps1` gates `GameBot.Domain.Updates`, `GameBot.Service.Services.Updates`, and
`GameBot.Updater` at 80% line and branch coverage (unit run). The classes `SystemUpdaterHost`, `Program` (updater),
`ProcessUpdaterStarter`, `SystemFreeSpaceProvider`, and the registry read in `InstallLocationGuard` have
`[ExcludeFromCodeCoverage]`. They only call the operating system. The manual check covers them.

## Manual check

T056 (the manual check on a real install, `quickstart.md` steps 1 to 10) is done. The project owner did the check
and reported it as done. This file does not hold the measured values (the click count for SC-001, the total update
time for SC-004, and the time of one check).
