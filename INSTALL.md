# GameBot Installer Usage Guide

This document explains how to run the GameBot Windows installer and all currently supported installer options.

## Scope

- Platform: Windows x64
- Installer type: Bootstrapper EXE that installs MSI payload(s)
- Modes: interactive wizard and silent/unattended install
- Installation scope: per-user only
- ARP visibility: current-user install appears in Windows Installed Apps

---

## 1) Quick start

### Interactive install

1. Locate the installer executable (for example, `GameBotInstaller.exe`).
2. Run the installer as the current user.
3. Complete the wizard prompts.
4. Verify installation:
  - Installed Apps entry is present for `GameBot`
   - App files are installed
   - Data directory exists and is writable
  - Installer log exists under `%LocalAppData%\GameBot\Installer\logs`

### Silent install

Use `/quiet` with installer variables:

```powershell
.\GameBotInstaller.exe /quiet MODE=backgroundApp SCOPE=perUser DATA_ROOT="%LocalAppData%\GameBot\data" PORT=8080 BIND_HOST=0.0.0.0 PROTOCOL=http ENABLE_HTTPS=0 ALLOW_ONLINE_PREREQ_FALLBACK=1
```

### Getting installer binaries from CI

- Installer packages are published by the `release-installer` workflow as GitHub Actions artifacts.
- The workflow runs only when you start it by hand (`workflow_dispatch`). A push or a PR merge does not start it.
- Artifact name format:

  ```text
  GameBotInstaller-v<version>-<branch>-run<github.run_number>
  ```

- Download the artifact from the workflow run summary, then use `GameBotInstaller.exe` for install/upgrade.

---

## 2) Installer variables (all supported options)

These variables are supported by the bootstrapper and forwarded to MSI.

| Variable | Type | Default | Allowed values | Required | Description |
|---|---|---:|---|---|---|
| `MODE` | string | `backgroundApp` | `backgroundApp` | Yes | Runtime mode to configure. |
| `SCOPE` | string | `perUser` | `perUser` | Yes | Installation scope. |
| `DATA_ROOT` | string (path) | empty (resolved to per-user path) | Writable path | No | Runtime data path override. |
| `PORT` | integer | empty (saved value, or first free of `8080,8088,8888,80` on a first install) | `1..65535` | No | Port of the service and the Web UI. An explicit value replaces the saved value. |
| `BIND_HOST` | string | empty (saved value, or `127.0.0.1` on a first install) | IPv4/hostname (for example `0.0.0.0`, `127.0.0.1`) | No | Backend bind interface/host. An explicit value replaces the saved value. |
| `PROTOCOL` | string | `http` | `http`, `https` | Yes | Endpoint protocol. |
| `ENABLE_HTTPS` | boolean-ish string | `0` | `0`/`1` | No | Enable HTTPS validation path. |
| `CERTIFICATE_REF` | string | empty | Certificate identifier | Conditionally | Required when `ENABLE_HTTPS=1`. |
| `ALLOW_ONLINE_PREREQ_FALLBACK` | boolean-ish string | `1` | `0`/`1` | No | Allow online prerequisite fallback from allowlisted sources. |

Notes:
- `DATA_ROOT` defaults to `%LocalAppData%\GameBot\data` when omitted.
- Install root defaults to `%LocalAppData%\GameBot`.

### Interface and port choice

Every interactive install and upgrade shows the pages `WelcomeDlg`, the install folder, and the network settings.
The network page has the **Bind interface** list and the **Port** field.

- A new install pre-fills `localhost (127.0.0.1)` and the first free port of `8080,8088,8888,80`.
- An upgrade pre-fills the saved interface and port. Click **Next** to keep them.
- The page checks the port when you click **Next**. A port that is not a whole number from 1 to 65535 gives an error. A port that another program uses also gives an error. The page stays until the value is valid.
- The installed GameBot service itself can use the saved port during an upgrade. The page does not count that as "in use".
- A silent install or upgrade shows no page. The same rules apply to the command-line options.

### Saved host and port

The installer saves the host and port in `config\network.json` in the data directory. This is the only saved location.
Only the installer writes the file. The installer and the service read it.
The installer keeps these values for:

- an update from the Web UI or the API (the update passes no host and no port),
- a manual update with a newer installer,
- an uninstall and a new install into the same data directory.

Rules:
- If you give `BIND_HOST` or `PORT` to the installer, your value replaces the saved value. The installer saves the new value.
- If you give no option, the installer uses the saved value. The installer does not look for a free port again.
- An older install (before the file existed) kept the host and port in the registry key `HKCU\Software\GameBot\Network`. The installer reads these values once, saves them in `network.json`, and deletes them from the registry after a successful install. A failed install keeps them.
- The uninstall keeps `config\network.json`.
- A new install into a different folder has a different data directory. The first-install rules apply there.
- A manual update under a different Windows account must pass `APPLICATIONFOLDER` of the existing install. Without it, the installer uses the default folder of that account. It finds no saved file, and the first-install rules apply.
- A runtime override is not saved. The Web UI update does not pass it to the installer. The service names are `GAMEBOT_BIND_HOST` and `GAMEBOT_PORT`. `Service:Network:BindHost` and `Service:Network:Port` are aliases. The configuration alias wins over the environment variable, and the environment variable wins over `network.json`.
- The service ignores a saved value that is not valid, writes a warning to the log, and uses the next source.
- The **Configuration** page of the Web UI shows the active host and port with their source (`Configuration`, `Environment`, `NetworkFile`, or `Default`). The two rows are read-only.

### Start-Menu shortcut address

The `GameBot` shortcut opens `PROTOCOL://host:port/` with the host and port in use after the install.
The host is `localhost` for `127.0.0.1`. For all other values, the host is the computer name. The value `0.0.0.0` is one of them.
A new install, an upgrade, and an update from the Web UI or the API all use this rule. The `GameBot Background` shortcut has no address.

### A silent upgrade when the saved port is in use

The upgrade ends with success and keeps the saved port. The installer log has the line `WARNING: port <n> is in use. The saved port is kept.`
The service cannot start its web server then. It writes an error to its log (host, port, and the source of each) and ends with exit code 98.
Stop the other program, or change the port with the installer.

To open the web UI on a phone, set `BIND_HOST` to a network address, set an access token, and open the firewall port. See [docs/phone-access.md](docs/phone-access.md).

---

## 3) Mode/scope constraints

- Supported mode: `backgroundApp`
- Supported scope: `perUser`
- Supported installation channel for users: bootstrapper EXE (`GameBotInstaller.exe`)
- Startup behavior:
  - Registers autostart in `HKCU\Software\Microsoft\Windows\CurrentVersion\Run`
  - Adds a Start Menu shortcut `GameBot Background` for manual start
- Start Menu shortcut `GameBot` only opens the web UI URL and does not start backend processes.

### Upgrade policy

- Upgrade using the bootstrapper EXE only.
- Do not deploy raw MSI directly for end-user installs.
- Installing both MSI and EXE channels on the same machine can create side-by-side registrations and confusing uninstall behavior.

---

## 4) HTTPS behavior

- Default is HTTP (`PROTOCOL=http`, `ENABLE_HTTPS=0`).
- To enable HTTPS:
  - Set `PROTOCOL=https ENABLE_HTTPS=1`
  - Provide `CERTIFICATE_REF=<certificate-id>`
- If HTTPS is enabled without a certificate reference, validation must fail with remediation guidance.

Example:

```powershell
.\GameBotInstaller.exe /quiet MODE=backgroundApp SCOPE=perUser PROTOCOL=https ENABLE_HTTPS=1 CERTIFICATE_REF="thumbprint:ABCDEF123456" DATA_ROOT="%LocalAppData%\GameBot\data"
```

---

## 5) Port behavior

- Deterministic preference order for a first install: `8080 -> 8088 -> 8888 -> 80`
- The installer reads `netstat -ano -p tcp` and counts only the listening lines. It compares the port with the local address. A port in a foreign address or in a closed connection does not count.
- For each listener, the installer reads the program name and path. The installed `GameBot.Service.exe` does not count. Any other program counts. A program that the installer cannot read also counts.
- In interactive mode, the detected values show on the network settings page and you can edit them. A port in use gives an error and the page stays.
- In silent mode, the installer keeps a given or saved port, also when another program uses it. It writes a warning to the installer log.

---

## 6) Logging and retention

- Log root: `%LocalAppData%\GameBot\Installer\logs`
- Retention target: last 10 log files

### Version numbering in CI

- Installer semantic version format is `major.minor.patch.build`.
- In CI, `build` is sourced from `github.run_number`.
- `github.run_number` increments only when GitHub creates a new workflow run for `release-installer`.
- For this repo, that means it changes on each manual `workflow_dispatch` run of `release-installer`.
- Re-running a failed/successful existing run does **not** change `github.run_number`; only `github.run_attempt` increases.
- Example timeline:
  - Run A (you start the workflow): `github.run_number=57`, `github.run_attempt=1`
  - Re-run Run A: `github.run_number=57`, `github.run_attempt=2`
  - You start the workflow again (new run): `github.run_number=58`, `github.run_attempt=1`
- CI does not write version counters back to protected branches.
- Local/manual builds still support override/version files under `installer/versioning`.

### Version numbering in local builds

- Local builds now attempt to query the latest completed `release-installer` workflow run for the current branch.
- When available, local build number is set to:

  ```text
  local build = (latest CI run_number on current branch) + 1
  ```

- If GitHub context cannot be resolved (for example, no `origin` GitHub remote, no `gh` CLI, or no auth), the build falls back to local counter-based version resolution.

---

## 7) Exit codes (silent/unattended)

| Exit code | Meaning |
|---:|---|
| `0` | Success |
| `3010` | Success, reboot required |
| `1603` | Fatal installer error |
| `1618` | Another installation already running |
| `2` | Validation error |

---

## 8) Common command examples

### Background app, per-user, HTTP

```powershell
.\GameBotInstaller.exe /quiet MODE=backgroundApp SCOPE=perUser DATA_ROOT="%LocalAppData%\GameBot\data" PORT=8080 BIND_HOST=0.0.0.0 PROTOCOL=http ENABLE_HTTPS=0 ALLOW_ONLINE_PREREQ_FALLBACK=1
```

### Background app, per-user, HTTPS

```powershell
.\GameBotInstaller.exe /quiet MODE=backgroundApp SCOPE=perUser DATA_ROOT="%LocalAppData%\GameBot\data" PORT=5000 PROTOCOL=https ENABLE_HTTPS=1 CERTIFICATE_REF="thumbprint:ABCDEF123456" ALLOW_ONLINE_PREREQ_FALLBACK=0
```

---

## 9) Troubleshooting

- `1618`: close/wait for other installer sessions and retry.
- `2`: review parameter combination (`MODE`/`SCOPE`, HTTPS certificate, writable `DATA_ROOT`).
- `1603`: inspect latest log in `%LocalAppData%\GameBot\Installer\logs`.
- Installed app not visible: ensure install used the current-user bootstrapper and complete the install as the same Windows user.
- Legacy hidden/stale uninstall entry cleanup (advanced):

  ```powershell
  Get-ChildItem "HKCU:\Software\Microsoft\Windows\CurrentVersion\Uninstall", "HKLM:\Software\Microsoft\Windows\CurrentVersion\Uninstall", "HKLM:\Software\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall" |
    Get-ItemProperty |
    Where-Object { $_.DisplayName -like "*GameBot*" } |
    Select-Object DisplayName, DisplayVersion, PSChildName, UninstallString
  ```

  If a stale MSI product code is found:

  ```powershell
  msiexec /x "{PRODUCT-CODE-GUID}"
  ```

---

## 10) Related docs

- Build scaffold notes: `INSTALLER.md`
- Feature specification: `specs/025-standalone-windows-installer/spec.md`
- Contract schema: `specs/025-standalone-windows-installer/contracts/installer.openapi.yaml`
- Validation quickstart: `specs/025-standalone-windows-installer/quickstart.md`

---

## 11) CI/PR operations (important)

This section documents the current installer CI/release behavior and operational runbook.

### Workflow and trigger model

- Installer artifacts are produced by GitHub workflow `release-installer`.
- `release-installer` runs only on manual `workflow_dispatch`. It builds an installer from any
  branch on demand. A push or a PR merge does not start it, so a merge does not build twice.
- Open pull requests do **not** build an installer. The installer is a release
  artifact, not a PR gate; PRs are gated by `.NET CI` and `CodeQL`.
- To publish a GitHub Release, start the workflow on `master` with `publish_release` set to `true`.
- There is deliberately no `paths` filter. The installer payload is the full
  publish closure of `src/GameBot.Service` plus the built `src/web-ui/dist`
  bundle plus the root `LICENSE`, so nearly any source change alters what the
  installer contains. A path filter could never be complete, and an incomplete
  one silently skips runs, leaving the current `master` commit with no installer
  of its own.
- The concurrency group is `release-installer-<ref>`, and `cancel-in-progress` is
  **false**: a run can publish a release, so a newer run on the same ref waits and never cancels it.
- The workflow uses read-only permissions (`contents: read`) and does not push version files back to protected branches.

### Build/version behavior in CI

- `scripts/build-installer.ps1` sets context:
  - CI (`CI=true` or `GITHUB_ACTIONS=true`) => `buildContext=ci`
  - local => `buildContext=local`
- In CI, `GITHUB_RUN_NUMBER` is parsed and passed as `-BuildNumberOverride` into `Resolve-InstallerVersion`.
- Effective installer version in CI is therefore:

  ```text
  <major>.<minor>.<patch>.<github.run_number>
  ```

- `Resolve-InstallerVersion` still contains explicit `if ($BuildContext -eq "ci")` logic for test compatibility.

### Artifact naming behavior

- `release-installer` computes artifact name from override major/minor/patch + `github.run_number`.
- Branch is sanitized and included in non-main/non-master artifact names.
- Current naming examples:
  - `gamebot-installer-v0.1.0.123-win-x64` (main/master style)
  - `gamebot-installer-v0.1.0.123-026-installer-semver-upgrade-win-x64` (feature branch)

### Required checks / PR gate

- PRs should wait for all CI checks to complete before merge.
- Typical check set includes:
  - `.NET CI` (`ci-gate` is the single pass signal; jobs: `changes`, `compile`, `analyze`, `test-unit`, `test-integration`, `test-contract`, `web-ui-tests`) - `analyze` enforces `-warnaserror`, and `changes` runs the installer secret scan. See `docs/ci-time.md`
  - `CodeQL`
- `release-installer` (`build-release-installer`) is **not** a PR check and will
  not appear in a PR's status rollup. It does not run after a merge either. Its
  absence from a PR is expected, not a missing check.
- To get an installer for a merge, start the workflow by hand on `master`, then check the run:
  `gh run list --workflow release-installer.yml --branch master`.

### Conflict resolution runbook

When a PR shows `DIRTY`/`CONFLICTING` against `master`:

```powershell
git fetch origin
git checkout <feature-branch>
git merge origin/master
# resolve conflicts in:
# - .github/workflows/release-installer.yml
# - scripts/build-installer.ps1
# - scripts/installer/common.psm1
git add <resolved-files>
git commit
git push
```

After push:
- Verify PR becomes `MERGEABLE`.
- Re-check all workflow runs from the new commit.

### CI assertion compatibility notes

Installer integration tests validate literal script strings in `scripts/installer/common.psm1`.
Do not remove these expected forms without updating tests:

- `if ($BuildContext -eq "ci")`
- `Persisted = ($BuildContext -eq "ci")`

These assertions are used by:
- `tests/integration/Installer/CiBuildCounterPersistenceTests.cs`
- `tests/integration/Installer/LocalBuildDerivationTests.cs`

---

## 12) Finalized UX behavior (regression-locked)

The installer UX and runtime behavior are now validated against the following scenario contract.

### Scenario 1: Fresh install

- Page 1 (Bootstrapper): EULA + exact bundle build version is shown.
- Page 2 (MSI): installation directory page with default per-user path.
- Page 3 (MSI): interface/port page with:
  - bind host combo (default localhost)
  - port prefilled using deterministic first-available selection order (`8080,8088,8888,80`).
- Final page: “start background app” suggestion defaults to checked.
- Shortcuts are created from the resulting configuration.

### Scenario 2: Upgrade

- Page 1 (Bootstrapper): same EULA + exact version display.
- The directory page and the interface/port page show, as for a fresh install (feature 162).
- The interface/port page shows the saved values. The saved configuration stays if you click **Next**.
- The shortcut is created again from the host and port in use. An upgrade from the Web UI or the API does the same.

### Scenario 3: Removal

- No configuration pages are shown.
- Background app launch action is not triggered on uninstall.

### Scenario 4: Downgrade

- Downgrade is blocked.
- User gets explicit downgrade error message instead of upgrade flow.

### Frontend shortcut stability rules

- Frontend shortcut always uses URL protocol handler via `rundll32`.
- The target is the property `SHORTCUT_URL`. The installer script `PortResolver.jscript` sets it once, from the host and port in use.
- No action changes the shortcut file after the install.

### Automated regression coverage

The following tests now enforce this contract:

- `tests/integration/Installer/InstallerAuthoringScenarioTests.cs`
- `tests/integration/Installer/UpgradePropertyRetentionTests.cs`
- `tests/integration/Installer/DowngradeBlockFlowTests.cs`
- `tests/integration/Installer/FirstInstallFlowTests.cs`
- `tests/integration/Installer/HttpsConfigurationFlowTests.cs`
- `tests/integration/Installer/PersistedNetworkScriptTests.cs` (resolver, port check, and registry migration)
- `tests/integration/Installer/TestVariantTests.cs` (isolated test copy of the MSI)
- `tests/unit/Installer/PortSelectionRulesTests.cs` (port check on sample `netstat` text)
- `scripts/installer/scope-smoke.ps1` (authoring smoke assertions)

### Test an install without a risk to your real install

The MSI is per-user and has fixed identities. A test run of the real MSI replaces your installed GameBot.
Use `scripts/installer/new-test-variant.ps1` instead. It copies a built MSI and gives the copy its own upgrade code,
component GUIDs, registry root, Start-Menu folder, autostart name, and default install folder.

```powershell
.\scripts\installer\new-test-variant.ps1 -InstanceName T1
msiexec /i installer\wix\bin\x64\Release\GameBot.T1.msi APPLICATIONFOLDER="C:\Temp\gb-test" PORT=18080 BIND_HOST=127.0.0.1
```

Use a temporary folder and a temporary port. Remove the copy with `msiexec /x` when you are done.
