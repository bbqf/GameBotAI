# Contract: Saved Binding File

**Path**: `<data root>\config\network.json`. The file is the one saved location of the host and port. This contract does not change the format of feature 132.

## Format

```json
{
  "bindHost": "127.0.0.1",
  "port": "8080"
}
```

| Field | Type | Rule |
|-------|------|------|
| `bindHost` | text | Not empty. No space, `/`, `:`, `"`, or `\`. |
| `port` | text or number | Whole number, 1 to 65535. The installer writes text. The bot accepts both. |

## Rules

1. Each field is checked alone. A bad field is ignored and a warning goes to the log. A good field stays valid.
2. A file that is not valid JSON is ignored. A warning goes to the log.
3. Writers: the installer only. The bot and the updater do not write the file.
4. Readers: the installer and the bot.
5. The installer writes the file in a deferred action with an undo marker. The rollback action restores the old state. The commit action removes the markers and deletes the legacy registry values.
6. Uninstall keeps the file.
7. Character set: ASCII, no byte order mark. The reader ignores a byte order mark.

## Legacy source

`HKCU\Software\GameBot\Network` values `BindHost` and `Port`. Only the installer reads them. It reads a field only if the file has no valid value for that field. After a successful commit, the installer deletes both values.
