# GameBot Installer Usage Guide

This document explains how to run the GameBot Windows installer and all currently supported installer options.

## Scope

- Platform: Windows x64
- Installer type: Bootstrapper EXE that installs MSI payload(s)
- Modes: interactive wizard and silent/unattended install
- Installation scope: per-user only

---

## 1) Quick start

### Interactive install

1. Locate the installer executable (for example, `GameBotInstaller.exe`).
2. Run the installer as the current user.
3. Complete the wizard prompts.
4. Verify installation:
   - App files are installed
   - Data directory exists and is writable
  - Installer log exists under `%LocalAppData%\GameBot\Installer\logs`

### Silent install

Use `/quiet` with installer variables:

```powershell
.\GameBotInstaller.exe /quiet MODE=backgroundApp SCOPE=perUser DATA_ROOT="%LocalAppData%\GameBot\data" BACKEND_PORT=auto WEB_PORT=auto BIND_HOST=0.0.0.0 PROTOCOL=http ENABLE_HTTPS=0 ALLOW_ONLINE_PREREQ_FALLBACK=1
```

---

## 2) Installer variables (all supported options)

These variables are supported by the bootstrapper and forwarded to MSI.

| Variable | Type | Default | Allowed values | Required | Description |
|---|---|---:|---|---|---|
| `MODE` | string | `backgroundApp` | `backgroundApp` | Yes | Runtime mode to configure. |
| `SCOPE` | string | `perUser` | `perUser` | Yes | Installation scope. |
| `DATA_ROOT` | string (path) | empty (resolved to per-user path) | Writable path | No | Runtime data path override. |
| `BACKEND_PORT` | integer/string | `8080` | `1..65535`, `auto` | Yes | Backend API port input. `auto` resolves to first available at install time. |
| `WEB_PORT` | integer/string | `8080` | `1..65535`, `auto` | Yes | Web UI port input. `auto` resolves to first available at install time. |
| `BIND_HOST` | string | `127.0.0.1` | IPv4/hostname (for example `0.0.0.0`, `127.0.0.1`) | Yes | Backend bind interface/host. |
| `PROTOCOL` | string | `http` | `http`, `https` | Yes | Endpoint protocol. |
| `ENABLE_HTTPS` | boolean-ish string | `0` | `0`/`1` | No | Enable HTTPS validation path. |
| `CERTIFICATE_REF` | string | empty | Certificate identifier | Conditionally | Required when `ENABLE_HTTPS=1`. |
| `ALLOW_ONLINE_PREREQ_FALLBACK` | boolean-ish string | `1` | `0`/`1` | No | Allow online prerequisite fallback from allowlisted sources. |

Notes:
- `DATA_ROOT` defaults to `%LocalAppData%\GameBot\data` when omitted.
- Install root defaults to `%LocalAppData%\GameBot`.

---

## 3) Mode/scope constraints

- Supported mode: `backgroundApp`
- Supported scope: `perUser`
- Startup behavior:
  - Registers autostart in `HKCU\Software\Microsoft\Windows\CurrentVersion\Run`
  - Adds a Start Menu shortcut `GameBot Background` for manual start
- Start Menu shortcut `GameBot` only opens the web UI URL and does not start backend processes.

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

- Deterministic preference order: `8080 -> 8088 -> 8888 -> 80`
- During installation, occupied ports are automatically detected and resolved to the next available preferred port.
- In interactive mode, detected values are shown on the network settings dialog and can be edited before continuing.
- In silent mode, CLI overrides are honored when available; if a requested port is occupied, installer resolves to an available port.

---

## 6) Logging and retention

- Log root: `%LocalAppData%\GameBot\Installer\logs`
- Retention target: last 10 log files

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
.\GameBotInstaller.exe /quiet MODE=backgroundApp SCOPE=perUser DATA_ROOT="%LocalAppData%\GameBot\data" BACKEND_PORT=auto WEB_PORT=auto BIND_HOST=0.0.0.0 PROTOCOL=http ENABLE_HTTPS=0 ALLOW_ONLINE_PREREQ_FALLBACK=1
```

### Background app, per-user, HTTPS

```powershell
.\GameBotInstaller.exe /quiet MODE=backgroundApp SCOPE=perUser DATA_ROOT="%LocalAppData%\GameBot\data" BACKEND_PORT=5000 WEB_PORT=8088 PROTOCOL=https ENABLE_HTTPS=1 CERTIFICATE_REF="thumbprint:ABCDEF123456" ALLOW_ONLINE_PREREQ_FALLBACK=0
```

---

## 9) Troubleshooting

- `1618`: close/wait for other installer sessions and retry.
- `2`: review parameter combination (`MODE`/`SCOPE`, HTTPS certificate, writable `DATA_ROOT`).
- `1603`: inspect latest log in `%LocalAppData%\GameBot\Installer\logs`.

---

## 10) Related docs

- Build scaffold notes: `INSTALLER.md`
- Feature specification: `specs/025-standalone-windows-installer/spec.md`
- Contract schema: `specs/025-standalone-windows-installer/contracts/installer.openapi.yaml`
- Validation quickstart: `specs/025-standalone-windows-installer/quickstart.md`
