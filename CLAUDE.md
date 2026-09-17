When running PowerShell commands, always use absolute paths. Never prepend `cd <dir>;` before a script — use the full absolute path to the script directly, e.g. `& "c:\src\GameBot\.specify\scripts\powershell\setup-tasks.ps1"` not `cd c:\src\GameBot; & ".specify\scripts\powershell\setup-tasks.ps1"`.

For multi-line text passed to a native command — a commit message, a PR body — never use a PowerShell here-string (`git commit -m @'...'@`). Windows PowerShell 5.1 re-splits native-command arguments, so git receives the message body as a pathspec and fails with `error: pathspec '<rest of the message>' did not match any file(s) known to git`. Nothing is committed. Write the text to a file outside the repo (the session scratchpad directory) and pass it by path instead: `git commit -F "<absolute\path\to\msg.txt>"`, `gh pr create --body-file "<path>"`.

When editing UTF-8 files in this repo, prefer the Edit/Write tools over `Get-Content`/`Set-Content`: PS 5.1 reads as ANSI and writes a BOM, which turns em-dashes and arrows into mojibake.

<!-- SPECKIT START -->
For additional context about technologies to be used, project structure,
shell commands, and other important information, read the current plan
at specs/098-resume-queues-on-restart/plan.md
<!-- SPECKIT END -->
