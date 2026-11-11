# Contract: Local Auth Command

The command is a mode of the service executable. It does not start the web server. It works when the service runs and when the service does not run.

```text
GameBot.Service.exe auth <command> [--data-dir <path>]
```

The command uses the same storage root rules as the service (`Service:Storage:Root`, `GAMEBOT_DATA_DIR`, then `data` next to the executable). `--data-dir` sets the root directly.

| Command | Result | Exit code |
|---------|--------|-----------|
| `auth new-code` | Makes a registration code, valid for 10 minutes. Cancels an older code. Prints the code and the registration link. | 0 |
| `auth list` | Prints each passkey: id, name, registration time, last use. Prints the number of active sessions. | 0 |
| `auth remove <id>` | Removes the passkey and ends its sessions. | 0; 2 if the id is unknown |
| `auth end-sessions` | Ends all sessions. | 0 |
| `auth` or `auth --help` | Prints the usage text. | 0 |
| unknown command or bad arguments | Prints the error and the usage text to the error stream. | 2 |
| file error (no access, lock not free after retry) | Prints the cause to the error stream. | 1 |

## Output of `auth new-code`

```text
Registration code: K7QF-9XWM
The code is valid for 10 minutes. You can use it one time.
Open this page on the new device: https://bot.example.com/register
```

If `Service:Auth:PublicOrigin` is not set, the third line is: `Set GAMEBOT_AUTH_PUBLIC_ORIGIN first. Then open /register at the public address.`

If the sign-in function is off, the command adds: `Warning: the sign-in function is off. Set GAMEBOT_AUTH_ENABLED=true.`

## Rules

- The command writes a sign-in record with source `local-command` for `remove` and `end-sessions`.
- The command never prints a session token, a hash, or key material.
- No web endpoint has these functions without a session, and no web endpoint makes a code (FR-007).
