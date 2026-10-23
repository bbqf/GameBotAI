# Contract: Runtime Host and Port Names in the Bot

## Names

| Value | Main name | Alias |
|-------|-----------|-------|
| Host | `GAMEBOT_BIND_HOST` | `Service:Network:BindHost` (env form `Service__Network__BindHost`) |
| Port | `GAMEBOT_PORT` | `Service:Network:Port` (env form `Service__Network__Port`) |

No other name sets the host or port. `ASPNETCORE_URLS` and `--urls` still replace the whole address (not changed by this feature).

## Priority for each value

1. Configuration alias `Service:Network:*` (this includes `Service__Network__*` from the environment)
2. Environment variable `GAMEBOT_*`
3. `network.json`
4. Default (`127.0.0.1`, `8080`)

The bot does not read the registry. An override is never written to `network.json`.

An invalid port in an override becomes the default `8080` (not changed). An invalid saved value is ignored with a warning.

## Active value and source

`ResolveListenAddress` returns:

| Field | Values |
|-------|--------|
| `Host`, `Port` | The active values. |
| `HostSource`, `PortSource` | `Configuration`, `Environment`, `NetworkFile`, `Default` |
| `Problems` | Warnings for saved values that are not valid. |

## Configuration snapshot

The snapshot has one parameter row for each value: `GAMEBOT_BIND_HOST` and `GAMEBOT_PORT`.

| Field of the row | Value |
|------------------|-------|
| `Name` | `GAMEBOT_BIND_HOST` or `GAMEBOT_PORT` |
| `Value` | The active value from `ResolveListenAddress`. |
| `Source` | `Configuration`, `Environment`, `NetworkFile`, or `Default`. |

The rows `Service__Network__BindHost` and `Service__Network__Port` are removed from the defaults.

The two rows are read-only. A request that updates one of them gets the error: `The host and port come from the installer. Change them with the installer or in network.json.` The current endpoint and status code for rejected updates stay.

## Bind failure

If the bot cannot bind the active address, it writes an error to its log. The error names the host, the port, and the source of each. The bot then exits with a non-zero code. The web server does not start, so no status page can show the failure.
