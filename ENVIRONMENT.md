# Environment Variables

This document lists all environment variables recognized by GameBot and how to use them. It also explains how to add or change environment variables in the future.

Applies to: .NET 8 ASP.NET Core service, emulator/ADB integration, OCR backends, tests.

Note on configuration: In addition to direct environment variables (e.g., `GAMEBOT_*`), the service uses standard ASP.NET Core configuration. Any configuration key like `Service:Storage:Root` can be supplied via environment variables by replacing `:` with `__` (double underscore), e.g., `Service__Storage__Root`.

## Service and Hosting

- GAMEBOT_DATA_DIR
  - Purpose: Root directory for data storage (games, profiles).
  - Used in: `Program.cs`
  - Default: `(<app-base>)/data` when not set; or `Service:Storage:Root` if provided.
  - Example (PowerShell): `$env:GAMEBOT_DATA_DIR = "C:\\src\\GameBot\\data"`

- GAMEBOT_DYNAMIC_PORT
  - Purpose: Bind service to a dynamically assigned port instead of a fixed one to avoid conflicts in tests/CI.
  - Used in: `Program.cs`
  - Values: `true` | `false` (string comparison, case-insensitive)
  - Default: `false` (unset)

- GAMEBOT_ENABLE_PROFILE_ENDPOINTS
  - Purpose: Toggle mapping of legacy `/profiles` endpoints (and related trigger routes) for backward compatibility during migration to `/actions`.
  - Used in: `Program.cs`
  - Values: `true` | `false` (case-insensitive). Any value other than `"false"` enables the endpoints.
  - Default: `true` (unset) to preserve compatibility; set to `false` to disable.
  - Example (PowerShell): `$env:GAMEBOT_ENABLE_PROFILE_ENDPOINTS = "false"`

- GAMEBOT_AUTH_TOKEN
  - Purpose: Enables token-based auth for all non-health endpoints when set.
  - Used in: `Program.cs`
  - Default: unset (no auth)
  - Example: `$env:GAMEBOT_AUTH_TOKEN = "dev-token-123"`

- Logging (ASP.NET Core built-in)
  - Purpose: Control log levels via configuration.
  - Example (env): `$env:Logging__LogLevel__Default = "Debug"`

## Screen Capture Source

- GAMEBOT_USE_ADB
  - Purpose: Select screen source implementation.
  - Used in: `Program.cs`, `SessionManager`
  - Values: any value other than `"false"` enables ADB; `"false"` selects stub bitmap source.
  - Default: ADB enabled unless explicitly set to `false`.

- GAMEBOT_TEST_SCREEN_IMAGE_B64
  - Purpose: Provide a base64-encoded PNG/JPEG used by the stub screen source when ADB is disabled.
  - Used in: `Program.cs`
  - Default: no image (screen source returns null)
  - Notes: Supports data URLs (e.g., `data:image/png;base64,....`) or raw base64.

## OCR Backends

- GAMEBOT_TESSERACT_ENABLED
  - Purpose: Enable Tesseract-based OCR backend.
  - Used in: `Program.cs`
  - Values: `true` | `false`
  - Default: `false` (Env-backed OCR used)

- GAMEBOT_TESSERACT_PATH
  - Purpose: Full path to `tesseract` executable when not on PATH.
  - Used in: `TesseractProcessOcr`, integration tests
  - Default: `tesseract` resolved from PATH

- GAMEBOT_TESSERACT_LANG
  - Purpose: Default OCR language passed to Tesseract when a trigger doesn't override it.
  - Used in: `TesseractProcessOcr`
  - Default: `eng`

- GAMEBOT_TEST_OCR_TEXT
  - Purpose: Deterministic OCR text for the environment-backed OCR stub (no native deps).
  - Used in: `EnvTextOcr`
  - Default: empty string

- GAMEBOT_TEST_OCR_CONF
  - Purpose: Deterministic OCR confidence (0.0–1.0) for the environment-backed OCR stub.
  - Used in: `EnvTextOcr`
  - Default: `0.99` when text is non-empty; `0.0` otherwise

## ADB / Emulator

- GAMEBOT_ADB_PATH
  - Purpose: Override path to `adb` executable.
  - Used in: `AdbResolver`
  - Default: Attempt resolver logic (PATH, LDPlayer locations)

- LDPLAYER_HOME / LDP_HOME
  - Purpose: Hints for locating LDPlayer’s bundled `adb`.
  - Used in: `AdbResolver`

- GAMEBOT_ADB_RETRIES
  - Purpose: Number of ADB retry attempts in session management.
  - Used in: `SessionManager`
  - Default: `2`

- GAMEBOT_ADB_RETRY_DELAY_MS
  - Purpose: Delay (ms) between ADB retry attempts.
  - Used in: `SessionManager`
  - Default: `100`

## Service Options via ASP.NET Core Configuration

You can set these via appsettings or environment variables using double underscores.

- Service:Storage:Root
  - Env name: `Service__Storage__Root`
  - Purpose: Data storage root (alternative to `GAMEBOT_DATA_DIR`)

- Service:Auth:Token
  - Env name: `Service__Auth__Token`
  - Purpose: Auth token (alternative to `GAMEBOT_AUTH_TOKEN`)

- Service:Sessions:MaxConcurrentSessions
  - Env name: `Service__Sessions__MaxConcurrentSessions`
  - Default: `3`

- Service:Sessions:IdleTimeoutSeconds
  - Env name: `Service__Sessions__IdleTimeoutSeconds`
  - Default: `1800`

- Service:Triggers:Worker:IntervalSeconds
  - Env name: `Service__Triggers__Worker__IntervalSeconds`
  - Default: `2`

- Service:Triggers:Worker:GameFilter
  - Env name: `Service__Triggers__Worker__GameFilter`
  - Default: unset

- Service:Triggers:Worker:SkipWhenNoSessions
  - Env name: `Service__Triggers__Worker__SkipWhenNoSessions`
  - Default: `true`

- Service:Triggers:Worker:IdleBackoffSeconds
  - Env name: `Service__Triggers__Worker__IdleBackoffSeconds`
  - Default: `5`

## Quick reference (what to set for common scenarios)

- Run with stub screen + deterministic OCR text:
  - `GAMEBOT_USE_ADB=false`
  - `GAMEBOT_TEST_SCREEN_IMAGE_B64=<base64 image>`
  - `GAMEBOT_TEST_OCR_TEXT="HELLO WORLD"`
  - optional: `Logging__LogLevel__Default=Debug`

- Run with Tesseract:
  - `GAMEBOT_TESSERACT_ENABLED=true`
  - `GAMEBOT_TESSERACT_PATH=C:\\Program Files\\Tesseract-OCR\\tesseract.exe` (if not on PATH)
  - `GAMEBOT_TESSERACT_LANG=eng` (or target language)

- Require an auth token and dynamic port:
  - `GAMEBOT_AUTH_TOKEN=your-token`
  - `GAMEBOT_DYNAMIC_PORT=true`

## Maintaining this document

When you add or change environment variables:

1) Naming
- Use `GAMEBOT_` prefix for service-specific switches (OCR/ADB/auth/data/etc.).
- Prefer explicit names that describe the behavior (e.g., `GAMEBOT_TESSERACT_ENABLED`).

2) Code placement
- Read variables close to their usage (e.g., OCR selection in `Program.cs`).
- For configurable options, prefer ASP.NET Core configuration (and document the corresponding env form with `__`).

3) Documentation updates (required)
- Update this `ENVIRONMENT.md`: add the variable name, purpose, default, scope, and examples.
- If user-facing, add a short note in `README.md` linking to this file.

4) Tests & CI
- If tests depend on new variables, update integration tests and any test README comments.
- Keep skip conditions/capabilities (e.g., Tesseract presence) aligned with the documented variables.

5) Discoverability tip
- To find env usages: search for `Environment.GetEnvironmentVariable(` and configuration keys like `Service:`.

This file is the source of truth for environment-driven behavior. Please keep it accurate and up to date with each change.
