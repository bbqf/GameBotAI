# Installer Payload Layout

This directory is populated by `scripts/package-installer-payload.ps1`.

## Expected structure

- `service/` — published backend service artifacts
- `web-ui/` — built web UI static assets
- `payload-manifest.json` — generated payload metadata

The payload directory is an intermediate build artifact and should be regenerated for each packaging run.
