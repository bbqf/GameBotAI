# Quickstart: check the feature

Use a test Windows account. Use a data directory with no real data.

## Automated checks

1. `dotnet test` for the unit tests of `PersistedNetworkSettings` and the updated `UpdaterLauncher` test.
2. `dotnet test` for the executable installer-script tests in `tests\integration\Installer\PersistedNetworkScriptTests.cs`. They run the read, write, rollback, and commit functions with `cscript.exe` against a temporary folder.
3. `dotnet test` for the installer source tests in `tests\integration\Installer` (order of `SetProperty` rows, new actions, new properties, empty defaults in the bundle).
4. `scripts\installer\install-smoke.ps1`: the upgrade check also reads `config\network.json`.

For each fix, the failing test is run and shown to fail before the fix (FR-022).

## Manual checks (real MSI)

Build the installer, then do each step in order V-1 to V-9. Use host `0.0.0.0` and port `9090` for the first install. After each scenario (V-1, V-2, V-3), also check the Web UI shortcut: it MUST use the same port as the service and the host `localhost` (the shortcut cannot open `0.0.0.0`).

- **V-1 Update from the Web UI (US1)**: Install an older build. Start the update from the Web UI. After the restart, check that the service listens on `0.0.0.0:9090` and that the start-menu shortcut opens `http://localhost:9090/`. Read the msiexec log: `PERSISTED_FILE_PORT` is `9090`.
- **V-2 Manual update (US2)**: Run the newer installer with no host or port option. Check the same result and the shortcut. Then run it with `PORT=9091`. Check that the service uses `9091`, that the shortcut opens `9091`, and that a third run with no option keeps `9091`.
- **V-3 Reinstall (US3)**: Uninstall. Check that `config\network.json` is still in the data directory. Install again with the same folder and no option. Check `0.0.0.0:9090` and the shortcut. Install into a new folder: check that the first-install defaults apply.
- **V-4 First install (SC-005)**: With no saved value, check that the dialog shows `127.0.0.1` and the first free port from `8080,8088,8888,80`.
- **V-5 Older install (SC-004)**: Use an install that has only the registry values (delete `network.json`). Update. Check that the values stay and that the file is written.
- **V-6 Bad value (FR-009)**: Put `"port": "abc"` in the file. Start the service. Check that it listens on the next source and that the log has one warning.
- **V-7 Override**: Set `GAMEBOT_PORT=7000`. Start the service. Check that it uses `7000`. Run an update from the Web UI. Check that `network.json` does not change to `7000`.
- **V-8 Different account (FR-004)**: Install under account A with `0.0.0.0:9090` into a folder that account B can write to (for example `C:\GameBotShared`). Log on as account B. Run the newer installer with `APPLICATIONFOLDER=C:\GameBotShared` and no host or port option. Check that `network.json` is read (msiexec log: `PERSISTED_FILE_PORT` is `9090`) and that the service listens on `0.0.0.0:9090`. Then run it as account B with no `APPLICATIONFOLDER`. Check that it uses the default folder of account B, finds no file, and applies the first-install rules. This second result is expected.
- **V-9 Rollback and cancel (FR-017, FR-021)**: Install with `0.0.0.0:9090`. Run the newer installer with `PORT=9191` and make it fail after `WritePersistedNetworkFile`. Check that `network.json` still has `9090` and that no `.bak`, `.none`, or `.tmp` file is left. Then cancel an interactive run on a dialog: check that the file is not changed.
  - **How to make the failing test MSI**: build the normal MSI with a test-only change: add a deferred custom action `FailAfterPersistedNetworkWrite` (for example a JScript action that raises an error) with `After="WritePersistedNetworkFile"` and the same condition. Build it into a separate MSI file in a scratch folder. Never commit this change. Run it with `msiexec /i <test msi> PORT=9191 /l*v <log>`. The error causes a rollback, which runs `RollbackPersistedNetworkFile`.
  - **If not possible**: when the test MSI cannot be built or run (for example no WiX build tools or no clean test machine), write "not possible" and the reason in the pull request. The executable `cscript` tests of rollback (FR-019) and the source test of the action order are then the only proof for rollback. Do the cancel part of V-9 anyway if an interactive install can run.
