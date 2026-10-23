# Implementation Plan: Installer Bind Address and Port Control

**Branch**: `162-installer-bind-address-port` | **Date**: 2026-10-23 | **Spec**: [spec.md](spec.md)
**Input**: Feature specification from `/specs/162-installer-bind-address-port/spec.md`

## Summary

The installer must show the interface and port choice on every interactive install and upgrade. An upgrade must keep the saved binding and the Start-Menu shortcut address. The host and port must have one saved location and one main name.

The approach has five parts:

1. **One resolver in the MSI.** One new immediate action, `ResolveNetworkBinding`, replaces the chain of `SetProperty` rows, `DetectAvailablePorts` side effects, `updateShortcutHost`, and `NormalizeStartMenuShortcut`. It applies one priority: explicit value, then `network.json`, then a legacy registry value (read once), then the first-install default.
2. **The dialog shows on upgrades.** The `WelcomeDlg` branch that skips the network dialog for an upgrade is removed. The dialog is pre-filled by the resolver.
3. **One shortcut rule.** The shortcut argument uses the derived property `SHORTCUT_URL`. The rule in FR-023 exists in one place.
4. **One saved location.** The registry values `BindHost` and `Port` are no longer written. The commit action removes them after the file is written.
5. **One main name in the bot.** `GAMEBOT_BIND_HOST` and `GAMEBOT_PORT` are the main names. `Service:Network:*` is a documented alias. The bot status shows the active value and its source.

## Technical Context

**Language/Version**: C# on .NET 9 (`net9.0`) for the bot and the updater. WiX v4 authoring (`.wxs`). JScript for MSI custom actions (`PortResolver.jscript`).
**Primary Dependencies**: WiX Toolset v4 (MSI and bundle), ASP.NET Core configuration, `System.Text.Json`. No new dependency.
**Storage**: `<data root>\config\network.json` (fields `bindHost`, `port`). The registry key `HKCU\Software\GameBot\Network` is legacy and is read once for migration.
**Testing**: xUnit tests in `tests/unit`, `tests/integration`, `tests/contract`. JScript core functions run under `cscript` (current `PersistedNetworkScriptTests`). MSI table checks in `tests/integration/Installer`.
**Target Platform**: Windows 10 and 11, per-user install, interactive and silent.
**Project Type**: Desktop background app with an installer, a bot service, and an updater.
**Performance Goals**: The dialog pre-fill takes 3 s or less. The port check takes 2 s or less. The resolver runs `netstat` once for each resolve. The bot start time does not change.
**Constraints**: Per-user MSI. No admin rights. The MSI cannot show the dialog on a silent install. A deferred action cannot read normal properties. The test run must not replace the production install (see Risks).
**Scale/Scope**: One user, one install, one binding. About 12 source files change.

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

*NON-NEGOTIABLE*: If `build` or required `test` runs are failing (local or CI), implementation progression is blocked until failures are fixed or a documented maintainer waiver exists.

*NON-NEGOTIABLE*: All text in this plan and in the artifacts it produces MUST obey Simplified Technical English (Constitution Principle VI).

*Phone check (Constitution Principle III)*: The feature does not add or change a web UI screen. The configuration page shows the same two rows with a new source text. Phone support level: unchanged (the page keeps its current level). No new control is added.

| Gate | Status | Note |
|------|--------|------|
| I. Code quality | Pass | Dead code is removed (`NormalizeStartMenuShortcut`, `updateShortcutHost`, `PERSISTED_*` rows). CamelCase names, no underscores in C# methods. |
| II. Testing | Pass | A test that fails comes first for each fault: dialog skip on upgrade, shortcut host, registry duplicate. Coverage of touched code stays at 80% line and 70% branch or better. |
| III. UX consistency | Pass | Error messages name the port and say how to fix it. The silent-mode argument names `BIND_HOST` and `PORT` do not change. |
| IV. Performance | Pass | Goals are in Technical Context. No hot path changes. |
| V. Living docs | Pass (planned) | `docs/architecture.md` (network settings section), `INSTALL.md`, `docs/phone-access.md`, `CHANGELOG.md`, spec `Status` lines, `specs/STATUS.md`. Feature 132 gets "iterated by 162". |
| VI. STE | Pass (planned) | New text is written in STE. A review step checks it. |
| Version bump | Planned | Raise `patch` in `installer/versioning/version.override.json` before the PR (master `patch` plus 1). |

Post-design re-check: no gate fails. No entry is needed in Complexity Tracking.

## Design Decisions

Full reasons are in [research.md](research.md).

| # | Decision |
|---|----------|
| D1 | One immediate action `ResolveNetworkBinding` sets `BIND_HOST`, `PORT`, and `SHORTCUT_URL`. It runs in the UI sequence after `ReadPersistedNetworkFile`. It runs again on the **Next** click of `InstallDirDlg`. It runs once in the execute sequence, after `ReadPersistedNetworkFile`, with the condition `NOT REMOVE~="ALL"`. The `Set*PersistedNetworkFileData` rows run after it. `CreateShortcuts` runs later, so one execute run is enough. |
| D2 | The `WelcomeDlg` upgrade branch to `VerifyReadyDlg` is removed. Every UI run shows `InstallDirDlg` and then `NetworkConfigDlg`. |
| D3 | `NetworkConfigDlg` **Next** runs `ValidateNetworkBinding`. It rejects an invalid port. It rejects a port that another program holds. A port that the installed GameBot service itself holds is not "in use". |
| D4 | Priority in the resolver: explicit value, `network.json`, legacy registry value (read once), first-install default. The first-install port comes from the current `DetectAvailablePorts` logic, which becomes a pure function used by the resolver. |
| D5 | The shortcut target is `[SHORTCUT_URL]`. `localhost` for `127.0.0.1`. The computer name for all other values. No other code computes this value. |
| D6 | The commit action deletes the legacy registry values `BindHost` and `Port`. The rollback action keeps them. |
| D7 | Silent upgrade with a saved port in use by another program: warn in the installer log, keep the port, end with success. The bot writes the bind failure to its log and exits with a non-zero code. |
| D8 | The updater drops `--port` and `--bind-host`, and `MsiexecArguments` drops `PORT` and `BIND_HOST`. The bot never passed them. |
| D9 | The bot reads the host and port from the environment, the configuration alias, and `network.json`. It does not read the registry. `ResolveListenAddress` returns the source of each value. |
| D10 | The configuration snapshot shows one row for each value (`GAMEBOT_BIND_HOST`, `GAMEBOT_PORT`) with the source from D9. The rows `Service__Network__*` are removed from the defaults. |
| D11 | `MajorUpgrade` uses `Schedule="afterInstallInitialize"`, so the removal of the old product is inside the transaction and a failed upgrade rolls back (FR-012). Found in the Level 2 run (research R13). |

## Project Structure

### Documentation (this feature)

```text
specs/162-installer-bind-address-port/
├── plan.md              # This file
├── research.md          # Phase 0 output
├── data-model.md        # Phase 1 output
├── quickstart.md        # Phase 1 output
├── contracts/
│   ├── installer-properties.md
│   ├── network-file.md
│   ├── runtime-network-config.md
│   └── updater-command-line.md
└── tasks.md             # Phase 2 output (/speckit-tasks - NOT created by /speckit-plan)
```

### Source Code (repository root)

```text
installer/wix/
├── Product.wxs                       # remove upgrade branch at WelcomeDlg; keep custom action refs
├── Bundle.wxs                        # no change to BIND_HOST and PORT variables
├── Fragments/
│   ├── InstallerProperties.wxs       # remove PERSISTED_* searches and SetProperty chain
│   ├── PortDetection.wxs             # one ResolveNetworkBinding action; remove NormalizeStartMenuShortcut
│   ├── NetworkConfigUi.wxs           # pre-fill, validate, remove SHORTCUT_HOST publish rows
│   ├── ConfigTemplates.wxs           # remove registry BindHost and Port; add legacy cleanup on commit
│   └── Directories.wxs               # shortcut argument uses [SHORTCUT_URL]
└── Scripts/PortResolver.jscript      # new resolver and validator; remove dead functions

src/GameBot.Service/
├── GameBotServiceSetup.cs            # ResolveListenAddress: sources, no registry
├── Services/PersistedNetworkSettings.cs   # file only; drop registry reader
└── Services/ConfigSnapshotService.cs # one row per value, with source

src/GameBot.Updater/
├── UpdaterArguments.cs               # drop Port and BindHost
└── MsiexecArguments.cs               # drop PORT and BIND_HOST

tests/
├── unit/Config/                      # ConfigureWebHostUrlsPersistedTests, ConfigSnapshotPersistedNetworkTests
├── unit/Updates/                     # UpdaterProgramTests, UpdaterLauncherTests
├── unit/Installer/                   # InstallerPropertyValidationTests, PortSelectionRulesTests
├── contract/Installer/               # InstallerPropertyContractTests
└── integration/Installer/            # PersistedNetworkScriptTests, PersistedNetworkSettingsInstallerTests,
                                      # UpgradePropertyRetentionTests, InstallerAuthoringScenarioTests (new cases)

scripts/installer/                    # install-smoke.ps1, scope-smoke.ps1, silent-install-examples.ps1
INSTALL.md, docs/architecture.md, docs/phone-access.md, CHANGELOG.md
```

**Structure Decision**: The current layout stays. No new project. One new JScript function pair and one new WiX action replace several old parts.

## Risks

- **Test install replaces the production install.** The MSI is per-user and has a fixed `UpgradeCode`. A test run of the real MSI triggers `MajorUpgrade` and replaces the production product. It also changes `HKCU\Software\GameBot`, the Start-Menu folder, and the autostart entry. The owner has said that a test install to a temporary folder and port is allowed. The owner must approve any step that replaces the production binaries.
  - **Mitigation**: First, test the logic with `cscript` tests and MSI table checks. These steps change no installed product. Second, build a test variant with a different `UpgradeCode` and different names (task in `tasks.md`). Third, if a real run over the production install is needed, stop and ask the owner.
- **Dialog sequence on `InstallDirDlg` Back and Next.** The resolver can run twice. Rule: a value that equals the earlier pre-filled value counts as "not explicit". Tests cover this.
- **The `Source` text set.** The web UI may map known source texts. Task: check the web UI map before the change.

## Complexity Tracking

No violation. The table is not needed.
