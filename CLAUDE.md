When running PowerShell commands, always use absolute paths. Never prepend `cd <dir>;` before a script — use the full absolute path to the script directly, e.g. `& "c:\src\GameBot\.specify\scripts\powershell\setup-tasks.ps1"` not `cd c:\src\GameBot; & ".specify\scripts\powershell\setup-tasks.ps1"`.

For multi-line text passed to a native command — a commit message, a PR body — never use a PowerShell here-string (`git commit -m @'...'@`). Windows PowerShell 5.1 re-splits native-command arguments, so git receives the message body as a pathspec and fails with `error: pathspec '<rest of the message>' did not match any file(s) known to git`. Nothing is committed. Write the text to a file outside the repo (the session scratchpad directory) and pass it by path instead: `git commit -F "<absolute\path\to\msg.txt>"`, `gh pr create --body-file "<path>"`.

Write all new or changed text in ASD-STE100 Simplified Technical English (STE): specs, plans, tasks, docs, commit messages, PR titles and descriptions, code comments, and user-facing messages. Also use STE in all conversation with the user, not only in files. Code, identifiers, commands, paths, tool output, and quotations stay as they are. See Principle VI in `.specify/memory/constitution.md` for the full rules.

When editing UTF-8 files in this repo, prefer the Edit/Write tools over `Get-Content`/`Set-Content`: PS 5.1 reads as ANSI and writes a BOM, which turns em-dashes and arrows into mojibake.

Before you push a branch for a new PR, bump the version in `installer/versioning/version.override.json` (constitution, "Version bump"). Run `git fetch origin master` and compare the file with `origin/master`. If `minor` or `major` changed, set `patch` to `"0"`. If not, set `patch` to the `patch` on `origin/master` plus 1. Do nothing if the file already has this value. Set `updatedBy` and `updatedAtUtc`. Do this on your own, with no prompt.

<!-- SPECKIT START -->
For additional context about technologies to be used, project structure,
shell commands, and other important information, read the current plan
at specs/173-queues-ui-cleanup/plan.md
<!-- SPECKIT END -->
