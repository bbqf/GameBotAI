# Quickstart: Check Installer Bind Address and Port Control

**Safety rule**: Do not run the real MSI over the production install. The per-user MSI shares its `UpgradeCode`, registry key, and Start-Menu folder with the production install. Ask the owner for approval before any step that replaces the production binaries. Use a temporary folder and a temporary port for all other runs.

## Level 1: no installed product changes

1. Build the solution and run the unit tests:

   ```powershell
   dotnet test C:\src\GameBot\GameBot.sln --filter "FullyQualifiedName~Installer|FullyQualifiedName~Config|FullyQualifiedName~Updates"
   ```

2. Run the resolver tests under `cscript`. They are in `tests\integration\Installer\PersistedNetworkScriptTests.cs`. They check the priority order, the legacy registry step, the shortcut host rule, and the port check.
3. Check the MSI tables. The tests in `InstallerAuthoringScenarioTests` read the built MSI and check:
   - the `WelcomeDlg` Next event has no upgrade branch to `VerifyReadyDlg`
   - the `Shortcut` row for `GameBot` uses `[SHORTCUT_URL]`
   - no `CustomAction` named `NormalizeStartMenuShortcut` exists
   - the registry component has no `BindHost` or `Port` value

## Level 2: test variant (isolated)

1. Build the test MSI with a different `UpgradeCode`, product name, Start-Menu folder, autostart name, and registry root. The task list has a task for the build switch.
2. Install it to a temporary folder with a temporary port:

   ```powershell
   & "C:\src\GameBot\artifacts\test\GameBotInstaller.exe" /quiet APPLICATIONFOLDER="C:\Temp\gb-test" DATA_ROOT="C:\Temp\gb-test\data" PORT=18080 BIND_HOST=127.0.0.1
   ```

3. Check the result:
   - `C:\Temp\gb-test\data\config\network.json` has `bindHost` `127.0.0.1` and `port` `18080`.
   - The test Start-Menu shortcut opens `http://localhost:18080/`.
   - The key `HKCU\Software\<test root>\Network` has no `BindHost` and no `Port`.
4. Upgrade in silent mode with no binding. Check that the file, the shortcut, and the active port are the same (SC-002, SC-003).
5. Start an upgrade from the bot API of the test instance. Check the same items (SC-002, FR-011).
6. Run an interactive upgrade. Check that `NetworkConfigDlg` shows `127.0.0.1` and `18080`. Change the port to `18081`. Check the file and the shortcut.
7. Set `BIND_HOST=0.0.0.0`. Check that the shortcut host is the computer name (FR-023).
8. Hold port `18080` with another program and run a silent upgrade. Check that the upgrade succeeds and the installer log has the warning. Check that the bot log has the bind failure and the bot exits with a non-zero code (FR-022). Then stop the other program. Start the test bot on `18080` and run the upgrade again. Check that the installer shows no port warning.
9. Make a pre-132 state: put `BindHost` and `Port` in the test registry key and remove `network.json`. Upgrade. Check that the file has the values and the registry values are gone (FR-018).
10. Force a failed upgrade (lock a payload file). Check that the file, the registry values, and the shortcut are the same as before (FR-012).

## Manual dialog checklist (owner runs it on the isolated test variant)

Use the test variant only. Use a temporary folder and a temporary port.

1. Run the test installer with no argument on a clean state. Check that the pages are `WelcomeDlg`, folder, network, ready. Check that the network page shows `localhost (127.0.0.1)` and the first free port.
2. Enter `0` as the port. Click **Next**. Check that an error shows and the page stays.
3. Enter a port that another program holds. Click **Next**. Check that an error shows and the page stays.
4. Enter a free port such as `18080`. Finish the install. Check the shortcut address.
5. Run the same installer again. Check that the same four pages show, and that the network page shows `127.0.0.1` and `18080`.
6. Go **Back** to the folder page and click **Next** again. Check that the network page keeps any value that you typed.
7. Change the port to `18081`. Finish. Check the file and the shortcut.
8. Report the result for each step.

## Level 3: production install

Do not do this step without approval from the owner. It replaces the active production binaries. Ask first and wait for a clear yes.
