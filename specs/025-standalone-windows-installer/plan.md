# Implementation Plan: Standalone Windows Installer (EXE/MSI)

## Architecture

- WiX toolset-based installer project under `installer/wix/`
- Bootstrapper EXE chains prerequisite/runtime and MSI payload
- MSI installs backend + web UI artifacts to `Program Files\GameBot`
- Post-install configuration writes app settings and startup mode selections

## Workstreams

1. Payload packaging pipeline (`publish` output -> installer payload directory)
2. WiX MSI authoring (components, files, install folders, registry markers)
3. Bootstrapper authoring (prerequisite checks and chain)
4. Silent install contract (`/quiet` + properties)
5. Validation scripts and smoke tests

## Quality Gates

- Build installer artifacts from clean checkout
- Run unattended install smoke test on Windows VM
- Verify app reachability post-install
- Verify uninstall removes installed files and service artifacts
