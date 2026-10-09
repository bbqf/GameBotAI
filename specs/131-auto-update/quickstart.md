# Quickstart: Auto-Update

## Owner: publish a release

1. Merge the change to `master`. Wait for `release-installer` to finish.
2. Open **Actions**, then **release-installer**, then **Run workflow**.
3. Select `master`. Set `publish_release` to `true`. Start the run.
4. Open **Releases**. Check that release `v<version>` has three files: `GameBot.msi`, `GameBotInstaller.exe`, and `update-manifest.json`.

## User: first install

1. Download `GameBotInstaller.exe` from the release page.
2. Run it. If Windows shows a SmartScreen warning, click **More info**. Then click **Run anyway**. This happens one time only.
3. Finish the installer. Open the bot UI.

## User: update

1. Open the UI on the bot PC.
2. Click **Check for Update**.
3. Read the new version and the notes. Click **Install update**.
4. Read the warning that all active queues stop. Confirm.
5. Wait. The UI connects again by itself. It shows the new version and "Update succeeded".

## Developer: local checks

Run the unit and contract tests:

```powershell
& "C:\Program Files\dotnet\dotnet.exe" test "C:\src\GameBot\GameBot.sln" --filter "FullyQualifiedName~Update"
```

Run the UI tests:

```powershell
npm --prefix "C:\src\GameBot\src\web-ui" test -- --testPathPattern=update
```

## Manual check on a real install (required before the PR is Done)

1. Build and install an older build with `build-installer.ps1`. Change `PORT` and `BIND_HOST` from the defaults first.
2. Publish a newer build to a test release. Or use a local fake release server. Set `Update:Repository` or the API base URL for it.
3. Click **Check for Update** and confirm. Check these points:
   - No UAC prompt and no SmartScreen prompt appear.
   - The UI shows the new version.
   - Games, sequences, queues and images are still present (SC-003).
   - `PORT` and `BIND_HOST` keep their values (research R-002).
   - Record the number of clicks (SC-001) and the total time (SC-004).
4. Repeat with a wrong `sha256` in the manifest. Check that the old version still works. Check that the UI shows `update_checksum_mismatch` (SC-005).
5. Open the UI from a second PC. Check that **Install update** is hidden. Check that the API returns `403 update_local_only`.
6. Start a queue and install. Check that the warning appears. Check that the queue resumes after the restart if `resumeOnServiceStart` is on.
7. Start the bot from source, not from the install folder. Check that "Check for Update" works. Check that "Install update" is refused with "Update works only for an installed bot" (FR-015).
8. Close the browser tab while an update runs. Open the UI again. Check that the update finished and the UI shows the result.
9. Measure the time of one check. It must be 5 s or less.
10. Push a small change to `master` without `publish_release`. Check the Releases page. No new release may appear (FR-011).
