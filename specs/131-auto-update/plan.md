# Implementation Plan: Auto-Update

**Branch**: `131-auto-update` | **Date**: 2026-10-09 | **Spec**: [spec.md](spec.md)
**Input**: Feature specification from `/specs/131-auto-update/spec.md`

## Summary

The owner publishes a GitHub Release from the `release-installer` workflow. The release has the MSI, the bundle EXE, and the file `update-manifest.json`. The manifest has the version and the SHA-256 checksum.

The installed bot reads the latest release from the public GitHub API. This happens when the user clicks "Check for Update". When the user confirms, the bot downloads the MSI itself. A download by the bot has no Mark of the Web, so SmartScreen does not start. The bot checks the SHA-256 checksum. Then it starts a small detached updater program.

The updater waits for the bot to exit. It runs `msiexec` silent. The MSI is per-user, so there is no UAC prompt. The updater starts the bot again and writes a result file. The bot reads the result file at the next start. The UI shows the result. If the install fails, Windows Installer rolls back by itself.

## Technical Context

**Language/Version**: C# on .NET 9 (service, updater); TypeScript with React (web UI); PowerShell and GitHub Actions YAML (release workflow)
**Primary Dependencies**: ASP.NET Core minimal APIs. `HttpClient` for the GitHub REST API and the download. `GameBot.Domain.Versioning` (`SemanticVersion`, `SemanticVersionComparer`). WiX v4 installer: the package is the same, with one new payload folder. Windows Installer (`msiexec`).
**Storage**: Files only. `update-result.json` and the downloaded MSI are in `<data root>\updates\`. There is no new database.
**Testing**: xUnit unit tests for version selection, manifest parse, checksum, both guards, and the state machine. Contract tests for the new routes in `tests/contract`. Jest tests for the UI. A manual check on a real install from `quickstart.md`.
**Target Platform**: Windows 11 and Windows 10, per-user install, x64
**Project Type**: Web application (ASP.NET Core service with a React UI) and one small console program
**Performance Goals**: One "Check for Update" takes 5 s or less on a normal network. The download and install take 5 minutes or less (SC-004).
**Constraints**: No UAC prompt. No SmartScreen prompt in the update step. No code signing. No GitHub token on the user PC. A remote PC cannot start an update (FR-014).
**Scale/Scope**: One user PC for each install. One release channel. About 1 check each day for each install. This is far below the GitHub limit of 60 requests each hour for each IP.

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

*NON-NEGOTIABLE*: If `build` or required `test` runs fail (local or CI), implementation does not continue. A fix or a documented maintainer waiver must come first.

*NON-NEGOTIABLE*: All text in this plan and in the artifacts it produces MUST obey Simplified Technical English (Constitution Principle VI). The artifacts are research, data model, contracts, quickstart, tasks, code comments, and user-facing messages.

| Principle | Status | Note |
|-----------|--------|------|
| I. Code quality | Pass | Each class has one job. Endpoint handlers are named methods, not inline lambdas (taint analyzer rule in `VersioningEndpoints.cs`). Method names have no underscores. |
| II. Testing | Pass | Each unit has tests. Network calls use an injected `HttpMessageHandler`, so tests are deterministic. The process start is behind an interface. The coverage gate (80% line, 70% branch) includes the new `GameBot.Updater` project. |
| III. UX consistency | Pass | Errors use the current `{ error: { code, message, hint } }` shape. Messages give a fix hint. The three routes have OpenAPI tags, names, and response types. |
| IV. Performance | Pass | Goals are in Technical Context. A check is one HTTP call. |
| V. Living documentation | Action | Update `docs/architecture.md` (API surface, capability map, persistence, "Last reviewed" date). Set the `Status` line of this spec and `specs/STATUS.md`. |
| VI. STE | Pass | All artifacts follow STE. |
| Security gate | Pass | Checksum check, install from the bot PC only, https download from `github.com` hosts only, no secrets. See `research.md` R-003, R-004, R-006. |

**Re-check after design**: Pass. The design adds one console project, `GameBot.Updater`. The Complexity Tracking table gives the reason.

## Project Structure

### Documentation (this feature)

```text
specs/131-auto-update/
├── plan.md
├── research.md
├── data-model.md
├── quickstart.md
├── contracts/
│   ├── update-api.md
│   └── release-manifest.md
└── tasks.md             # Created by /speckit-tasks, not by this command
```

### Source Code (repository root)

```text
src/
├── GameBot.Domain/
│   └── Updates/                      # Pure logic: ReleaseInfo, UpdateCheckResult, UpdateState,
│                                     #   UpdateVersionSelector, ChecksumVerifier, UpdateManifest
├── GameBot.Service/
│   ├── Endpoints/UpdateEndpoints.cs  # GET /api/update/status, POST /api/update/check, POST /api/update/install
│   ├── Services/Updates/             # GitHubReleaseClient, UpdateCheckService (check only),
│   │                                 #   UpdateCoordinator (install flow: download, queue stop, launch),
│   │                                 #   UpdateDownloader, UpdaterLauncher, UpdateResultStore,
│   │                                 #   LoopbackGuard, InstallLocationGuard
│   └── Hosted/UpdateResultReportingService.cs   # Reads update-result.json at start
├── GameBot.Updater/                  # New console project: wait for exit, run msiexec, restart, write result
└── web-ui/src/
    ├── pages/UpdatePage.tsx          # "Check for Update" button, result, progress
    └── services/update.ts

installer/wix/                        # Payload gets the updater files through the generated file list
scripts/package-installer-payload.ps1 # Publish GameBot.Updater next to the service (framework-dependent, like the service)
scripts/new-update-manifest.ps1       # Write update-manifest.json from the built MSI
.github/workflows/release-installer.yml  # New publish_release input, manifest step, release step

tests/
├── unit/Updates/                     # Version selection, manifest, checksum, guards, state machine
├── contract/Update/                  # Route contract tests (with the 403 for a remote request)
└── integration/Updates/              # Coordinator with a fake release server and a fake launcher
```

**Structure Decision**: Pure logic is in `GameBot.Domain/Updates`. All I/O is in `GameBot.Service/Services/Updates` behind interfaces. I/O means HTTP, files, and process start. One new console project does the work that must happen after the bot exits.

## Complexity Tracking

| Violation | Why Needed | Simpler Alternative Rejected Because |
|-----------|------------|-------------------------------------|
| New project `GameBot.Updater` | A program cannot replace its own files while it runs. A separate program must wait for the bot to exit. It runs the installer. Then it starts the bot again. | A `cmd.exe` batch string that the bot builds at run time is hard to test and hard to read. It fails on paths with special characters. A copy of the service EXE cannot be the helper, because the service has many files. The updater is framework-dependent like the service. The installer stays small, and the .NET runtime that the bot needs also runs the updater. |
