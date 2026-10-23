# Data Model: Files in use dialog fix

This feature stores no new data. The entities below come from the spec. They describe the states that the tests compare.

## Entities

- **Upgrade run**: One run of `GameBotInstaller.exe` or `msiexec`. Fields: start mode (interactive or silent), source build, target build, schedule, log path.
- **Installer session**: A Windows Installer session in a run. A run has one outer session (new product) and, for an upgrade, one nested session (uninstall of the old product). Each session has its own Restart Manager (RM) session and can show the dialog.
- **Dialog event**: One `MsiRMFilesInUse` display. Fields: session, time, owner choice (close, do not close).
- **Locking process**: A process that holds `GameBot.Service.exe` open. Fields: run mode (no window or console window), process id.
- **Install state snapshot**: Old product registration, shortcuts, Run entry, and `network.json`. It is taken before the upgrade and compared after a forced failure.

## Rules

- Dialog events per interactive run MUST be 0 or 1 (FR-001).
- After a forced failure, the snapshot after the run MUST equal the snapshot before the run (FR-002).
- After a successful run, the saved host and port MUST equal the values before the run (FR-003).
