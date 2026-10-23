# Quickstart: Check the "Files in use" dialog on an isolated copy

Do not run the real MSI over the production install. A real upgrade of the production install is Level 3 and needs approval from the owner.

1. Publish the branch bot to a temporary folder (do not use `installer\wix\payload`):
   `dotnet publish src/GameBot.Service` with `GAMEBOT_DATA_DIR` set to a temporary folder.
2. Build the MSI. Make two test variants with `scripts/installer/new-test-variant.ps1`: an old build and a new build with the same own upgrade code, a temporary folder, and a temporary port. Create the package and start `msiexec` in separate shell calls.
3. Install the old variant with `msiexec /i <old.msi> /qn`.
4. Start a locking process from the test folder that holds `GameBot.Service.exe` open. Use a process with no window first. Then repeat with a console window.
5. Owner step: run `msiexec /i <new.msi> /l*v <log>` (interactive). Count the dialogs. Choose "Close the applications" one time.
6. Search the log: lines `MsiRMFilesInUse`, `RESTART MANAGER`, `FileInUseProcess`, `1611`, `reboot will be required`. Expect one dialog and no 1611.
7. Check the new bot runs again and uses the saved host and port.
8. Rollback check: run `new-test-variant.ps1 -FailAfterInstallFiles` and compare the old product, shortcuts, Run entry, and `network.json` before and after (SC-002).
9. Silent check: repeat step 5 with `/qn` and no locking process. Expect success.
10. Remove the test products with `msiexec /x <ProductCode>`, then delete the test Start-Menu folder and the test Run value.
11. Run all existing installer tests (SC-003).
