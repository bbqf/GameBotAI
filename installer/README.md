# GameBot Windows Installer Scaffold

This folder contains the standalone Windows installer scaffold.

## Intended Output

- Bootstrapper EXE
- MSI payload package

## Tooling

- WiX Toolset (v4+)
- PowerShell packaging scripts in `scripts/`

## Build Flow (scaffold)

1. Publish backend and web UI artifacts
2. Copy outputs into installer payload directory
3. Build WiX MSI
4. Build WiX bootstrapper EXE

## Current State

This is a scaffold branch for the corrected architecture. Authoring and packaging details are intentionally incremental.
