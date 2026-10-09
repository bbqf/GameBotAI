# Data Model: Auto-Update

## Release (read from GitHub, not stored)

| Field | Type | Rule |
|-------|------|------|
| version | 4-part version | Parsed from the tag `v<major>.<minor>.<patch>.<build>`. Ignore a tag that does not parse. |
| tag | string | Release tag name. |
| publishedAtUtc | timestamp | From the release. |
| notes | string, optional | Release text. The UI shows it as plain text (FR-016). |
| msiUrl | https URL | Must be on an allowed host (research R-004). |
| msiSizeBytes | integer | From the manifest. Must be greater than 0. |
| msiSha256 | 64 hex characters | From the manifest. Compare in lower case. |

## UpdateCheckResult (in memory)

| Field | Type | Rule |
|-------|------|------|
| status | enum | `upToDate`, `updateAvailable`, `checkFailed` |
| installedVersion | 4-part version | From the service assembly. |
| latestVersion | 4-part version, optional | Set when the bot read a release. |
| notes | string, optional | From the release. |
| checkedAtUtc | timestamp | Time of the check. |
| error | error object, optional | Set only when status is `checkFailed`. |

Rule: the status is `updateAvailable` only when `latestVersion` is greater than `installedVersion`.

## UpdateAttempt (in memory, and `update-result.json` for the final state)

| Field | Type | Rule |
|-------|------|------|
| attemptId | GUID | New for each install request. |
| targetVersion | 4-part version | Version that the user confirmed. |
| fromVersion | 4-part version | Version before the update. |
| state | enum | See the state diagram. |
| startedAtUtc | timestamp | Start time. |
| finishedAtUtc | timestamp, optional | Set in `succeeded` and `failed`. |
| errorCode | string, optional | Set in `failed`. Example: `update_checksum_mismatch`. |
| errorMessage | string, optional | STE text with a fix hint. |
| msiexecExitCode | integer, optional | Set when `msiexec` ran. |
| logPath | string, optional | Path of the `msiexec` log file. |

### State transitions

```text
idle -> downloading -> verifying -> installing -> restarting -> succeeded
           |              |             |              |
           +--------------+-------------+--------------+--> failed
```

- In `downloading` and `verifying`, the bot process runs.
- In `installing` and `restarting`, the updater process runs. The bot process does not run.
- The updater sets `succeeded` only after the new bot process starts.
- `failed` ends the attempt. The old version still works (FR-009).
- Only one attempt can be active (research R-010).

## UpdateStatus (view for `GET /api/update/status`)

| Field | Type | Rule |
|-------|------|------|
| installedVersion | 4-part version | From the service assembly. |
| lastCheck | UpdateCheckResult, optional | The last check in this bot run. |
| attempt | UpdateAttempt, optional | The active attempt. |
| lastResult | UpdateAttempt, optional | A finished attempt from before the last restart. The bot keeps it in memory for the rest of the bot run. The UI shows it until the user closes it. |
| canInstallHere | boolean | True only if the request is from the bot PC and the bot runs from an installed folder. |
| installBlockedReason | enum, optional | `remote` or `notInstalled`. `remote` has priority. |

## Persistence layout

| Path | Content | Life |
|------|---------|------|
| `<data root>\updates\GameBot-<version>.msi` | Downloaded installer | Deleted after a successful update |
| `<data root>\updates\updater\` | Copy of `GameBot.Updater.exe` and its files | Replaced for each attempt |
| `<data root>\updates\update-result.json` | Final attempt state | Renamed to `update-result.reported.json` after the bot reads it |
| `<data root>\updates\msiexec-<attemptId>.log` | Windows Installer log | Kept for 30 days |
