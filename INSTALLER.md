# GameBot Windows Installer (Scaffold)

This branch introduces the corrected installer direction: a standalone Windows installer (EXE/MSI), not in-app installer HTTP endpoints.

## What exists in scaffold

- WiX project scaffold in `installer/wix/`
- Payload packaging script: `scripts/package-installer-payload.ps1`
- Installer build script: `scripts/build-installer.ps1`
- Feature spec/plan/tasks in `specs/025-standalone-windows-installer/`

## Build scaffold

From repository root:

```powershell
.\scripts\build-installer.ps1 -Configuration Release
```

## Notes

- This scaffold intentionally uses placeholder MSI payload authoring.
- Next iteration should replace placeholder files with published backend/web-ui payload entries and author full WiX components.
- Add signing and CI artifact publishing after MSI authoring is complete.
