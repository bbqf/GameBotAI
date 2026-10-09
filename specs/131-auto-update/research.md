# Research: Auto-Update

Each item below answers a question from the plan. No `NEEDS CLARIFICATION` remains.

## R-001: Release source and check method

- **Decision**: Read `GET https://api.github.com/repos/bbqf/GameBotAI/releases/latest`. Do not send a token.
- **Rationale**: The repository is public (spec FR-012). The `latest` route skips drafts and pre-releases. The owner controls what users see (FR-011). One call for each check is far below the limit of 60 requests each hour for each IP.
- **Alternatives considered**: Workflow artifacts need a login token, even in a public repository. A custom update server is a new host to run. A static JSON file in the repository gives no release files and no owner control. All three are rejected.
- **Config**: The repository name is the setting `Update:Repository`. The default is `bbqf/GameBotAI`. Tests and forks can change it.

## R-002: What the bot downloads and how it installs

- **Decision**: Download the MSI (`GameBot.msi`). Do not download the bundle EXE. Run this command: `msiexec /i "<msi>" /qn /norestart /l*v "<log>" APPLICATIONFOLDER="<current folder>" MSIINSTALLPERUSER=1`. Add the saved `PORT`, `BIND_HOST` and `DATA_ROOT` values.
- **Rationale**: The bundle chain has only this MSI, so the MSI works alone. The MSI is `Scope="perUser"`, so a silent run needs no UAC prompt. The MSI already has `MajorUpgrade`. It replaces the old version and keeps the data folder. The setting `AllowSameVersionUpgrades` is on. This does no harm, because the bot never offers an equal version (FR-008).
- **Alternatives considered**: The bundle EXE with `/quiet` can ask for elevation. The bundle has no explicit `Scope`, so this risk is open. A manual file copy from a ZIP skips the Windows Installer rollback. Both are rejected.
- **Open check (quickstart step 5)**: Check on a real install that the saved network values survive a silent upgrade. If they do not survive, pass them as the explicit properties above.

## R-003: Why no Windows prompt appears

- **Decision**: Download with `HttpClient` inside the bot process. Do not use a browser. Do not set the Mark of the Web.
- **Rationale**: SmartScreen and the "unknown publisher" dialog depend on the `Zone.Identifier` stream. Browsers add this stream. A file that `HttpClient` writes does not have it. The per-user MSI needs no elevation, so UAC does not appear. The first manual download in a browser can still show one SmartScreen warning. The owner accepted this.
- **Risk**: Some antivirus products can scan or block an unsigned MSI. The log file and the UI error message must show the `msiexec` exit code.
- **Alternatives considered**: A self-signed certificate adds a trust risk and gives no gain. A paid code-signing certificate is out of scope. Both are rejected.

## R-004: Integrity and download safety

- **Decision**: The release has the file `update-manifest.json`. It has the version, the MSI file name, the size, and the SHA-256 checksum. The bot checks the MSI against the manifest before it starts the updater. The bot accepts only `https` URLs on `github.com` and `*.githubusercontent.com`. The bot checks each redirect step and refuses other hosts.
- **Rationale**: The checksum finds damaged or partial downloads (US4). The host list limits harm if a release has a bad URL.
- **Limit**: The manifest and the MSI come from the same release. If an attacker controls the release, the checksum gives no protection. Only code signing fixes this. The owner accepted this. Write this limit in the docs.
- **Alternatives considered**: A checksum in the release text is hard to parse. Build attestations need a verifier on the user PC. Both are rejected for the first version.

## R-005: Version comparison

- **Decision**: The installed version is the `InformationalVersion` of the service assembly. The build already sets it to `major.minor.patch.build` (`package-installer-payload.ps1`). The .NET SDK can add a suffix such as `+<commit>` to this value. Remove everything from the first `+` before the parse. Parse the release tag `v<major>.<minor>.<patch>.<build>`. Compare the two with `SemanticVersionComparer`.
- **Rationale**: This reuses tested code. The 4-part version already orders builds from the same workflow.
- **Rule**: Offer an update only when the release version is greater than the installed version (FR-008). Ignore a tag that does not parse, and write a log line.

## R-006: Who may install (FR-014)

- **Decision**: `POST /api/update/install` checks `HttpContext.Connection.RemoteIpAddress`. It must be a loopback address. `IPAddress.IsLoopback` covers IPv4-mapped IPv6. A remote request gets `403` with code `update_local_only`. Do not read `X-Forwarded-For`.
- **Rationale**: The bot binds to `127.0.0.1` by default, but it can bind to a LAN address. The check uses the real socket address. A header cannot fake it. The token middleware stays active for all routes.
- **Order**: This check runs before the install location check (R-012). A remote user learns nothing about the install state.
- **Alternatives considered**: A confirmation code on the PC needs more UX work. It was rejected in the clarification.

## R-007: Stop the bot and start it again

- **Decision**: The coordinator does these steps in order:
  1. Stop queue execution.
  2. Write `update-result.json` with the state `installing`.
  3. Start `GameBot.Updater.exe` detached.
  4. Call `IHostApplicationLifetime.StopApplication()`.
- The updater does these steps in order:
  1. Wait up to 60 s for the bot process to exit.
  2. Run `msiexec`.
  3. Start `GameBot.Service.exe` from the install folder.
  4. Write the final result.
- **Rationale**: The bot is a per-user background app. It starts from `HKCU\...\Run`. It is not a Windows service. A file in use blocks a file replace, so the bot must exit first. The updater runs from a copy in `<data root>\updates\`. This lets the MSI replace the installed updater file.
- **Rollback**: If `msiexec` fails, Windows Installer restores the old files. The updater starts the old bot. It writes `failed` with the exit code (US4).
- **Queues**: The coordinator stops queues before shutdown (clarification Q2). Queues with `resumeOnServiceStart` start again through the current `QueueResumeOnStartupService`.

## R-008: Show the result after the restart

- **Decision**: `update-result.json` holds the final state. At start, `UpdateResultReportingService` reads it. It keeps the result in memory for `GET /api/update/status`. It renames the file to `update-result.reported.json`. The bot keeps the result in memory for the rest of the bot run, so the UI can read it more than once. The file is renamed one time.
- **Rationale**: The UI loses its connection during the restart. A file survives the restart. It needs no extra service.

## R-009: GitHub Actions release step

- **Decision**: Add a boolean input `publish_release` to `workflow_dispatch` in `release-installer.yml`. A second job runs only when the input is true and the ref is `master`. This job has `contents: write`. It creates the release `v<version>` at the built commit. It attaches `GameBot.msi`, `GameBotInstaller.exe` and `update-manifest.json`. As built, the workflow has no push trigger. Only `workflow_dispatch` starts it, so a merge to master builds nothing and creates no release.
- **Rationale**: The owner marks a release when the owner starts the workflow by hand (clarification, release choice). Write permission stays in one small job.
- **Alternatives considered**: A release on tag push needs a separate tag step. A release on every master push was rejected in the clarification.
- **Note**: The MSI must be an output of the build. `build-installer.ps1` already copies it to `installer/wix/payload/GameBot.msi`. The upload step adds this file as a build artifact. The publish job downloads the artifacts of the build job. It does not build a second time. This way, the released files are the files that the build tested.
- **Check**: After the first merge, look at the Releases page. A plain push to master must not add a release (FR-011).

## R-010: Concurrency and state

- **Decision**: `UpdateCoordinator` is a singleton. It has one `SemaphoreSlim(1,1)`. A second check or install during an install gets `409` with code `update_in_progress`.
- **Rationale**: This covers the double-click edge case in the spec.

## R-011: Disk space and download failure

- **Decision**: Before the download, check the free space on the data drive. It must be at least 3 times the MSI size from the manifest. If not, fail early with code `update_disk_space`. If the network stops during the download, delete the partial file and set code `update_download_failed`. After a success, delete the older files in `<data root>\updates\`.
- **Rationale**: These are spec edge cases. A fixed factor is simple and safe.

## R-012: Install location guard (FR-015)

- **Decision**: `InstallLocationGuard` reads the registry value `GameBot` in `HKCU\Software\Microsoft\Windows\CurrentVersion\Run`. The MSI writes this value. The value is a quoted path to the EXE. The guard removes the quotes. It compares the folder of the EXE with the folder of the service assembly. The compare ignores case and a trailing slash. If the folders differ, `POST /api/update/install` returns `409 update_not_installed`. The message is "Update works only for an installed bot". `POST /api/update/check` ignores this guard. `GET /api/update/status` returns `canInstallHere: false`.
- **Rationale**: A bot that starts from source has no `Run` value that points to its folder. The guard needs no new setting.
- **Alternatives considered**: A setting to turn the install on or off adds work for the user. It was rejected in the clarification.

## R-013: Updater build

- **Decision**: Publish `GameBot.Updater` framework-dependent. Use the same runtime as the service.
- **Rationale**: The bot runs, so the .NET runtime is on the PC. A self-contained build adds about 70 MB to the installer.
- **Risk**: A damaged runtime after an update also blocks the updater. The risk is small, because the MSI does not change the runtime.
