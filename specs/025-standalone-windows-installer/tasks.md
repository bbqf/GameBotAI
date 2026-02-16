# Tasks: Standalone Windows Installer (EXE/MSI)

## Phase 1: Scaffold

- [ ] T001 Create installer folder structure under `installer/wix/`
- [ ] T002 Add WiX project scaffold files (`.wixproj`, `Bundle.wxs`, `Product.wxs`)
- [ ] T003 Add payload packaging scripts in `scripts/`
- [ ] T004 Add installer usage/build docs

## Phase 2: Payload Packaging

- [ ] T005 Publish backend/web-ui outputs into installer payload folder
- [ ] T006 Add deterministic payload manifest and version stamping

## Phase 3: MSI Authoring

- [ ] T007 Define install directories, features, and components
- [ ] T008 Add service/background mode configuration properties
- [ ] T009 Add backend/web-ui config file templating at install time

## Phase 4: Bootstrapper

- [ ] T010 Chain prerequisite/runtime checks
- [ ] T011 Add interactive UX strings and silent mode contract
- [ ] T012 Add logging and installer exit-code mapping

## Phase 5: Validation

- [ ] T013 Add install smoke test script
- [ ] T014 Add uninstall smoke test script
- [ ] T015 Add CI packaging task and artifact publish
