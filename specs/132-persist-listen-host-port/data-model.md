# Data Model: Persist the Listen Host and Port

## Persisted network setting

A pair of two text values that the user chose in the installer.

| Field | Type | Rule |
|-------|------|------|
| `bindHost` | string | Not empty. No spaces. No `/` or `:` characters. Example: `127.0.0.1`, `0.0.0.0`. |
| `port` | string (whole number) | 1 to 65535. |

A field that breaks its rule is ignored. The other field is still used. The system uses the next source for the ignored field and writes a log message.

### Sources (highest priority first)

Service:

1. Configuration (`Service:Network:BindHost`, `Service:Network:Port`)
2. Environment variable (`GAMEBOT_BIND_HOST`, `GAMEBOT_PORT`)
3. Saved file `<data root>\config\network.json`
4. Older registry value `HKCU\Software\GameBot\Network` (`BindHost`, `Port`)
5. Default (`127.0.0.1`, `8080`)

Installer:

1. Explicit option or dialog value (`BIND_HOST`, `PORT`)
2. Saved file
3. Older registry value
4. Port detection (first install) or default

Each field uses this order alone. A host can come from the file while the port comes from the registry.

## File: `config\network.json`

See [contracts/network-settings-file.md](contracts/network-settings-file.md).

## State changes

| Event | Effect on the file |
|-------|--------------------|
| First install | The installer writes the values it ends with (defaults and detected port). |
| Update from the Web UI | The installer reads the file, uses it, and writes the same values. |
| Manual update, no option | Same as above. |
| Manual update, option given | The option replaces the value. The installer writes the new value. |
| Install or update cancelled or rolled back | The file is as before (restored from the backup, or removed when it was new). |
| Update or install under another account, same data path | The installer finds the file by path and uses it. |
| Update or install under another account, other data path | No file. First-install rules apply. |
| Uninstall | The file stays. The data directory stays. |
| Reinstall into the same data directory | The installer reads the file and uses it. |
| Reinstall into another data directory | No file. First-install rules apply. |
| Service start | Read only. The service never writes the file. |

## Installer properties (MSI)

| Property | Meaning |
|----------|---------|
| `BIND_HOST`, `PORT` | Final values. Empty at the start means "not given". |
| `PERSISTED_BIND_HOST`, `PERSISTED_PORT` | Older registry values (exist already). |
| `PERSISTED_FILE_BIND_HOST`, `PERSISTED_FILE_PORT` | New. Values from `network.json`, after validation. Set after `CostFinalize`. |
| `DATAROOTFOLDER` | The data path (Directory id, full path with trailing `\`). Valid after `CostFinalize`. The file is `[DATAROOTFOLDER]config\network.json`. |
| `WritePersistedNetworkFile` | `CustomActionData` of the deferred write action: `[DATAROOTFOLDER]\|[BIND_HOST]\|[PORT]` (3 parts). |
| `RollbackPersistedNetworkFile` | `CustomActionData` of the rollback action: `[DATAROOTFOLDER]` (1 part, the data path). |
| `CommitPersistedNetworkFile` | `CustomActionData` of the commit action: `[DATAROOTFOLDER]` (1 part, the data path). |

The write step creates the `config` folder when it does not exist. It keeps temporary files in `config\` during an install: `network.json.bak` (old content) or `network.json.none` (no old file), and `network.json.tmp` (new content before the replace). The commit action removes all three. The rollback action uses the marker to restore the old file or the old "no file" state, and removes all three, also after a partial write.
