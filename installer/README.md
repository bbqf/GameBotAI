# GameBot Windows Installer Scaffold

This folder contains the standalone Windows installer scaffold.

## Intended Output

- Bootstrapper EXE
- MSI payload package

## Tooling

- WiX Toolset (v4+)
- PowerShell packaging scripts in `scripts/`

## Build Flow (scaffold)

1. Publish backend and web UI artifacts
2. Copy outputs into installer payload directory
3. Build WiX MSI
4. Build WiX bootstrapper EXE

## Scaffold Artifacts

- `wix/Fragments/` contains initial WiX fragment scaffolds for directories and components
- `wix/Installer.Build.props` contains shared installer build properties
- `wix/payload/README.md` defines expected payload layout
- `../scripts/installer/` contains installer helper and smoke scripts

## Current State

This is a scaffold branch for the corrected architecture. Authoring and packaging details are intentionally incremental and tracked in `specs/025-standalone-windows-installer/tasks.md`.

## First install and SmartScreen

The build is not signed. When you download `GameBotInstaller.exe` in a browser and start it, Windows can show
one SmartScreen warning ("Windows protected your PC"). Click **More info**, then **Run anyway**. You do this
one time. The installer is per-user, so Windows shows no UAC prompt.

## Update an installed bot

1. Open the GameBot Web UI on the PC that runs GameBot.
2. Open the **Update** tab and click **Check for Update**.
3. If a new version exists, click **Install update** and confirm. All active queues stop at once.
4. Wait. The bot downloads the update, installs it, and starts again. The page shows the result.

Windows shows no SmartScreen warning and no UAC prompt in this path. The bot downloads the file itself, and the
MSI is per-user. You can install an update only from the bot PC, and only when the bot runs from the installed
folder. A bot that starts from source cannot update itself.

If the update fails, the old version still works. The page shows an error code, a message, and a hint. The
Windows Installer log is in `<data root>\updates\msiexec-<attempt id>.log`.

## Publish a release (owner)

1. Merge the change to `master`.
2. Open **Actions**, select `release-installer`, and click **Run workflow**.
3. Select the `master` branch. Set `publish_release` to `true`. Click **Run workflow**.
4. The workflow builds the installer once. Then it creates the GitHub Release `v<major>.<minor>.<patch>.<build>`.
   The release has `GameBot.msi`, `GameBotInstaller.exe`, and `update-manifest.json`.
5. Look at the **Releases** page. The release must be a normal release, not a draft and not a pre-release.

A push to `master` builds an installer but creates no release. Installed bots see only the releases that you
publish this way. The checksum in `update-manifest.json` finds damaged downloads. It gives no protection if an
attacker controls the release, because the build is not signed.
