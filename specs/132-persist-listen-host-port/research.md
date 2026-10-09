# Research: Persist the Listen Host and Port

All findings come from a read of the code. Items marked "to verify" need a real MSI run. Quickstart steps V-1 to V-9 do those runs. The data path property is `DATAROOTFOLDER` in all places.

## Current behavior (facts from the code)

- The service picks its address in `GameBotServiceSetup.ConfigureWebHostUrls`. Order: `Service:Network:BindHost` and `Service:Network:Port`, then `GAMEBOT_BIND_HOST` and `GAMEBOT_PORT`, then `ReadInstallerNetworkValue` (HKCU `Software\GameBot\Network`), then `127.0.0.1` and `8080`. A port that is not valid becomes `8080` with no log message. `ASPNETCORE_URLS` and `--urls` skip all of this.
- `ConfigSnapshotService.ReadInstallerNetworkValue` is a second copy of the registry reader. It only fills default display values.
- The only writer of the saved values is the MSI component `PersistNetworkConfigComponent` (`ConfigTemplates.wxs`). It writes `Protocol`, `BindHost`, and `Port` from the MSI properties. The component is not permanent, so the MSI removes the values on uninstall and when a major upgrade removes the old product.
- The MSI reads the values again with `RegistrySearch` into `PERSISTED_*` properties (`InstallerProperties.wxs`). Then `SetProperty` actions put `PERSISTED_BIND_HOST` and `PERSISTED_PORT` into `BIND_HOST` and `PORT` whenever the saved value is not empty. No condition checks whether the user gave an option.
- The MSI `Property` table sets `PORT=8080` and `BIND_HOST=127.0.0.1`. The bundle (`Bundle.wxs`) has variables with the same defaults and always forwards them as `MsiProperty`. An explicit option and a default look the same inside the MSI.
- `PortDetection.wxs` and `Product.wxs` skip port detection and the install-folder pages only when `WIX_UPGRADE_DETECTED` is set or a `PERSISTED_*` value is not empty.
- The update from the Web UI starts `GameBot.Updater`. The updater runs `msiexec /i ... /qn /norestart APPLICATIONFOLDER=... MSIINSTALLPERUSER=1` and adds `PORT` and `BIND_HOST` only when the launcher gave them (`MsiexecArguments.Build`). `UpdaterLauncher.BuildArguments` gives them only from `Service:Network:*` or `GAMEBOT_*` values. `SystemUpdaterHost.StartBot` then starts `GameBot.Service.exe` again. It does not touch any network value.
- The data directory is the folder `<install folder>\data` by default (`AppContext.BaseDirectory\data` in the service, `DATAROOTFOLDER` in the MSI, both `%LOCALAPPDATA%\GameBot\data`). The MSI creates the folder with `CreateFolder` and never tracks its other content. Uninstall keeps it when it holds files. The `DATA_ROOT` MSI property is declared but no MSI element uses it.
- `config\config.json` is not a safe place. `ConfigSnapshotService.PersistSnapshotAsync` rewrites the whole file on every refresh and writes every default key into it, including `GAMEBOT_PORT` and `GAMEBOT_BIND_HOST`. Also, the service host setup never reads that file.

## Root cause per scenario

### Scenario 1: update from the Web UI

Confirmed by code:

- The only saved copy is the registry copy that the MSI itself owns. During the update, `RemoveExistingProducts` of the major upgrade removes the old product and its registry values. The new product writes them again from the properties `PORT` and `BIND_HOST`. So the new values are exactly as good as the property values at that moment.
- The launcher passes `PORT` and `BIND_HOST` only from configuration or environment variables. For a normal install neither exists. The launcher passes nothing, and the MSI must rebuild the values from the registry search alone. There is no second source if that search gives an empty value (for example an install from before the registry copy existed, or any run where the HKCU hive that the search sees is not the hive that the service used).
- When the launcher does pass a value (configuration or environment override), the MSI saves it. This turns a runtime override into a saved value. This is the reverse defect and the clarification forbids it.

To verify (V-1): read the msiexec log (`/l*v`) of a real Web UI update with a non-default port. Check the `PERSISTED_PORT` search result and the final `PORT` value. The plan does not depend on the result, because the new file is a second source and the registry stays as a fallback.

### Scenario 2: manual update with a newer installer

Confirmed by code:

- The `SetProperty` actions for `BIND_HOST` and `PORT` override an explicit option (and the dialog value) with the saved value. This breaks FR-007. The cause is the missing "option was given" test. The cause of the missing test is that the bundle and the MSI always carry default values, so "not given" cannot be seen.
- If the registry search gives an empty value, the bundle default `8080` and `127.0.0.1` are forwarded to the MSI and written as the new saved value. This is the reset that the issue reports.

### Scenario 3: uninstall, then install into the same data directory

Confirmed by code:

- The registry component is not permanent. Uninstall removes `HKCU\Software\GameBot\Network`. The data directory stays, but it holds no network value. The next install sees no saved value and applies the first-install defaults and port detection.

### Different user or elevation (edge case)

Confirmed by code that the registry copy is per Windows user (HKCU). A different account, or an elevated run under another account, sees another hive. The data directory is shared by path, so a file in it does not have this problem (FR-004).

## Decisions

### R-001: Where to store the values

- **Decision**: A JSON file `config\network.json` in the data directory (`<data root>\config\network.json`; in the installer `[DATAROOTFOLDER]config\network.json`). The installer writes it. The installer and the service read it. The registry stays as an older read fallback and is still written, so a downgrade keeps working.
- **Rationale**:
  - The data directory is kept by update and by uninstall, and the spec ties scope to "same data directory".
  - It does not depend on the Windows user or on elevation.
  - It is a separate file, so `ConfigSnapshotService` cannot overwrite it. The service host setup can read it with no new dependency.
  - The installer already runs JScript with `Scripting.FileSystemObject` (`PortResolver.jscript`), so no new custom action type is needed.
- **Alternatives considered**:
  - *Permanent HKCU registry component*: survives uninstall, but stays tied to the Windows user. Fails the user-change case of FR-004.
  - *HKLM registry*: needs elevation and breaks the per-user install model.
  - *`config\config.json`*: rewritten at every refresh, and not read by host setup. Rejected.
  - *`%ProgramData%`*: outside the data directory. A reinstall into another data directory would wrongly pick up the values (clarification says it must not).
  - *Service writes the file at start-up from the effective values*: would save configuration and environment overrides. Rejected by the clarification.

### R-002: Who writes the file

- **Decision**: The installer writes it with a deferred JScript custom action `WritePersistedNetworkFile`, scheduled `After="InstallFiles"` with condition `NOT REMOVE~="ALL"`. Immediate `SetProperty` rows set `CustomActionData` for each of the three deferred actions (property name equal to the action id), `Before="InstallInitialize"`. Write data: `[DATAROOTFOLDER]|[BIND_HOST]|[PORT]`. Rollback data: `[DATAROOTFOLDER]`. Commit data: `[DATAROOTFOLDER]`. The action creates the `config` folder when it is missing, backs up the old file (`network.json.bak`, or the marker `network.json.none`), writes `network.json.tmp`, and replaces the file. A rollback action `RollbackPersistedNetworkFile` (before the write action) restores the backup or removes the new file, and removes `network.json.tmp`, also after a partial write. A commit action `CommitPersistedNetworkFile` deletes the backup, the marker, and the `.tmp` file. The exact steps are in plan.md (Write action section).
- **Rationale**: The installer knows which value the user chose. The service does not know the source of a value. This keeps FR-007 and the "only installer-chosen values" rule. A deferred action with rollback and commit makes a cancelled or rolled-back run leave the file as it was (FR-017). An immediate action cannot be rolled back.
- **Alternatives considered**: A WiX `util:XmlFile` or a `File` element. A tracked file is removed on uninstall (fails scenario 3). `XmlFile` does not write JSON. An immediate write: not rollback-safe.

### R-003: Priority

- **Decision**:
  - Service: configuration, environment variable, `network.json`, registry, default.
  - Installer: explicit option or dialog value, `network.json`, registry, port detection or default.
- **Rationale**: Matches FR-005, FR-006, and FR-007. The installer needs the explicit value first, so the user can change the address on purpose.

### R-004: How the installer sees an explicit option

- **Decision**: Remove the default values from the bundle variables `BIND_HOST` and `PORT` and from the MSI `Property` rows. Empty means "not given". Change the `SetProperty` rows to apply the saved value only when the property is empty. The existing last-step rows still set `127.0.0.1` and `8080` when the value is still empty. The rows move from `Before="CostFinalize"` to a fixed chain after `ReadPersistedNetworkFile` (see R-005). Each row has an explicit `Action` name.
- **Rationale**: This is the smallest change that lets the MSI tell an option from a default, with no new public property. Port detection keeps its own rules for a first install (FR-011): it runs only when no saved value and no option exist, and it starts from an empty requested port, which already means "use the preferred order".
- **Risk**: The interactive dialog shows the control values. For a first install, `DiscoverBindInterfaces` and `DetectAvailablePorts` already fill them before the dialog. A check in the manual steps (V-4) confirms the dialog still shows `127.0.0.1` and the first free port.
- **Alternatives considered**: A new `EXPLICIT_*` property copy made at the start of the sequence. More moving parts and the same result.

### R-005: Read the file inside the installer

- **Decision**: A new immediate JScript action `ReadPersistedNetworkFile`, scheduled `After="CostFinalize"` in `InstallUISequence` and in `InstallExecuteSequence`. The data path property is `DATAROOTFOLDER` (the Directory id for `<install folder>\data`, full path with a trailing `\`). The action reads `[DATAROOTFOLDER]config\network.json` with a simple pattern match (no `eval`), validates the host and the port, and sets the new properties `PERSISTED_FILE_BIND_HOST` and `PERSISTED_FILE_PORT`. Each field is set on its own (FR-018).
- **Sequence position**: `CostFinalize` resolves the Directory table, so `DATAROOTFOLDER` (and `APPLICATIONFOLDER` as a directory) is not valid before it (FR-016). The old plan read the file before the `SetProperty` rows, which run `Before="CostFinalize"`. That was not valid. Now these actions run in this order: `ReadPersistedNetworkFile`, `DiscoverBindInterfaces`, `DetectAvailablePorts` (first install only), then the `SetProperty` rows for `BIND_HOST`, `PORT`, `PROTOCOL`, `SHORTCUT_*`. Dialogs of the UI sequence run after `CostFinalize`, so they see the final values. The skip conditions in `PortDetection.wxs` and `Product.wxs` also test the file properties.
- **Rationale**: `RegistrySearch` cannot read a file. `FileSearch` cannot read content. JScript is already in use. The registry search (`AppSearch`) runs earlier and needs no folder, so the registry fallback stays valid.
- **Alternatives considered**: Use `APPLICATIONFOLDER` and read before `CostFinalize`: it is only a plain property there and a user can change it later in the UI. `DATA_ROOT`: unused and set to `__UNSET__`.
- **Note**: The data folder in the MSI and the folder that the service uses are the same path (`%LOCALAPPDATA%\GameBot\data`) for a default install. For a custom install folder, the data directory moves with the install folder (`AppContext.BaseDirectory\data`). A reinstall into a different folder finds no file. This matches the clarification.

### R-006: The Web UI update stops forwarding overrides

- **Decision**: `UpdaterLauncher.BuildArguments` no longer adds `--port` and `--bind-host`. `UpdaterArguments` and `MsiexecArguments` keep the optional arguments, so the update contract does not change.
- **Rationale**: The installer finds the saved values itself (R-005). A forwarded configuration or environment value would be saved as an installer-chosen value. Change is limited (FR-013).
- **Alternatives considered**: Keep forwarding. Rejected because it saves runtime overrides.

### R-007: Invalid saved values

- **Decision**: One shared class `PersistedNetworkSettings` in the service reads and validates both sources. It returns a value per field or `null`, and logs a warning with the reason for each value it ignores. The host setup runs before the logger exists, so the class returns the problems as a list and the host setup writes them to the logger after the host is built (or to `Console.Error` when no logger exists). The same rules apply to the JScript reader (debug log line).
- **Validation**: Port: whole number from 1 to 65535. Host: not empty, no spaces, and no `/` or `:` characters.
- **Rationale**: FR-009. Today a bad port silently becomes `8080`.

### R-008: Saved port is in use

- **Decision**: No change. Kestrel reports the bind error at start-up (existing handling). The installer does not change a saved port (it skips detection when a saved value exists).

### R-009: Shortcut URL (FR-008)

- **Decision**: The start-menu shortcut target in `Directories.wxs` already uses `[PORT]`. Because `PORT` now comes from the same final value that the file stores, the shortcut and the service agree. Keep the target. The task list adds a check that the shortcut uses the final `PORT` for the three scenarios. The host text in the shortcut does not have to equal `BIND_HOST` (spec clarification U4). The rule is: `localhost` for `127.0.0.1` and `0.0.0.0` (a shortcut cannot open `0.0.0.0`), and the chosen host for any other value. This is the existing behavior (`SHORTCUT_HOST` rows). The rule does not change. The rows must run after `BIND_HOST` and `PORT` are final, so they stay in the fixed chain.

### R-011: Change of account (F1, FR-004)

- **Decision**: The file survives a change of Windows account (or elevation) when the data path is the same before and after. The default data root is under `%LOCALAPPDATA%`, which is different for each account. So the update MUST target the data directory of the existing install.
- **Fact from the code**: `MsiexecArguments` already passes `APPLICATIONFOLDER=<existing install folder>`. But the custom action `SetApplicationFolder` (`InstallerProperties.wxs`) sets `APPLICATIONFOLDER` to `[LocalAppDataFolder]GameBot` with no condition, in both sequences, and so overwrites the given value. Under a different account this points to another folder.
- **Change**: Add the condition `APPLICATIONFOLDER = ""` to both `SetApplicationFolder` entries. A given value now stays, and the data path is the one of the existing install.
- **Manual update under another account**: The operator MUST pass `APPLICATIONFOLDER` of the existing install (and the account needs write access to it). Without it, the run uses the default folder of the new account. This is a new data directory, so first-install rules apply and no file is read. This is allowed by the clarification "only the same data directory keeps the values". `INSTALL.md` says it.
- **Registry**: per account. It cannot help for another account. This is the reason for the file.
- **Check**: quickstart V-8.

### R-012: Rollback and cancel (F3, FR-017)

- **Decision**: See R-002. Only deferred actions change the file. The rollback action restores the backup. The commit action removes the backup after success. Immediate actions (read, set `CustomActionData`) never change the file. A cancel before `InstallInitialize` runs no deferred action.
- **Partial write**: a stop between the temporary write and the replace leaves `network.json.tmp` plus a marker. Rollback removes `.tmp` and restores the marker state. The next write also repairs a left-over `.bak` before it starts.
- **Check**: executable `cscript` tests of the rollback and commit functions (FR-019), a source test for the action types, order, and conditions, and quickstart V-9.

### R-013: Executable tests of the installer script (FR-019)

- **Decision**: Split each JScript function into a thin `Session` part and a core function with plain arguments. A test harness runs the real `PortResolver.jscript` with `cscript.exe` against a temporary folder.
- **Rationale**: Source-text tests do not prove behavior. `cscript` is on every Windows machine, and the installer already runs this script type.
- **Alternatives considered**: A mock `Session` object in the harness (more code, and the core function is simpler to test). A full MSI run in CI (not available, see the spec clarification F4).

### R-014: Docs and process (FR-020, FR-022)

- **Decision**: Update `docs/architecture.md` (persistence layout, address priority, updater no longer forwards overrides) and its "Last reviewed" date. Add a row for feature 132 to `specs/STATUS.md` and set the spec Status to Implemented in the last task. Each fix starts with a failing test that is run and kept as proof.
- **Rationale**: Constitution Principles II and V.

### R-010: Documentation

- **Decision**: Update `INSTALL.md`: say that the installer keeps the host and port for update, manual update, and reinstall into the same data directory; say that an explicit `BIND_HOST` or `PORT` option replaces the saved value; say that a manual update under another account must pass `APPLICATIONFOLDER` of the existing install; use the names `BIND_HOST` and `PORT`, because the file lists `BACKEND_PORT` and `WEB_PORT`, which the MSI does not read.
