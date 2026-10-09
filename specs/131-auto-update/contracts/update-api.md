# Contract: Update API

All routes use the current auth rules. All errors use this shape:

```json
{ "error": { "code": "update_local_only", "message": "Install from the bot PC.", "hint": "Open the UI on the PC that runs GameBot." } }
```

## GET /api/update/status

Return the current update state. This route has no side effects. The UI uses it for progress and for the last result after a restart.

**200**

```json
{
  "installedVersion": "1.7.0.412",
  "lastCheck": {
    "status": "updateAvailable",
    "latestVersion": "1.7.0.430",
    "notes": "Release text",
    "checkedAtUtc": "2026-10-09T10:00:00Z"
  },
  "attempt": {
    "attemptId": "6f1c...",
    "state": "downloading",
    "targetVersion": "1.7.0.430",
    "startedAtUtc": "2026-10-09T10:01:00Z"
  },
  "lastResult": null,
  "canInstallHere": true,
  "installBlockedReason": null
}
```

- `lastCheck`, `attempt`, and `lastResult` can be `null`.
- `lastResult` is a finished attempt (`succeeded` or `failed`) from before the last restart. It appears one time.
- `canInstallHere` is `true` only when two things are true. The request comes from the bot PC. The bot runs from an installed folder. The UI uses it to show or hide "Install update".
- `installBlockedReason` is `null`, `"remote"`, or `"notInstalled"`. The UI uses it to choose the message. If both reasons apply, the value is `"remote"`.

## POST /api/update/check

Read the latest release and compare it with the installed version. The request has no body.

| Status | Code | When |
|--------|------|------|
| 200 | | The body is an `UpdateCheckResult`. `status` is `upToDate`, `updateAvailable`, or `checkFailed`. |
| 409 | `update_in_progress` | An install is active. |

A network failure returns `200` with `status: "checkFailed"` and an `error` object. The route does not return `5xx` for a failed check. The UI shows the message in the normal flow.

The `error.code` of a failed check is one of these values:

| Code | When |
|------|------|
| `update_network_error` | The bot cannot reach GitHub. |
| `update_rate_limited` | GitHub returns HTTP 403 or 429 for the request limit. The hint says "Try again later". |
| `update_no_release` | The repository has no release, or no release has a version that parses. |
| `update_manifest_invalid` | The manifest is missing, is not valid JSON, or has a bad field. |
| `update_manifest_unsupported` | The manifest `schemaVersion` is higher than the bot supports. |

## POST /api/update/install

Start the update. Request body:

```json
{ "targetVersion": "1.7.0.430", "confirmStopQueues": true }
```

The route checks the request in this order. The first failed check decides the response.

| Order | Status | Code | When |
|-------|--------|------|------|
| 1 | 403 | `update_local_only` | The request is not from the bot PC (FR-014). |
| 2 | 409 | `update_not_installed` | The bot does not run from an installed folder (FR-015). Message: "Update works only for an installed bot". |
| 3 | 400 | `update_confirmation_required` | `confirmStopQueues` is not `true` (FR-003, FR-013). |
| 4 | 400 | `update_not_available` | `targetVersion` is not greater than the installed version. Or it is not the latest checked version (FR-008). |
| 5 | 409 | `update_in_progress` | Another attempt is active. |
| 6 | 422 | `update_disk_space` | Free disk space is too low. |
| 7 | 202 | | The attempt started. The body is `{ "attemptId": "...", "state": "downloading" }`. |

Later failures do not change this response. The attempt gets `state: "failed"` and an `errorCode`. The UI reads them from `GET /api/update/status`. The codes are `update_download_failed`, `update_checksum_mismatch`, `update_install_failed`, and `update_restart_failed`.

## UI behavior (not a route)

- The page shows the installed version and a "Check for Update" button.
- When `status` is `updateAvailable`, the page shows the new version. It shows the notes as plain text if the release has notes. It shows an "Install update" button.
- If `canInstallHere` is `false`, the page shows no install button. For `remote`, the message is "Install from the bot PC". For `notInstalled`, the message is "Update works only for an installed bot".
- The install dialog states that all active queues stop at once. The user must confirm.
- In the states `installing` and `restarting`, the UI loses its connection. It requests `GET /api/update/status` at intervals until the bot answers. Then it shows `lastResult`.
