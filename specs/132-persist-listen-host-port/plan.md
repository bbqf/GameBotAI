# Implementation Plan: Persist the Listen Host and Port

**Branch**: `132-persist-listen-host-port` | **Date**: 2026-10-09 | **Spec**: [spec.md](spec.md)
**Input**: Feature specification from `/specs/132-persist-listen-host-port/spec.md`

## Summary

The installer resets the listen host and port to `127.0.0.1:8080` on update and on reinstall (GitHub issue #65). The plan stores the chosen values in a file, `config\network.json`, in the data directory. The installer writes the file and reads it. The service reads it. The older registry values stay as a fallback. The installer can now tell an explicit option from a default, so an explicit option replaces the saved value (FR-007). The Web UI update stops forwarding runtime overrides to the installer, so overrides are not saved.

Task IDs are only in `tasks.md`. This plan names tasks by their job, not by ID, so the plan cannot hold a wrong ID.

Six design rules come from the corrected spec:

1. **One data path property**: the installer reads the file at `[DATAROOTFOLDER]config\network.json`. `DATAROOTFOLDER` is the data directory (`<install folder>\data`). `CostFinalize` resolves it, so the read action runs `After="CostFinalize"` (FR-016). All rows that use the saved values run after the read action.
2. **Safe write**: the write is a deferred custom action. It gets its values from `CustomActionData`. It has a rollback action and a commit action, so a cancelled or rolled-back install does not change the file (FR-017).
3. **Account change**: the file is found by path. It survives a change of account when the data path is the same. The update MUST target the data directory of the existing install, so `SetApplicationFolder` stops overwriting a given `APPLICATIONFOLDER` (FR-004).
4. **Independent fields**: host and port resolve on their own in the service and in the installer (FR-018).
5. **Executable installer tests**: the JScript read, validate, write, rollback, and commit functions have core versions that take plain arguments (no `Session`). Tests run them with `cscript` against a temporary folder (FR-019).
6. **Test first, docs in scope**: each fix starts with a failing test (FR-022). The change updates `docs/architecture.md`, the spec Status, and `specs/STATUS.md` (FR-020).

## Technical Context

**Language/Version**: C# 12 / .NET 8 (service, updater); WiX Toolset v4 (installer); JScript (MSI custom actions)
**Primary Dependencies**: ASP.NET Core Kestrel host setup; Windows Installer; no new package
**Storage**: JSON file `<data root>\config\network.json`; older registry values `HKCU\Software\GameBot\Network`
**Testing**: xUnit unit tests in `tests/unit`; installer source tests in `tests/integration/Installer`; executable installer-script tests (`cscript.exe` against a temporary folder, see Test strategy); `scripts/installer/install-smoke.ps1`; manual run on a real MSI
**Target Platform**: Windows 10 and 11, per-user MSI install
**Project Type**: Windows service with web UI and MSI/bundle installer
**Performance Goals**: Not applicable. One small file is read at start-up and at install time.
**Constraints**: No new user interface (FR-012). Change to feature 131 is limited (FR-013). First-install defaults and port selection stay as they are (FR-010, FR-011).
**Scale/Scope**: 2 values. About 8 source files, 3 documents, and 6 test files.

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

*NON-NEGOTIABLE*: If `build` or required `test` runs are failing (local or CI), implementation progression is blocked until failures are fixed or a documented maintainer waiver exists.

*NON-NEGOTIABLE*: All text in this plan and in the artifacts it produces (research, data model, contracts, quickstart, tasks, code comments, user-facing messages) MUST obey Simplified Technical English (Constitution Principle VI).

| Gate | Result |
|------|--------|
| Build and tests green before work | Pass. The task list ends with a full build and test run, then the manual MSI check. |
| STE in all new text | Pass. All artifacts use STE. |
| Tests for new behavior (Principle II) | Pass. Failing test first for each fix (FR-022). Unit tests, executable installer-script tests, source tests, smoke check, and a required manual MSI check. |
| Living documentation (Principle V) | Pass. `docs/architecture.md`, spec Status, and `specs/STATUS.md` are updated (FR-020). |
| Minimal change to other features | Pass. Only `UpdaterLauncher.BuildArguments` changes in feature 131. |
| No new UI | Pass. |

Re-check after design: no violation. Complexity Tracking is empty.

## Installer design (resolves spec Planning Notes)

### Data path property and sequence position

- **Property**: `DATAROOTFOLDER` (a Directory id in `Directories.wxs`, child of `APPLICATIONFOLDER`, name `data`). Its value is a full path that ends with `\`. The saved file path is `[DATAROOTFOLDER]config\network.json`.
- **Not used**: `APPLICATIONFOLDER` for the file path, and the MSI property `DATA_ROOT` (it is declared but no element uses it, and its default is `__UNSET__`).
- **Position**: `CostFinalize` resolves the Directory table, so `DATAROOTFOLDER` is valid only after it. The immediate JScript action `ReadPersistedNetworkFile` runs `After="CostFinalize"` in `InstallUISequence` and in `InstallExecuteSequence`. In the UI sequence the dialogs come after `CostFinalize`, so the properties are ready before any dialog shows.
- **Resulting order** (both sequences):

  1. `FindRelatedProducts`, `AppSearch` (sets `WIX_UPGRADE_DETECTED`, `PERSISTED_*` registry values)
  2. `SetApplicationFolder` (only when `APPLICATIONFOLDER` is empty), `WixAppFolder`, `ALLUSERS`, `MSIINSTALLPERUSER`
  3. `CostFinalize`
  4. `ReadPersistedNetworkFile` sets `PERSISTED_FILE_BIND_HOST` and `PERSISTED_FILE_PORT`
  5. `DiscoverBindInterfaces`, `DetectAvailablePorts` (first install only, skipped when a file or registry value exists)
  6. The `SetProperty` rows for `BIND_HOST`, `PORT`, `PROTOCOL`, `SHORTCUT_*`, in a fixed chain
  7. Dialogs (UI sequence)

- The `SetProperty` rows and the two port-detection actions today use `Before="CostFinalize"`. They change to `After=` the previous action in the chain above. Each row gets an explicit `Action` name, so the order is fixed and a source test can check it.
- The value rule for each field is: explicit value (non-empty property) wins; else file value; else registry value; else default or detected port. Host and port use separate rows (FR-018).

### Write action: deferred, with CustomActionData, rollback-safe

| Action | Type | Position | Job |
|--------|------|----------|-----|
| `SetWritePersistedNetworkFileData` | immediate `SetProperty` | `Before="InstallInitialize"`, condition `NOT REMOVE~="ALL"` | Sets property `WritePersistedNetworkFile` to the write data (see the exact data below). |
| `SetRollbackPersistedNetworkFileData` | immediate `SetProperty` | `Before="InstallInitialize"`, condition `NOT REMOVE~="ALL"` | Sets property `RollbackPersistedNetworkFile` to the rollback data. |
| `SetCommitPersistedNetworkFileData` | immediate `SetProperty` | `Before="InstallInitialize"`, condition `NOT REMOVE~="ALL"` | Sets property `CommitPersistedNetworkFile` to the commit data. |
| `RollbackPersistedNetworkFile` | deferred, `Execute="rollback"`, `Impersonate="yes"` | `Before="WritePersistedNetworkFile"`, condition `NOT REMOVE~="ALL"` | Restores the old state (see Rollback below). |
| `WritePersistedNetworkFile` | deferred, `Execute="deferred"`, `Impersonate="yes"` (per-user install) | `After="InstallFiles"`, condition `NOT REMOVE~="ALL"` | See Write below. |
| `CommitPersistedNetworkFile` | deferred, `Execute="commit"`, `Impersonate="yes"` | `After="WritePersistedNetworkFile"`, condition `NOT REMOVE~="ALL"` | Deletes `network.json.bak`, `network.json.none`, and `network.json.tmp`. |

**Exact CustomActionData** (a deferred action cannot read normal properties, so each action gets its own data). In all three strings `[DATAROOTFOLDER]` is the data path. It is a full path that ends with `\`. The data path is the first part in each string, so rollback and commit always receive it (FR-021).

| Property (same name as the action) | Value | Parts |
|-----|-----|-----|
| `WritePersistedNetworkFile` | `[DATAROOTFOLDER]\|[BIND_HOST]\|[PORT]` | 3 parts: data path, host, port. `\|` is the separator. A Windows path cannot hold `\|`. The host and port are the final values of the immediate chain. |
| `RollbackPersistedNetworkFile` | `[DATAROOTFOLDER]` | 1 part: data path. |
| `CommitPersistedNetworkFile` | `[DATAROOTFOLDER]` | 1 part: data path. |

Each action splits its data on `\|`. A write with a part count other than 3, or a rollback or commit with an empty data path, does nothing and writes a debug line (the installer MUST NOT fail, FR-009). Do not put `[BIND_HOST]` or `[PORT]` in the rollback or commit data: they do not need the values.

**Write** (`WritePersistedNetworkFile`, in this order):

1. Create the `config` folder in the data path when it does not exist (FR-021). Creation of the data folder itself (parent) is also done when needed.
2. Validate host and port. A field that is not valid is not written. The old file value for that field stays (FR-018). If neither field is valid and no old file exists, write nothing.
3. Recover from a hard stop of a former run: if `network.json.bak` exists, copy it to `network.json`, then delete the marker files and `network.json.tmp`.
4. Make the undo marker: copy the old `network.json` to `network.json.bak`; if no old file exists, create the empty marker `network.json.none`.
5. Write the full new content to `network.json.tmp` (UTF-8, no byte order mark).
6. Replace: delete `network.json`, then move `network.json.tmp` to `network.json`.

**Rollback** (`RollbackPersistedNetworkFile`, runs on failure or cancel after `InstallInitialize`, also when the write step stopped half way):

- If `network.json.bak` exists: copy it to `network.json` (restores the old file).
- Else if `network.json.none` exists: delete `network.json` (restores "no file").
- Else: the write step did not reach the marker step, so `network.json` is untouched. Do not change it.
- In all cases: delete `network.json.tmp`, `network.json.bak`, and `network.json.none` (the `.tmp` clean-up covers a partial write).
- Rollback runs again without harm (it is idempotent) and never fails the install.

**Commit** deletes the three temporary files only. It never changes `network.json`.

- A cancel before `InstallInitialize` changes nothing, because only deferred actions touch the file.
- Uninstall (`REMOVE="ALL"`) does not run the write step. The MSI does not track the file, so uninstall keeps it (scenario 3).

### Shortcut host (FR-008, spec clarification U4)

- The shortcut MUST use the final `PORT` and a host that reaches the service. The host text does not have to equal `BIND_HOST`.
- Rule: `localhost` when `BIND_HOST` is `127.0.0.1` or `0.0.0.0` (a shortcut cannot open `0.0.0.0`), as the installer does today. Any other host is used as given. `Directories.wxs` and the `SHORTCUT_HOST` / `SHORTCUT_URL` rows already follow this rule. No change to the rule.
- The rows run after `PORT` and `BIND_HOST` are final (they are in the fixed chain), so the shortcut uses the saved values in all three scenarios.
- Test: a source test checks the rows, and an executable check (V-1, V-2, V-3) reads the shortcut URL after each scenario.

### When the file survives an account change

- The file is found by the data path. It does not depend on the Windows account or on elevation. It survives when the data path is the same before and after.
- The default data path is under `%LOCALAPPDATA%`, which is different for each account. A run under a different account with no `APPLICATIONFOLDER` would use its own default folder and find no file. To prevent this:
  - The Web UI update already passes `APPLICATIONFOLDER=<existing install folder>` (`MsiexecArguments`). The custom action `SetApplicationFolder` today overwrites this value with `[LocalAppDataFolder]GameBot`. It gets the condition `APPLICATIONFOLDER = ""`, so a given value stays. The update then targets the data directory of the existing install.
  - A manual update under a different account MUST pass `APPLICATIONFOLDER` of the existing install. Without it, the run is a first install in a new data directory: first-install rules apply and the old file is not read (spec clarification). `INSTALL.md` says this.
- The registry fallback stays per account (HKCU). It does not help for a different account. This is the reason for the file.

## Service design

- `PersistedNetworkSettings` (new, `src/GameBot.Service/Services`): reads `<storage root>\config\network.json`, then the registry. It validates each field alone and returns a value or `null` per field plus a list of problems. It never throws.
- `GameBotServiceSetup.ConfigureWebHostUrls` order: configuration, environment variable, `network.json`, registry, default. Problems go to the log after the host is built (or to `Console.Error` before that).
- `ConfigSnapshotService` uses the same class. The duplicate registry reader is removed.
- `UpdaterLauncher.BuildArguments` no longer adds `--port` and `--bind-host`. The updater command line contract does not change.

## Test strategy

**Order (FR-022, Principle II)**: for each fix, write the failing test first, run it, and keep the failing output (in the task notes or the pull request). Then make the fix and run the test again. This applies to the service reader, the launcher change, each JScript function, and each WiX change.

**Layers**:

| Layer | What it proves | Where |
|-------|----------------|-------|
| Unit | `PersistedNetworkSettings` rules (file, registry, per-field validation, no throw); launcher gives no `--port` or `--bind-host`; priority order in `ConfigureWebHostUrls`. | `tests/unit` |
| Executable installer-script (FR-019) | Behavior of the JScript functions, not source text. | `tests/integration/Installer/PersistedNetworkScriptTests.cs` |
| Source | Sequence position, action types, conditions, separate host and port rows, `SetApplicationFolder` condition, no MSI element tracks `network.json`. | `tests/integration/Installer/PersistedNetworkSettingsInstallerTests.cs` |
| Smoke | File written after install, kept after uninstall. | `scripts/installer/install-smoke.ps1` |
| Manual (real MSI) | The three scenarios, account change, rollback, cancel. | quickstart V-1 to V-9 |

**Executable installer-script tests**:

- Each JScript function has a core function that takes plain arguments and does not use `Session`: `ReadNetworkFileCore(dataPath)` (returns host, port, and problems), `WriteNetworkFileCore(dataPath, host, port)`, `RollbackNetworkFileCore(dataPath)`, `CommitNetworkFileCore(dataPath)`. The `Session` functions (`ReadPersistedNetworkFile`, `WritePersistedNetworkFile`, `RollbackPersistedNetworkFile`, `CommitPersistedNetworkFile`) only split `CustomActionData` or set properties and call the core function.
- The test creates a temporary folder and a small harness script. The harness loads `installer/wix/Scripts/PortResolver.jscript` as text, runs it with `eval`, calls one core function, and prints the result as plain lines. The test starts `cscript.exe //nologo` (from `%SystemRoot%\System32`) and checks the output and the files in the temporary folder. The test is skipped when it does not run on Windows or `cscript.exe` is missing.
- Cases: read with no file, bad JSON, bad port with good host, port as number, unknown fields; write creates the missing `config` folder; write over an old file; write with one invalid field keeps the old value; rollback after an old file (restores it); rollback after "no file" (removes the new file); rollback after a partial write (a `.tmp` file and a `.bak` file, as left by a stop between step 5 and step 6); rollback twice; commit removes `.bak`, `.none`, `.tmp` and keeps `network.json`; `CustomActionData` split with a wrong part count does nothing.
- The harness does not need MSI. It does not replace the manual MSI run, which proves action order and rollback inside the real installer.

## Project Structure

### Documentation (this feature)

```text
specs/132-persist-listen-host-port/
├── plan.md
├── research.md
├── data-model.md
├── quickstart.md
├── spec.md
├── contracts/
│   └── network-settings-file.md
└── tasks.md
```

### Source Code (repository root)

```text
src/GameBot.Service/
├── GameBotServiceSetup.cs                  # priority order, log of problems
└── Services/
    ├── PersistedNetworkSettings.cs         # new shared reader
    ├── ConfigSnapshotService.cs            # use shared reader
    └── Updates/UpdaterLauncher.cs          # stop forwarding overrides

installer/wix/
├── Bundle.wxs                              # empty defaults for BIND_HOST, PORT
├── Product.wxs                             # skip conditions use PERSISTED_FILE_*
├── Scripts/PortResolver.jscript            # read, write, rollback, commit functions and their core functions
└── Fragments/
    ├── InstallerProperties.wxs             # new properties, ordered rows, conditional SetApplicationFolder
    ├── PortDetection.wxs                   # run After the read action
    └── ConfigTemplates.wxs                 # write, rollback, commit actions

tests/
├── unit/Config/PersistedNetworkSettingsTests.cs
├── unit/Updates/UpdaterLauncherTests.cs
├── integration/Installer/PersistedNetworkSettingsInstallerTests.cs   # source tests
└── integration/Installer/PersistedNetworkScriptTests.cs              # executable cscript tests

scripts/installer/install-smoke.ps1
INSTALL.md                                  # only user-visible behavior (FR-015)
docs/architecture.md                        # persistence layout, address priority, updater no longer forwards overrides; new "Last reviewed" date (FR-020)
specs/STATUS.md                             # new row for feature 132 (FR-020)
specs/132-persist-listen-host-port/spec.md  # Status set to Implemented when work is done (FR-020)
```

**Structure Decision**: Keep the existing layout. One new service class, new JScript functions in the existing script, and changes to existing WiX fragments. Three documents change. The `STATUS.md` row and the spec Status change are the last task, after the manual MSI check, so Status says "Implemented" only when the work is done.

## Complexity Tracking

No violation. Table not needed.
