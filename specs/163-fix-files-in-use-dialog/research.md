# Research: Second "Files in use" Dialog in an Interactive Upgrade

Source: the upgrade log set `C:\TEMP\gb-logs\GameBot_20261023172358*.log` (production upgrade 1.8.0.243 to 1.11.16.263, bot running in the background). All times are local (17:2x).

## R1. What the log shows (facts)

| Time | Session | Event |
|------|---------|-------|
| 17:24:06 | bundle | One `MsiPackage` run: `Applying execute package: GameBotMsi, action: Install`. The bundle starts the MSI one time only. |
| 17:24:46 | outer MSI (new product) | `InstallValidate` opens a Restart Manager (RM) session and shows `MsiRMFilesInUse`. **Dialog 1.** |
| 17:26:15 | outer MSI | The owner chose "Close". `ShutdownApplications`: "Successfully shut down all applications". |
| 17:26:45 | outer MSI | Message 1611: "unable to automatically close all requested applications". The 30 s gap shows that a process still held a file. |
| 17:27:18 | outer MSI | "The user chose to go on ... although a reboot will be required." `InstallInitialize`, then `RemoveExistingProducts`. |
| 17:27:18 | nested MSI (old product, `REMOVE=ALL`, `UPGRADINGPRODUCTCODE=...`) | New RM session. Its own `InstallValidate` shows `MsiRMFilesInUse` again (`FileInUseProcess` = 34880). **Dialog 2.** |
| 17:28:08 | nested MSI | The owner chose "Close". RM shut down the applications in 1.2 s. The nested uninstall continues. |
| 17:28:11 | outer MSI | "Previously shut down applications have been restarted." The bot runs again. |
| 17:28:13 | bundle | Removal of the old bundle `{CCAE54B6-...}` takes less than 1 s and shows no dialog. Then a second try fails with 0x80070003 (non-vital, "Continuing..."). |

## R2. Decision: the cause is the nested uninstall of the old product

- **Decision**: Dialog 2 comes from the uninstall of the old product that `RemoveExistingProducts` starts. That uninstall is a separate Windows Installer session with its own RM session and its own `InstallValidate`. It checks the locked files again.
- **Rationale**: The log lines above. The bundle does not start the MSI two times (R1, 17:24:06). The old bundle removal (the owner's first hypothesis) shows no dialog and runs after the MSI work. This rejects that hypothesis.
- **Open point**: The outer RM did not close the bot completely (message 1611 after a successful shutdown). So the nested session found a lock again. The owner says the fault can show only when the bot runs in the background with no console window. A process with no window cannot get a close request from RM, so RM can need more than 30 s or fail. We must check this on the isolated copy (R6, step E2).

## R3. Hypotheses that the isolated tests must separate

| ID | Statement | How to test |
|----|-----------|-------------|
| H1 | Any schedule shows a nested dialog while the bot runs, because the nested session has its own RM check. | Run E1 with each schedule (R5). |
| H2 | The nested dialog shows only because the outer RM did not close the bot completely (1611). If the outer RM closes the bot, the nested check finds no lock. | Run E2: compare a bot with a console window and a bot with no window. |
| H3 | The `afterInstallInitialize` schedule (feature 162) is the reason. The old schedule shows only one dialog. | Run E1 with `afterInstallValidate`. |

## R4. Candidate changes (smallest first)

| ID | Change | Keeps rollback (FR-002)? | Risk |
|----|--------|--------------------------|------|
| C1 | Make the outer RM close the bot fully, so the nested check finds no lock (if H2 is true). Example: a `util:CloseApplication` entry for `GameBot.Service.exe`, or a safe stop request in the product. | Yes. No schedule change. | `CloseApplication` ends the process without a dialog. The owner said "one choice", so it must keep one dialog or ask first. Check. |
| C2 | Another `MajorUpgrade` schedule (`afterInstallExecute`, `afterInstallExecuteAgain`, `afterInstallValidate`). | `afterInstallValidate` breaks it (feature 162, R13). `afterInstallExecute` was rejected in R13 (the new files are copied first). | High. Test only to compare the dialog count. |
| C3 | A custom action that stops the bot before `InstallValidate` and runs only after the owner agrees. | Yes, if the stop is rolled back by the Restart Manager restart. | A custom action in the interactive path. More code than C1. |
| C4 | `MSIRESTARTMANAGERCONTROL=Disable` for the new package. | Yes. | This switches off the one dialog we want to keep. The old product (nested session) is not changed by this property. Likely not enough alone. |

- **Decision**: Test C1 first (it is the smallest change and does not change the schedule). Use E1 and E2 results to confirm. Use C3 only if C1 fails. Use C2 only as a comparison, not as the fix (R13 of feature 162).
- **Alternatives rejected now**: going back to the default schedule (breaks FR-002); closing the bot with code in the Web UI update path (out of scope).

## R5. Experiment plan (isolated copy only)

Use `scripts/installer/new-test-variant.ps1` for every run (own upgrade code, temporary folder, temporary port). Do not run the real MSI over the production install (memory note: installer-isolated-test-variant). Create and start `msiexec` in separate shell calls. Publish the branch bot from `src/GameBot.Service` (not from `installer\wix\payload`).

| Step | Run | Result to record |
|------|-----|------------------|
| E0 | Silent upgrade with the bot running (baseline). | Success, bot restarted, binding kept. |
| E1 | Interactive upgrade (owner) with the current schedule, a locking background process, and a verbose log (`/l*v`). One more run for each schedule in C2. | Number of `MsiRMFilesInUse` dialogs per run. Action that raises each. |
| E2 | Same as E1 with a locking process that has no window, then one with a console window. | If the run mode changes the number of dialogs or message 1611. |
| E3 | Upgrade from a 1.11.16.x test build to a newer test build. | Dialog count between recent builds. |
| E4 | Candidate fix (C1, then C3 if needed) with E1 and E2. | One dialog. Bot closed and restarted. |
| E5 | `-FailAfterInstallFiles` with the fix. | Old product, shortcuts, Run entry, and `network.json` come back (SC-002). |

An interactive dialog needs the owner. The agent prepares the copy, the locking process, and the log search. The owner clicks.

## R6. Side findings (not in scope)

- The second try to remove the old bundle fails with 0x80070003 because the cached `GameBotInstaller.exe` of the old bundle is not there any more. The bundle marks it non-vital and continues. It does not affect the dialog count. Record it in the final notes. Do not fix it here.
- `RebootPending = 1` in the bundle log comes from the "go on although a reboot will be required" choice. A clean single close must not set it.
- The `MsiRMFilesInUse` dialog has two layout errors (error 2826, 4 pixels). They are a WiX dialog size fault and are not in scope.

## Test base (T001, T002)

- The build ran in a scratch copy of the repo (outside the repo tree), version 1.11.16.900. The branch bot is in the payload of that MSI, so a separate `dotnet publish` was not needed.
- Test copies: instance `T163`, files in `C:\TEMP\gb163`. `GameBot.T163A.msi` (old), `GameBot.T163B.msi` (new, revision B), `GameBot.T163F.msi` (forced failure after `InstallFiles`). Install folder `C:\TEMP\gb163\app`, host `127.0.0.1`, port `18163`.
- The saved binding is in `C:\TEMP\gb163\app\data\config\network.json`.

## Results

| Run | Dialogs | 1611 | Reboot choice | Note |
|-----|---------|------|---------------|------|
| Production upgrade log (1.8.0.243 to 1.11.16.263), bot in background | 2 | 1 | 1 | Checked with `test-files-in-use-dialog.ps1 -Action Count`. |
| E0: silent upgrade A to B, hidden test bot running | 0 | 0 | 0 | Success (exit 0). The RM log says "restarted", but no test bot listened on port 18163 after the run. See the open point below. |

| E1 try 1: interactive A to B (same file versions, 1.11.16.900 both), hidden test bot, current schedule | 1 | 0 | 0 | Owner saw one dialog. In the log the outer `InstallValidate` (18:18:17) showed none; only the nested uninstall session of the old product showed the dialog. See R8. |

| E1 try 2: interactive A899 to B (file versions differ), hidden console-less test bot, current schedule | 1 | 0 | 0 | Owner saw one dialog. Outer `InstallValidate` showed it (18:22:51). RM closed the bot (18:22:53). The nested uninstall (18:22:54) found no lock and showed none. The new bot ran again (new process id). **The fault did not reproduce.** |

**R9. What this changes.** The nested uninstall shows a second dialog only if a process still holds a file after the outer RM close. In the production log this happened (message 1611 after a "successful" shutdown, and `FileInUseProcess` 34880 in the nested session). On the test copy it did not. The test bot differs from the production bot in these ways, so the next runs must change them one by one: (1) a minimized console window (the app starts the bot with `start /min`), (2) a real autostart or shortcut start, (3) a bot that serves a game session (adb, OCR child processes), (4) a long wait before the first click (89 s in production). Ask the owner which processes the production dialog listed. The spec says both dialogs list `GameBot.Service`.

| E2 model: interactive A899 to B, minimized-console test bot, **frozen** (cannot answer a close request), current schedule | 2 | 2 | 2 | **The fault reproduced.** Owner saw "Unable to close the applications" after dialog 1, then dialog 2 with the same result. Install ended with success and offered to start the bot. The frozen bot lived through both checks. Log: `upgradeB3-frozen.log`. |

**R10. Confirmed cause.** The first close request does not end the bot (the owner's note: this happens when a queue runs, and the bot is slow to stop). Restart Manager reports "shut down", but 30 s later Windows Installer finds the lock (message 1611) and the owner chooses to go on. Then `RemoveExistingProducts` starts the nested uninstall of the old product. It opens its own Restart Manager session and checks again. The bot still lives, so dialog 2 shows. This is the same timeline as the production log (R1). The schedule, the bundle, and the old bundle removal are not the cause. A bot that stops at the first request (E1 try 2) gives one dialog.

**R11. Chosen candidate (C3 variant).** A custom action in the new product, before `RemoveExistingProducts`, ends the bot of this install folder only when the owner chose "Close the applications" (`WixUIRMOption = UseRM`) in an upgrade (`WIX_UPGRADE_DETECTED`). It ends only processes whose path equals `[APPLICATIONFOLDER]GameBot.Service.exe` (an isolated test copy never ends the production bot). Then the nested uninstall finds no lock and shows no dialog. It works for the first upgrade from today's installed build because the action is in the new (outer) product. A silent run leaves `WixUIRMOption` unset, so the action does not run (FR-004). The action does not need the Web UI path (FR-008).

| Fix try 1 (invalid): interactive A899 to a "fix" MSI whose payload was also 899, frozen bot | 0 | 0 | 0 | No file was replaced, so the outer check found no lock and no dialog showed. But the action **ended the bot without any dialog**, because `WixUIRMOption` is `UseRM` from the start. Result: the consent check must not use `WixUIRMOption` alone. |

| Fix try 2: interactive A899 to fix MSI with a custom action and `GB_CLOSE_CONSENT`, frozen bot | 2 | 2 | 2 | **Failed.** The log shows `GB_CLOSE_CONSENT=1` set in the client session (18:53:10), but the server skipped the action ("condition is false" at 18:53:43). A property that the client sets during `InstallValidate` does not reach the server session in the same sequence. |

**R13. Documented mechanism (internet research).** Sources: [Windows Installer team blog, "MSI Files-In-Use V2"](https://learn.microsoft.com/en-us/archive/blogs/windows_installer_team/windows-installer-and-restart-manager-msi-files-in-use-v2), [MSIRMSHUTDOWN](https://learn.microsoft.com/en-us/windows/win32/msi/msirmshutdown), [MSIRESTARTMANAGERCONTROL](https://learn.microsoft.com/en-us/windows/win32/msi/msirestartmanagercontrol), [RemoveExistingProducts](https://learn.microsoft.com/en-ie/Windows/Win32/msi/removeexistingproducts-action).

- All files-in-use detection and shutdown happens in `InstallValidate`. Each Windows Installer session (the new install and the nested uninstall of the old product) has its own Restart Manager session. This matches the two dialogs.
- Step 9 of the blog: if Restart Manager cannot shut down a process, Windows Installer shows message 1611. A process may stay alive "if the process doesn't respond to WM_ENDSESSION or WM_CLOSE messages". Our bot is a console program with no window (subsystem 3), and a queue makes it slow to stop. This is the production timeline (R1, R10).
- `MSIRMSHUTDOWN`: value 0 shuts down processes that answer. **Value 1 forces a shutdown of processes that do not respond to Restart Manager.** Value 2 shuts down only when all processes registered for restart.
- `MSIRESTARTMANAGERCONTROL` is only a switch between Restart Manager and the old dialog. It does not remove the second check, and custom actions cannot change it. Not suitable.
- `RemoveExistingProducts` has its own nested install, and the log of the nested install goes into the enclosing log. Its schedule options do not remove the second check.

**R14. New decision (replaces R11 and R12).** Set the property `MSIRMSHUTDOWN=1` in `Product.wxs`. No custom action and no consent property. Restart Manager forces the shutdown only after the owner chose "Close the applications" (the shutdown step runs after that choice). The bot is gone before the nested uninstall, so it finds no lock. It works for the first upgrade from today's build because the property is in the new (outer) product. In a silent run Restart Manager shuts down automatically, as before, and now also forces it.

| Fix try 3: interactive A899 to fix MSI with `MSIRMSHUTDOWN=1`, frozen minimized bot | 1 | 0 | 0 | **Passed.** Owner saw one dialog (log: `upgradeFix3.log`). RM force-closed the frozen bot (18:58:09). The nested uninstall (18:58:39) found no lock and showed none. A new bot ran again on `127.0.0.1:18163` with the saved binding (FR-003). |
| Rollback, baseline: failing upgrade (`-FailAfterInstallFiles`) with the MSI from before the fix | exit 1603 | | | Rollback log: 72 registry operations, Run entry, product info, and 46 file operations restored. |
| Rollback, with fix (`upgradeF-fail.log`) | exit 1603 | | | **Identical** counts and operations as the baseline. SC-002 holds as in feature 162. |
| Silent upgrade A899 to fix MSI, hidden bot running | 0 | 0 | 0 | Exit 0. Files updated to .900. The saved binding stayed. As in the baseline (E0), the silent run did not start the test bot again. |

**Note on the snapshot function.** After a rolled-back upgrade, `Get-TestInstallSnapshot` shows no product and no Run value, in the baseline too. The rollback log shows that the installer restored both (`RegAddValue` of the Run entry, `ProductInfo`). This is the shell registry view limit in the memory note `installer-test-shell-registry-view`. For the rollback check, compare the rollback operations in the log, and use the snapshot for files, shortcuts, and `network.json` only.

**R12 (superseded). Consent check.** The action now runs only if the property `GB_CLOSE_CONSENT` is `1`. A `Publish` on the OK button of the Restart Manager dialog (`MsiRMFilesInUse`) sets it when the owner chose "Close the applications" (`WixUIRMOption~="UseRM"`). So the bot is ended only after the owner saw the dialog and agreed. ICE63 does not allow an action between `InstallInitialize` and `RemoveExistingProducts`, so the action runs after `InstallValidate`.

**R8. The files must differ.** The outer check lists only files that the new product will replace. When old and new have the same file versions, the outer check finds no lock and only the nested uninstall shows a dialog. The production upgrade changed files, so both checks found locks. A valid test needs an old build with a lower file version. New test base: `GameBot.T163A899.msi` (1.11.16.899) as old, `GameBot.T163B.msi` (1.11.16.900) as new.

**Open point (E0):** After a silent upgrade the test bot did not run again. The production run restarted the bot. A likely reason is that RM restarts the process without the test environment variables (`GAMEBOT_DATA_DIR`, URLs). Check this in E4 before you judge FR-003.

## R7. Documentation to update

`INSTALL.md` (interactive upgrade notes), `installer/README.md` (test steps), `CHANGELOG.md`, `specs/STATUS.md`. `docs/architecture.md` only if the product definition changes (update "Last reviewed").
