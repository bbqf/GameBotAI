# Research: GameBot Android Emulator Service

Date: 2025-11-05
Branch: 001-android-emulator-service

## Goals
- Validate feasibility of controlling Android Emulator (AVD) on Windows via ADB for starting/stopping sessions, input injection, and screen capture.
- Identify prerequisites, risks, and constraints.

## Findings

### Emulator Options
- LDPlayer (Preferred for Windows): Popular Android emulator for Windows with bundled `adb.exe`. Using its bundled adb avoids path/version conflicts and ensures compatibility with the running instance(s). Plan includes detection and path resolution for LDPlayer.
- Android Studio Emulator (AVD): Official, scriptable via ADB; supports snapshots and multiple virtual devices. Used as fallback when LDPlayer is not present.
- Genymotion/BlueStacks: Third-party; licensing and automation policies vary. Excluded for MVP to avoid licensing and API differences.

### Control Surface (ADB)
- ADB Path Resolution Priority: config override → LDPlayer detection (env var, well-known paths, registry) → system PATH.
- Start emulator: If LDPlayer present, emulator instances assumed managed outside MVP (optional `LDConsole.exe` integration later). With AVD fallback, start via command line.
- Input injection via `adb shell input` (keyevent, tap, swipe); mapping to game controls required.
- Screen capture via `adb exec-out screencap -p` (PNG). Latency acceptable for periodic snapshots; streaming requires more work.

### Windows Prerequisites
- Windows 10/11 with virtualization enabled (Hyper-V/WHVP).
- LDPlayer installed (preferred) OR Android SDK Emulator + platform-tools on PATH.
- Adequate CPU/GPU and RAM for concurrent sessions.

### Security
- Token/key-based auth for REST. Avoid exposing ADB externally.
- Sanitize and validate file paths for game artifacts.
- Do not log sensitive data; redact tokens.

### Risks
- Performance variability across hosts (CPU/GPU) affecting snapshot latency and input responsiveness.
- Emulator stability under concurrent sessions; need watchdog and recovery.
- ADB path mismatch if multiple emulators present; mitigated by LDPlayer-first detection and explicit config override.
- Legal/licensing considerations for game artifacts; users provide their own content.

## Open Questions (captured and resolved in spec)
- "Learn" scope → deterministic automation profiles (MVP).
- Visual feedback → periodic snapshots (MVP).
- Access control → token/key-based auth.

## Next Steps
- Prototype ADB interactions (start AVD, input injection, screencap) and benchmark snapshot latency.
- Define concrete REST contracts aligned with entities.
- Establish CI with analyzers, tests, and coverage gates.
