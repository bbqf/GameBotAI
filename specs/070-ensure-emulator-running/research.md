# Phase 0 Research: Ensure Emulator Running Action

All Technical Context unknowns were resolved from the spec's clarifications and direct reading of the
existing codebase. No open `NEEDS CLARIFICATION` items remain.

## Decision 1 — Emulator process control mechanism

- **Decision**: Drive LDPlayer through its `ldconsole.exe` CLI, wrapped in a new
  `GameBot.Emulator.Adb.LdConsoleClient` (Windows-only), using `isrunning`, `launch`, and `reboot`.
- **Rationale**: `ldconsole.txt` at the repo root documents exactly these subcommands, each keyed by
  `--name <instance>` or `--index <n>`. It is the same tool the operator already uses to manage
  instances, and it lives in the LDPlayer install directory next to `adb.exe`, so discovery reuses
  the proven `AdbResolver` strategy. Mirroring `AdbClient` (process-exec + `LoggerMessage` logging +
  `[SupportedOSPlatform("windows")]`) keeps the new client idiomatic and analyzer-clean.
- **Alternatives considered**:
  - *Android SDK `emulator`/`avd` tooling* — rejected: the target is LDPlayer (serial
    `emulator-5558`), not an AVD; the SDK emulator binary is absent.
  - *Kill/relaunch the LDPlayer process directly via Win32* — rejected: fragile, bypasses LDPlayer's
    own supervision, and duplicates what `ldconsole` already does safely.
  - *`quit` then `launch` for a hung instance* — rejected per clarification in favor of the single
    `reboot` command (atomic, one round-trip, matches operator expectation).

## Decision 2 — "Running and responsive" (health) definition

- **Decision**: Three checks — (1) `ldconsole isrunning` reports running; (2) the serial appears in
  `adb devices` in state `device` (not `offline`/absent); (3) a bounded
  `adb -s <serial> shell getprop sys.boot_completed` returns `1` within the probe timeout.
- **Rationale**: A process can be "running" yet frozen (offline device, or shell that never answers).
  `sys.boot_completed` is the standard Android readiness signal and is cheap to query. The device
  present/offline distinction already exists in `SessionManager.ParseDeviceSerials`, which only counts
  serials whose state is exactly `device` — that parsing approach is reused for check (2).
- **Alternatives considered**: screenshot-diff liveness (heavier, image-dependent), `dumpsys` window
  focus (overkill for "is it responsive"). Both rejected as slower and less deterministic than a
  boot-complete getprop.

## Decision 3 — Remediation and wait loop

- **Decision**: If not running → `launch`; if running-but-unhealthy (hung) → `reboot`. After either,
  poll checks (2)+(3) every `EmulatorPollIntervalMs` until healthy or `EmulatorBootWaitMs` elapses;
  success only on healthy, otherwise fail the step with a clear reason. Already-healthy → no-op
  success (idempotent).
- **Rationale**: Directly satisfies FR-004/FR-005/FR-006/FR-007. A single capped, configurable wait
  is the only long-running portion and only runs during a real (re)start.
- **Alternatives considered**: fixed sleep after launch (unreliable across host speeds) — rejected in
  favor of polling with a ceiling.

## Decision 4 — Tool discovery: `LdConsoleResolver`

- **Decision**: A new `LdConsoleResolver.ResolveLdConsolePath()` that mirrors `AdbResolver` line-for-line
  but resolves `ldconsole.exe`: `GAMEBOT_LDCONSOLE_PATH` override → `LDPLAYER_HOME`/`LDP_HOME` →
  known install-path probes → registry `Uninstall` `InstallLocation`. Returns `null` when not found
  (→ neutral unsupported outcome).
- **Rationale**: `ldconsole.exe` and `adb.exe` sit in the same LDPlayer directory, so the identical
  discovery order applies; only the filename and the env-override name differ.
- **Alternatives considered**: deriving `ldconsole.exe` from the resolved `adb.exe` directory —
  viable and simpler, but a dedicated resolver keeps the `GAMEBOT_LDCONSOLE_PATH` override symmetric
  with `GAMEBOT_ADB_PATH` and is independently testable. Chosen for symmetry and testability.

## Decision 5 — Configuration knobs and where they bind

- **Decision**: Add `EmulatorProbeTimeoutMs` (10000), `EmulatorBootWaitMs` (120000),
  `EmulatorPollIntervalMs` (3000) to `AppConfig`. Bind `GAMEBOT_EMULATOR_PROBE_TIMEOUT_MS`,
  `GAMEBOT_EMULATOR_BOOT_WAIT_MS`, `GAMEBOT_EMULATOR_POLL_INTERVAL_MS` in
  `GameBotServiceSetup.BuildAppConfig`, add them to `ConfigSnapshotService` defaults, and apply them
  in `IConfigApplier.Apply` (runtime-mutable) — the exact three-place pattern used by
  `GAMEBOT_ADB_RETRIES`.
- **Rationale**: Consistency with existing knobs; operators can tune without redeploying; runtime-mutable
  via the config endpoints like the ADB knobs.
- **Alternatives considered**: per-action override fields on the payload — rejected as scope creep;
  host-level tuning belongs in config, not in every authored step.

## Decision 6 — Action shape: parameterized like `connect-to-game`

- **Decision**: The action carries `instanceName?`, `instanceIndex?`, and `adbSerial`. Validation
  requires `adbSerial` and at least one of `instanceName`/`instanceIndex`. A strongly-typed
  `EnsureEmulatorRunningArgs` (mirroring `ConnectToGameArgs`) reads these from either the primitive
  variant or the `SequenceActionPayload.Parameters` dictionary.
- **Rationale**: `connect-to-game` is the established precedent for an action that carries device
  identifiers; reusing its shape (variant fields + `TryFrom` + Parameters dict) means the DTO,
  validation, and web-ui panel all have a working template.
- **Alternatives considered**: auto-deriving the instance from the serial via `ldconsole list2` —
  rejected per spec assumption (author supplies the identifier; no fragile serial↔instance mapping).

## Decision 7 — Testability seams

- **Decision**: Introduce `IEmulatorControl` (isrunning/launch/reboot) and `IEmulatorDeviceProbe`
  (device-state + boot-completed) in the Service layer, with Windows adapters over `LdConsoleClient`
  and `AdbClient`. The handler depends on the interfaces.
- **Rationale**: Lets unit tests exercise the full outcome matrix with injected tiny timeouts and no
  real emulator/waits, meeting the determinism and coverage bars. Parallels how
  `EnsureGameRunningActionHandler` depends on `IAdbGameOperations`.
- **Alternatives considered**: calling `LdConsoleClient`/`AdbClient` directly from the handler —
  rejected: not unit-testable without a real emulator and real wall-clock waits.
