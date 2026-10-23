# Contract: Installer Properties and Dialog

## Public properties

These names do not change. A silent install or upgrade can set them on the command line.

| Property | Allowed values | Empty means |
|----------|----------------|-------------|
| `BIND_HOST` | Host name or address. No space, `/`, or `:`. | Not given. Use the saved value, else `127.0.0.1`. |
| `PORT` | Whole number, 1 to 65535. | Not given. Use the saved value, else the first free port of `8080,8088,8888,80`. |

An explicit value replaces the saved value. The installer saves the new value in `network.json`.

## Removed properties

`PERSISTED_BIND_HOST`, `PERSISTED_PORT`, `PERSISTED_FILE_BIND_HOST`, `PERSISTED_FILE_PORT`, `SHORTCUT_HOST`. These properties were never public. The installer does not handle a command line that sets one.

## Derived property

`SHORTCUT_URL` = `PROTOCOL://<shortcutHost>:<PORT>/`. `shortcutHost` is `localhost` when `BIND_HOST` is `127.0.0.1`. For all other values, it is the computer name. The user cannot set it.

## Internal properties

| Property | Role |
|----------|------|
| `NETWORK_BINDING_ERROR` | Set by `ValidateNetworkBinding`. Empty when the binding is valid. The dialog shows it and stays on the page when it is not empty. |
| `NETWORK_REGISTRY_ROOT` | Registry root of the legacy values under `HKCU`. Default `Software\GameBot\Network`. The legacy read and the commit delete use it. Only the test variant changes it. |
| `BINDING_PREFILL_HOST`, `BINDING_PREFILL_PORT` | The values that the resolver pre-filled. |

## Dialog behavior

| Run type | Dialog |
|----------|--------|
| Interactive first install | `WelcomeDlg`, `InstallDirDlg`, `NetworkConfigDlg`. The install starts when the user clicks **Next** on `NetworkConfigDlg` (this page ends the dialog, as before). |
| Interactive upgrade | Same pages. `NetworkConfigDlg` shows the saved values. |
| Interactive same-build reinstall | Same pages, after the bundle prompt. The binding stays unless the user changes it. |
| Silent (any) | No dialog. The same priority applies. |

`NetworkConfigDlg` rules:

1. The **Bind interface** list has `localhost (127.0.0.1)`, `all interfaces (0.0.0.0)`, and the interfaces that `ipconfig` finds. The list shows a saved value that is not in the list.
2. The **Port** field accepts text. **Next** runs `ValidateNetworkBinding`.
3. An invalid port gives the message: `Enter a port number from 1 to 65535.` The page stays.
4. A port in use by another program gives the message: `Port <n> is in use by another program. Enter a different port.` The page stays.
5. A port that the installed GameBot service itself holds does not count as in use. The page shows no message.

## Port check

1. The installer runs `netstat -ano -p tcp`.
2. It reads only lines in the `LISTENING` state. It compares the port with the local address only. A match in the foreign address or in another state does not count.
3. For each match, it reads the process id. It asks WMI (`Win32_Process`) for the process name and the executable path.
4. A match is "held by GameBot" when the process is `GameBot.Service.exe` and its path is inside `APPLICATIONFOLDER`. All other matches are "in use".
5. If the installer cannot read the process, it counts the port as "in use".
6. A listener on `0.0.0.0` or `::` counts for every interface. A listener on a specific address counts only for the same address or for `0.0.0.0`.

## Installer log lines

| Event | Line |
|-------|------|
| Explicit value (command line or dialog) | `Binding: <field>=<value> (source: explicit)` |
| Value from the file | `Binding: <field>=<value> (source: network.json)` |
| Value from the legacy registry | `Binding: <field>=<value> (source: legacy registry, migrated)` |
| Default value | `Binding: <field>=<value> (source: default)` |
| Silent port in use (saved value) | `WARNING: port <n> is in use. The saved port is kept.` |
| Silent port in use (given value) | `WARNING: port <n> is in use. The given port is kept.` |
| Value that is not valid | `WARNING: The given BIND_HOST value is not valid. The value is ignored.` (and the same for `PORT`) |
| Shortcut address | `Shortcut: <SHORTCUT_URL>` |

## Shortcut

| Shortcut | Target |
|----------|--------|
| `GameBot` | `rundll32.exe url.dll,FileProtocolHandler [SHORTCUT_URL]` |
| `GameBot Background` | `GameBot.Service.exe` (no address) |

Both shortcuts exist after an install, an upgrade, and a UI-started upgrade.

## Exit behavior

A port conflict does not change the exit code of a silent run. Exit code 0 or 3010 stays success.
