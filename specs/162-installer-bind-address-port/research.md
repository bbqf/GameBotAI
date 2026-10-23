# Research: Installer Bind Address and Port Control

All items from Technical Context are resolved. No "NEEDS CLARIFICATION" remains.

## Findings in the current code

| # | Fault | Where | Effect |
|---|---------|-------|--------|
| F1 | On an upgrade, `WelcomeDlg` goes to `VerifyReadyDlg` and skips `NetworkConfigDlg`. | `Product.wxs` (order 1001) | Fault for FR-001. |
| F2 | The Start-Menu shortcut argument is fixed to `http://localhost:[PORT]/`. The property `SHORTCUT_URL` exists but this component does not use it. | `Directories.wxs` | Fault for FR-008 and FR-009. |
| F3 | `NormalizeStartMenuShortcut` patches the `.lnk` file after `CreateShortcuts`. It runs only if `NOT Installed AND NOT WIX_UPGRADE_DETECTED`. It does not run on an upgrade. | `PortDetection.wxs` | An upgrade keeps the plain `localhost` shortcut. Fault for FR-011. |
| F4 | The shortcut host rule exists four times and the copies differ. `InstallerProperties.wxs`: `0.0.0.0` gives `localhost`. `NetworkConfigUi.wxs`: all non-loopback give the computer name. `updateShortcutHost` and `NormalizeStartMenuShortcut`: two more copies. | several | Duplicate logic. Conflicts with FR-023. |
| F5 | Saved host and port exist in `network.json`, in `HKCU\Software\GameBot\Network`, in `PERSISTED_*` and `PERSISTED_FILE_*` properties, and in `BIND_HOST` and `PORT`. | installer | Duplicates for FR-015. |
| F6 | The bot reads `Service:Network:*`, then `GAMEBOT_*`, then the file, then the registry, then the default. The snapshot lists `GAMEBOT_BIND_HOST`, `GAMEBOT_PORT`, `Service__Network__BindHost`, `Service__Network__Port` as four rows for two values. | `GameBotServiceSetup.cs`, `ConfigSnapshotService.cs` | Duplicate names for FR-016. |
| F7 | A user can save `GAMEBOT_PORT` in `config.json` through the snapshot. The listen address resolver does not read `config.json`. | `ConfigSnapshotService.cs` | A third copy with no effect on the real binding. |
| F8 | `UpdaterArguments` and `MsiexecArguments` still carry `Port` and `BindHost`. `UpdaterLauncher` never passes them. | `GameBot.Updater` | Dead duplicate for FR-016. |
| F9 | Each interactive run shows the dialog only if `DiscoverBindInterfaces` and `DetectAvailablePorts` ran. Both have the condition "no persisted value". | `PortDetection.wxs` | On an upgrade the pre-fill comes only from the `SetProperty` chain. |

## Decisions

### R1. One resolver action

- **Decision**: Add `ResolveNetworkBinding` (JScript, immediate). It sets `BIND_HOST`, `PORT`, and `SHORTCUT_URL`. Remove the `SetProperty` chain, `updateShortcutHost`, and `NormalizeStartMenuShortcut`.
- **Rationale**: The rule lives in one place. A JScript core function can run under `cscript` in tests, as the feature 132 functions do.
- **Alternatives considered**: (a) Keep the `SetProperty` chain and fix the conditions. Rejected: the same rule stays in several places, and a condition fault caused F1 and F3. (b) Write the resolver in C# as a WiX custom action DLL. Rejected: it adds a native build step and a new dependency for a small rule.

### R2. When the resolver runs

- **Decision**: Run it in three places. First, in the UI sequence after `ReadPersistedNetworkFile`. Second, on the **Next** click of `InstallDirDlg`, because the folder can change the data directory. Third, once in the execute sequence after `ReadPersistedNetworkFile`, with the condition `NOT REMOVE~="ALL"`. This run serves silent installs and is the final step. `CreateShortcuts` runs later, so this run sets `SHORTCUT_URL` in time.
- **Rationale**: `DATAROOTFOLDER` is valid only after `CostFinalize`, and it moves with the install folder.
- **Alternatives considered**: Run once only. Rejected: a folder change in the dialog would use the wrong data directory.
- **Rule for repeated runs**: The resolver stores the pre-filled values in `BINDING_PREFILL_HOST` and `BINDING_PREFILL_PORT`. A current value that equals the pre-filled value is "not explicit" and the resolver can replace it. Any other value is explicit and stays.

### R3. Priority order

- **Decision**: explicit value, then `network.json`, then legacy registry (read once), then the first-install default. Each field has its own chain.
- **Rationale**: This is the order of FR-013 and of feature 132. The registry step is new only as a migration step.
- **Alternatives considered**: Drop the registry read. Rejected: an install from before feature 132 has only the registry value, and the binding would reset (FR-018).

### R4. Dialog on upgrade

- **Decision**: Remove the `WelcomeDlg` branch to `VerifyReadyDlg`. Show `InstallDirDlg`, then `NetworkConfigDlg`, on every UI run.
- **Rationale**: FR-001. The folder step also lets the user confirm the data directory that holds the saved file.
- **Alternatives considered**: Go from `WelcomeDlg` straight to `NetworkConfigDlg` on upgrade. Rejected: the bundle passes `APPLICATIONFOLDER`, but a manual run from another account needs the folder step (see `INSTALL.md`).
- **Same-build reinstall**: Not changed. The bundle still asks for reinstall or cancel before the MSI dialog. The binding stays unless the user changes it.

### R5. Port check

- **Decision**: `ValidateNetworkBinding` runs on **Next** of `NetworkConfigDlg`. It checks: a whole number from 1 to 65535, and the host text rule (no space, `/`, `:`). It then runs the port check in `contracts/installer-properties.md`. The check reads `netstat -ano -p tcp` and uses only `LISTENING` lines and the local address. For each match it asks WMI for the process name and path. A listener that is `GameBot.Service.exe` inside `APPLICATIONFOLDER` is "held by GameBot" and does not count. Any other listener counts as "in use". A port in use gives an error and the page stays.
- **Rationale**: FR-006. JScript cannot bind a socket. The old check searched the whole `netstat` text, so a foreign address or a connection that ends looked like a listener. The active bot holds the saved port during an upgrade, so the check must know this. The path test stops a test instance from treating the production bot as its own.
- **Alternatives considered**: (a) Always reject a port in use. Rejected: the active bot holds the saved port, so every upgrade would fail. (b) Compare with the saved port only. Rejected: a different program can hold the saved port. (c) `Get-NetTCPConnection` from PowerShell. Rejected: it starts PowerShell in the install and adds time.

### R6. Silent port conflict

- **Decision**: In silent mode, the resolver does not use the port check as a gate. It writes `WARNING: port <n> is in use` to the installer log when another program holds the saved port. The upgrade ends with success. The bot writes an error to its log and exits with a non-zero code.
- **Rationale**: Clarification Q1 (FR-022). The web server does not start when the bind fails, so no status page can show the failure.
- **Alternatives considered**: Fail and roll back, or pick a new port. Both were rejected by the owner.

### R7. Shortcut

- **Decision**: `Shortcut Arguments="url.dll,FileProtocolHandler [SHORTCUT_URL]"`. `SHORTCUT_URL` is `PROTOCOL://HOST:PORT/`. HOST is `localhost` when `BIND_HOST` is `127.0.0.1`, else `[ComputerName]`. The resolver sets it as its last step, in the execute sequence, before `CreateShortcuts`.
- **Rationale**: FR-008, FR-009, FR-017, FR-023. `MajorUpgrade` removes the old product and installs the new one, so the shortcut is made again from the same resolved values.
- **Alternatives considered**: Change the `.lnk` file after the install. Rejected: it hides the fault and does not run on upgrade (F3).
- **Note**: `PROTOCOL` stays as a registry-persisted value. It is outside this feature.

### R8. Legacy registry cleanup

- **Decision**: Remove the `BindHost` and `Port` values from `PersistNetworkConfigComponent`. Keep `Protocol`. The commit action deletes the old values in `HKCU\Software\GameBot\Network`. The rollback action does not delete them.
- **Rationale**: FR-015, FR-018. A failed upgrade must leave the old state (FR-012).
- **Alternatives considered**: Use the WiX `RemoveRegistryValue` element. Rejected: it runs in the install transaction, before the file is committed, and a rollback of the file write would then lose the legacy value.

### R9. Updater arguments

- **Decision**: Remove `Port` and `BindHost` from `UpdaterArguments`, and `PORT` and `BIND_HOST` from `MsiexecArguments`.
- **Rationale**: The bot never passes them, and the updater copy always comes from the same version as the bot. A runtime override must not become a saved value (FR-019).
- **Alternatives considered**: Keep them as an alias. Rejected: no caller exists, so no one can break.

### R10. Names in the bot

- **Decision**: Main names `GAMEBOT_BIND_HOST` and `GAMEBOT_PORT`. Alias `Service:Network:BindHost` and `Service:Network:Port` (and the env form `Service__Network__*`). The priority between the two is not changed (configuration first, then environment). The snapshot shows one row for each value.
- **Rationale**: `docs/phone-access.md`, `INSTALL.md`, and the snapshot use the `GAMEBOT_*` names. FR-016 allows a documented alias. The same priority avoids a new break.
- **Alternatives considered**: Make `Service:Network:*` the main name. Rejected: it breaks the documented phone setup.

### R11. Source of the active value

- **Decision**: `ResolveListenAddress` returns `HostSource` and `PortSource` (`Environment`, `Configuration`, `NetworkFile`, `Default`). The snapshot uses them as the `Source` of the two rows. The rows are read-only. `UpdateParametersAsync` rejects the two keys with a message that points to the installer and `network.json`.
- **Rationale**: FR-019 and fault F7. A saved `GAMEBOT_PORT` in `config.json` has no effect and misleads.
- **Open check (closed in T002)**: The web UI type `ConfigurationParameter.source` allowed `Default`, `File`, and `Environment`. `ConfigParameterRow` made only `Environment` rows read-only. A new text would show as an editable row with the default badge. No code in `src` uses `GAMEBOT_PORT` or `GAMEBOT_BIND_HOST` except `ResolveListenAddress` and the snapshot, so `IConfigApplier` needs no change. Change: the type allows `Configuration` and `NetworkFile`; `ConfigParameterRow` treats these two sources, and the two row names, as read-only. A jest test covers it (`ConfigParameterRow.test.tsx`).
- **Stale entries**: Older versions let the page save `GAMEBOT_PORT` and `GAMEBOT_BIND_HOST` in `config.json`. These saved values never changed the binding. The snapshot service drops them (and `Service__Network__*`) when it reads `config.json`.

### R12. Safe test method

- **Decision**: Test in three levels. Level 1: `cscript` tests of the resolver, and MSI table checks (`Shortcut`, `CustomAction`, `InstallUISequence`). These change no installed product. The core functions take the registry root as an argument. In the installer, the property `NETWORK_REGISTRY_ROOT` gives the root to the legacy read and to the commit delete. The test variant sets another root, so it never reads or deletes the production values. Tests use a temporary key under `HKCU\Software\GameBotTest` and delete it after the run. A test never reads or deletes `HKCU\Software\GameBot`. Level 2: a test MSI built with a different `UpgradeCode`, product name, Start-Menu folder, autostart name, registry root, and component GUIDs. The installer has 38 fixed GUIDs. Two products that share a component GUID can break each other. The build derives every GUID from the instance name. A test checks that the two MSIs share no GUID. Run it with a temporary `APPLICATIONFOLDER`, `DATA_ROOT`, and port. Level 3: any run that replaces the production binaries. Level 3 needs approval from the owner first.
- **How the variant is made (changed in implementation)**: The build does not take the instance name. The script `scripts/installer/new-test-variant.ps1` copies a built MSI. It changes these identities in the copy: upgrade code, product code, package code, all 38 component GUIDs, registry root, autostart name, Start-Menu folder, default install folder, and `NETWORK_REGISTRY_ROOT`. The default MSI is not changed in any way, so the production package stays as it was. A second package with the same instance name and another `-Revision` has the same upgrade code, so an install of it runs a major upgrade. `TestVariantTests` checks that the variant shares no GUID with the default MSI.
- **Install the variant with `msiexec`, not with the bundle**: The bundle has its own upgrade code and is not patched. `msiexec /i` of the variant shows the MSI dialogs (`WelcomeDlg`, folder, network). The bundle license page does not show.
- **Rationale**: The owner allows a temporary install location and port. The per-user MSI shares `UpgradeCode`, registry, and shortcuts with the production install, so a plain test run is not isolated.
- **Alternatives considered**: Use `msiexec /a` (administrative image). Rejected for logic tests: it does not run the install sequence.

### R13. A failed upgrade must roll back (found in the Level 2 run)

- **Decision**: Set `Schedule="afterInstallInitialize"` on `MajorUpgrade` in `Product.wxs`.
- **Rationale**: FR-012 and SC-005 say a failed upgrade leaves the binding and the shortcuts as they were. The default schedule (`afterInstallValidate`) removes the old product before the install transaction starts. A Level 2 run with a forced failure after the removal left no product, no shortcut, and no `Run` entry. Only `network.json` stayed, because the MSI does not track it. This fault was in the installer before feature 162. With `afterInstallInitialize`, the removal is part of the transaction. The same forced failure then restored the shortcuts, the `Run` entry, the files, and the saved file. The removal still happens before the new files are copied, so the file rules do not change.
- **Alternatives considered**: (a) `afterInstallExecute`. Rejected: the new files are copied first, and the file and component rules for shared components change. (b) Leave the schedule and change FR-012. Rejected: the owner wants the old state back after a failure.
- **Check**: `scripts/installer/new-test-variant.ps1 -FailAfterInstallFiles` adds an error action after `InstallFiles`. See `quickstart.md` step 10.

## Documentation to update

`INSTALL.md` (saved host and port, silent arguments, names), `docs/architecture.md` (network settings section, "Last reviewed" date), `docs/phone-access.md` (names), `CHANGELOG.md`, `specs/161`-style status lines (feature 132 gets "iterated by 162"), `specs/STATUS.md`.
