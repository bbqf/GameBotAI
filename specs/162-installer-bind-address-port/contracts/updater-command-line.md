# Contract: Updater Command Line

The bot starts `GameBot.Updater.exe`. The updater starts `msiexec`.

## Updater arguments

All values come as `--name value` pairs.

| Argument | Required | Change in this feature |
|----------|----------|------------------------|
| `--bot-pid`, `--msi`, `--install-folder`, `--result-path`, `--attempt-id`, `--target-version` | Yes | None |
| `--data-root`, `--log-path`, `--from-version`, `--started-at` | No | None |
| `--port` | No | **Removed.** The bot never passed it. |
| `--bind-host` | No | **Removed.** The bot never passed it. |

The parser ignores an unknown `--name value` pair, as it does now. An old caller that passes `--port` does not fail.

## msiexec command line

```text
/i "<msi>" /qn /norestart [/l*v "<log>"] APPLICATIONFOLDER="<folder>" MSIINSTALLPERUSER=1
```

`PORT` and `BIND_HOST` are no longer in the command line. The MSI reads the saved binding from `network.json` (FR-010).

## Result

A UI-started or API-started upgrade keeps the binding and both Start-Menu shortcuts. A failed upgrade rolls back and keeps the old state (FR-012).
