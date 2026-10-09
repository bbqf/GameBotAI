# Contract: Release Layout and Manifest

## GitHub Release

- **Tag and title**: `v<major>.<minor>.<patch>.<build>`. Example: `v1.7.0.430`.
- **Type**: A normal release. It is not a draft. It is not a pre-release.
- **Created by**: The `release-installer` workflow. The owner starts the workflow with `publish_release: true` on `master`.
- **Assets**:

| File | Use |
|------|-----|
| `GameBot.msi` | Update package. The bot downloads this file. |
| `GameBotInstaller.exe` | First install by the user. |
| `update-manifest.json` | Version and checksums. |

## update-manifest.json

```json
{
  "schemaVersion": 1,
  "version": "1.7.0.430",
  "msi": {
    "fileName": "GameBot.msi",
    "sizeBytes": 48211968,
    "sha256": "0f3a...64 hex characters..."
  }
}
```

### Rules

- `schemaVersion` is `1`. The bot refuses a higher value with code `update_manifest_unsupported`.
- `version` MUST equal the version in the release tag, without the `v`.
- `sha256` is 64 hexadecimal characters. The bot compares it without case.
- The bot downloads `msi.fileName` from the same release. The bot refuses any other host (research R-004).
- Add only optional fields in later versions. Do not change or remove a field of version 1.
