# Contract: network settings file

**Path**: `<data root>\config\network.json`
(default: `%LOCALAPPDATA%\GameBot\data\config\network.json`)

**Installer path**: `[DATAROOTFOLDER]config\network.json`. `DATAROOTFOLDER` is valid after `CostFinalize`.

**Writer**: the installer, with the deferred JScript action `WritePersistedNetworkFile`. Its `CustomActionData` is `[DATAROOTFOLDER]|[BIND_HOST]|[PORT]`. It creates the `config` folder when it does not exist. Before the change it saves the old file as `network.json.bak` (or the marker `network.json.none`). It writes `network.json.tmp`, then replaces the file. The rollback action `RollbackPersistedNetworkFile` (`CustomActionData` is `[DATAROOTFOLDER]`) restores the old file or the old "no file" state and removes any `.tmp` file. The commit action `CommitPersistedNetworkFile` (`CustomActionData` is `[DATAROOTFOLDER]`) removes the backup, the marker, and any `.tmp` file. The service never writes this file.

**Readers**: the installer (`ReadPersistedNetworkFile`, immediate, after `CostFinalize`) and the service (`PersistedNetworkSettings`).

## Format

```json
{
  "bindHost": "0.0.0.0",
  "port": "9090"
}
```

- UTF-8 without a byte order mark.
- Two string fields. Both are optional for a reader.
- A reader ignores unknown fields.
- `port` is a string, so the JScript writer needs no number rules. A reader accepts a JSON number too.

## Reader rules

- File missing: no value, no message.
- File not readable or not valid JSON: no value, one warning message that names the file.
- Field present but not valid (see data-model.md): no value for this field, one warning message that names the field and the reason.
- The reader never throws to its caller.

## Lifetime

The uninstall does not remove the file. The MSI does not track it.

## Related contract (unchanged)

The updater command line keeps `--port` and `--bind-host` as optional arguments. The bot no longer sends them.
