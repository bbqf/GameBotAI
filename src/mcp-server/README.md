# GameBot MCP Server

A [Model Context Protocol](https://modelcontextprotocol.io) server that exposes the GameBot
automation API as tools, so MCP clients (Claude Code, Claude Desktop, etc.) can drive emulator
game automation directly: register games, start sessions, take screenshots, send taps/swipes,
author reference images, triggers, commands and sequences, and run scheduling queues.

The server is a thin stdio proxy over the GameBot service's REST API; it holds no state of its
own. Screenshot endpoints are returned as MCP image content so the client model can see the
emulator screen.

## Prerequisites

- Node.js 20+
- A running GameBot service (default `http://localhost:8080`)

## Build

```powershell
cd src\mcp-server
npm install
npm run build
```

## Configuration

Environment variables:

| Variable | Default | Purpose |
| --- | --- | --- |
| `GAMEBOT_API_URL` | `http://localhost:8080` | Base URL of the GameBot service |
| `GAMEBOT_API_TOKEN` | (none) | Bearer token, only if the service was started with auth enabled |
| `GAMEBOT_API_TIMEOUT_MS` | `60000` | Per-request timeout |

## Registering with Claude Code

Add to the repository's `.mcp.json` (or `claude mcp add`):

```json
{
  "mcpServers": {
    "gamebot": {
      "command": "node",
      "args": ["src/mcp-server/dist/index.js"],
      "env": {
        "GAMEBOT_API_URL": "http://localhost:8080"
      }
    }
  }
}
```

## Tool overview

| Group | Tools |
| --- | --- |
| System | `get_service_health`, `list_adb_devices` |
| Games | `list_games`, `get_game`, `create_game`, `update_game`, `delete_game` |
| Sessions | `start_session`, `list_running_sessions`, `get_session`, `get_session_health`, `session_snapshot`, `send_inputs`, `stop_session` |
| Screen and images | `emulator_screenshot`, `crop_capture`, `detect_reference_image`, `detect_all_reference_images`, `list_images`, `get_image`, `get_image_metadata`, `delete_image` |
| Triggers | `list_triggers`, `get_trigger`, `create_trigger`, `update_trigger`, `delete_trigger`, `test_trigger` |
| Commands | `list_commands`, `get_command`, `create_command`, `update_command`, `delete_command`, `force_execute_command`, `evaluate_and_execute_command`, `execute_step` |
| Sequences | `list_sequences`, `get_sequence`, `create_sequence`, `replace_sequence`, `patch_sequence`, `delete_sequence`, `validate_sequence`, `execute_sequence` |
| Queues and templates | `list_queues`, `get_queue`, `create_queue`, `update_queue`, `delete_queue`, `add_queue_entry`, `replace_queue_entries`, `remove_queue_entry`, `link_queue_template`, `link_queue_game`, `start_queue`, `stop_queue`, `live_schedule_sequence`, `list_queue_templates`, `get_queue_template`, `save_queue_template`, `delete_queue_template` |
| Execution logs | `list_execution_logs`, `get_execution_log`, `get_execution_log_subtree` |

## Typical flows

Screen authoring (screenshot, crop, detect):

1. `start_session` with a `gameId` and `adbSerial` (find serials with `list_adb_devices`)
2. `emulator_screenshot` - returns the screen image and a `captureId`
3. `crop_capture` with the `captureId` and bounds to save a reference image
4. `detect_reference_image` / `test_trigger` to verify detection works

Running automation:

1. Author commands (`create_command`) gated by triggers, compose them into sequences
   (`create_sequence`)
2. Test with `force_execute_command` / `execute_sequence`
3. Schedule with queues: `create_queue`, `replace_queue_entries` (or `link_queue_template`),
   `start_queue`
4. Diagnose runs with `list_execution_logs` and `get_execution_log_subtree`

## Notes and pitfalls

- Polymorphic JSON payloads (command steps, sequence actions) must put the `type` discriminator
  as the FIRST property of the object.
- Queue template `timerTimeOfDay` values are interpreted in the service's LOCAL time zone, not
  UTC.
- A failed sequence step aborts the remaining steps; place recovery steps at the start of a
  sequence rather than the end.
