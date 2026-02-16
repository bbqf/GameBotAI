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

## Scaffold Artifacts

- `wix/Fragments/` contains initial WiX fragment scaffolds for directories and components
- `wix/Installer.Build.props` contains shared installer build properties
- `wix/payload/README.md` defines expected payload layout
- `../scripts/installer/` contains installer helper and smoke scripts

## Current State

This is a scaffold branch for the corrected architecture. Authoring and packaging details are intentionally incremental and tracked in `specs/025-standalone-windows-installer/tasks.md`.
