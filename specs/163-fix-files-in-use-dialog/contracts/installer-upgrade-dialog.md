# Contract: Interactive upgrade dialog behavior

This contract states what the owner sees and what the installer log must show.

## Owner-visible behavior (interactive upgrade, bot running)

| Step | Result |
|------|--------|
| The owner starts `GameBotInstaller.exe` and finishes the pages. | The "Files in use" dialog shows at most one time. |
| The owner chooses "Close the applications and attempt to restart them". | The bot closes. The upgrade ends with success. The bot starts again. No second dialog. No "reboot required" message. |
| The owner chooses "Do not close". | The installer does not show the same dialog again for the same files in the same run. |
| The owner cancels. | The old product stays installed and works. |

## Log checks (`/l*v` log of the MSI)

- The count of `MsiRMFilesInUse` dialog displays per run is 0 or 1. The test script counts the lines `RESTART MANAGER: Session opened` that are followed by a `FileInUseProcess` property in the same run.
- No line "The user chose to go on with the installation, although a reboot will be required".
- No Windows Installer message 1611.

## Unchanged

- Silent upgrade (`/qn`, `/quiet`): no dialog. Exit code 0 when the bot is stopped first.
- Update from the Web UI or the API: the updater waits for the bot to exit before it starts the installer.
- Pages, saved binding, and shortcut rule of feature 162.
