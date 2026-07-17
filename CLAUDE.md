When running PowerShell commands, always use absolute paths. Never prepend `cd <dir>;` before a script — use the full absolute path to the script directly, e.g. `& "c:\src\GameBot\.specify\scripts\powershell\setup-tasks.ps1"` not `cd c:\src\GameBot; & ".specify\scripts\powershell\setup-tasks.ps1"`.

<!-- SPECKIT START -->
For additional context about technologies to be used, project structure,
shell commands, and other important information, read the current plan
at specs/070-ensure-emulator-running/plan.md
<!-- SPECKIT END -->
