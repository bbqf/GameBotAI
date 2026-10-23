# Data Model: Installer Bind Address and Port Control

## Entities

### Binding

The pair of bind interface and port on which the bot listens.

| Field | Type | Rule |
|-------|------|------|
| `host` | text | Not empty. No space, `/`, or `:`. Visible ASCII only in the installer. Default `127.0.0.1`. |
| `port` | whole number | 1 to 65535. Default: first free port of `8080,8088,8888,80` on a first install. |
| `source` | text | Where the active value came from: `Explicit`, `NetworkFile`, `LegacyRegistry`, `Default` (installer). `Environment`, `Configuration`, `NetworkFile`, `Default` (bot). |

Each field has its own source and its own fallback. A bad `host` does not change a good `port`.

### Saved binding (`network.json`)

The one saved location. See [contracts/network-file.md](contracts/network-file.md).

| Field | Type | Rule |
|-------|------|------|
| `bindHost` | text | Same rule as `host`. |
| `port` | text or number | Same rule as `port`. The installer writes it as text. |

### Legacy binding (registry)

`HKCU\Software\GameBot\Network` values `BindHost` and `Port`. The installer reads them only if the file has no valid value for that field. The installer deletes them after a successful commit. Nothing writes them after this feature.

### Shortcut address

A value that the installer derives. It is never stored as an input.

`SHORTCUT_URL = PROTOCOL + "://" + shortcutHost + ":" + port + "/"`

| `host` value | `shortcutHost` |
|--------------|----------------|
| `127.0.0.1` | `localhost` |
| any other value (`0.0.0.0` is one of them) | computer name of the PC |

### Upgrade request

An upgrade from the installer, the web UI, or the API. The web UI and API start the updater. The updater runs `msiexec` with no `PORT` and no `BIND_HOST`. The request carries no binding. The installer reads it from the saved binding.

## Installer properties

| Property | Kind | Role |
|----------|------|------|
| `BIND_HOST` | public, input and result | Empty means "not given". The resolver sets the final value. |
| `PORT` | public, input and result | Empty means "not given". The resolver sets the final value. |
| `PROTOCOL` | public | Not changed by this feature. |
| `SHORTCUT_URL` | derived | Set by the resolver. Used by the shortcut. |
| `BINDING_PREFILL_HOST`, `BINDING_PREFILL_PORT` | internal | The values the resolver pre-filled. They show which values the user did not give. |
| `NETWORK_BINDING_ERROR` | internal | Set by `ValidateNetworkBinding`. Empty when the binding is valid. The dialog shows it as an error text. |
| `NETWORK_REGISTRY_ROOT` | internal | The registry root of the legacy values under `HKCU`. Default `Software\GameBot\Network`. The test variant sets another root. The legacy read and the commit delete use it. |

Removed: `PERSISTED_BIND_HOST`, `PERSISTED_PORT`, `PERSISTED_FILE_BIND_HOST`, `PERSISTED_FILE_PORT`, `SHORTCUT_HOST`.

## Resolution of each field

```text
explicit value (command line, or user input in the dialog)
  -> network.json field
    -> legacy registry value
      -> first-install default
```

## State transitions

| Event | `network.json` | Legacy registry | Shortcut |
|-------|----------------|-----------------|----------|
| First install, defaults | written | none | from defaults |
| First install, user values | written | none | from user values |
| Upgrade, no change | rewritten with the same values | deleted at commit | from saved values |
| Upgrade, user changes a value | written with the new value | deleted at commit | from the new value |
| Upgrade from a pre-132 install | written from the registry value | deleted at commit | from migrated value |
| Upgrade fails, rollback | restored to the old state | kept | restored by MSI rollback |
| Uninstall | kept | removed with the component | removed |
