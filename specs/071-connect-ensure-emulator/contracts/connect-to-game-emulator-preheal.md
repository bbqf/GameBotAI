# Contract: connect-to-game optional emulator pre-heal

The connect-to-game **sequence action** gains two optional parameters. No new endpoints; the fields
ride the existing action payload.

## Action payload (sequence Action step)

```json
{
  "stepType": "Action",
  "action": {
    "type": "connect-to-game",
    "parameters": {
      "gameId": "pns",
      "adbSerial": "emulator-5558",
      "instanceName": "LDPlayer-5558"
    }
  }
}
```

`instanceIndex` may be used instead of `instanceName`. Both optional; omitting both = today's behavior.

## Validation

| Condition                                   | Result                         |
|---------------------------------------------|--------------------------------|
| `gameId` or `adbSerial` missing             | reject (unchanged)             |
| `instanceName`/`instanceIndex` both omitted | accept — no pre-heal           |
| `instanceIndex` < 0                         | reject                         |
| valid + instance id present                 | accept — pre-heal enabled      |

## Execution contract (`DispatchConnectToGameAsync`)

| Situation (instance id present) | Emulator outcome | Connect step |
|---------------------------------|------------------|--------------|
| already up + responsive         | AlreadyHealthy   | proceed → attach + launch → **executed** |
| was closed                      | Started          | proceed → attach + launch → **executed** |
| was hung                        | Restarted        | proceed → attach + launch → **executed** |
| never boots in time             | RecoveryTimedOut | **failed**, session NOT started |
| identifier matches no instance  | InstanceNotFound | **failed**, session NOT started |
| host can't drive emulator       | PlatformUnsupported / ControlUnavailable | proceed → attach + launch → **executed** |
| **no instance id**              | (pre-heal skipped) | proceed exactly as today → **executed**/failed per existing rules |

## Step result message

- Proceed: `connected to game '<gameId>' on device '<serial>' (session <id>); emulator: <reasonCode>; game launch: <launchReason>` (the `emulator:` clause appears only when the pre-heal ran).
- Fail-fast: `connect-to-game emulator pre-heal failed: <reasonCode>` (no session started).

## MCP

The interactive `start_session` tool (`/api/sessions/start`) is **out of scope** and unchanged. The
connect-to-game *sequence action* is authored as JSON parameters; the optional fields are carried in
the parameters dictionary and require no schema change.
