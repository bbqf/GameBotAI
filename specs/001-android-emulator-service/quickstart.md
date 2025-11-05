# Quickstart: GameBot Android Emulator Service

Date: 2025-11-05
Branch: 001-android-emulator-service

## Prerequisites
- Windows 10/11 with virtualization enabled
- LDPlayer installed (preferred). If not using LDPlayer, install Android SDK (Emulator + platform-tools) and ensure `adb` is on PATH.
- Sufficient hardware for concurrent emulator sessions

## Setup (service only)
- Configure a token for API access (env var or config file)
- Ensure game artifact directories are accessible to the service
- Optional: Set explicit adb path via env `GAMEBOT_ADB_PATH` or config `Service:Emulator:AdbPath`. Otherwise, the service auto-detects LDPlayer and uses its bundled adb.

## Typical Flow
1. Register a game artifact via REST (title + path + checksum)
2. (Optional) Create an automation profile (ordered input steps)
3. Start a session referencing the game (and profile if desired)
4. Send control inputs via REST
5. Fetch periodic snapshots to render in a separate UI
6. Stop the session; verify resources are released

## Notes
- UI is a separate deployment consuming the documented REST API.
- Streaming visuals are not included in MVP; periodic snapshots are supported.
