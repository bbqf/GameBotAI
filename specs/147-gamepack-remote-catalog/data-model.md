# Data Model: Remote gamepack catalog

## GamepackCatalogSettings (file `gamepack-catalog.json`)

| Field | Type | Rule |
|-------|------|------|
| location | string or null | Web address (`https`, or `http` under R5) or a full folder path. Null or empty = not configured. |
| allowInsecure | bool | Default false. Allows `http` for non-loopback hosts. |
| token | string or null | Secret. Never returned, never logged. |

Validation on write: a web address must parse as an absolute URI with an allowed scheme. A folder path must be rooted. The folder need not exist at write time.

## CatalogEntry (one version of one gamepack)

| Field | Type | Rule |
|-------|------|------|
| name | string | Valid gamepack name (`GamepackNameRules`). |
| version | string | Valid version. Invalid = entry skipped. |
| changelog | string | Empty when missing. |
| file | string | Relative location. Must stay under the catalog base. |
| signature | string or null | Optional detached signature file location, same path rule as `file`. Same download rules as `file` (same host, token rule, request time limit), 1 MB size limit. Null = the signature check sees "missing". |
| sha256 | string or null | 64 hex chars when set. |

Duplicate (name, version): the first entry wins.

## CatalogListItem (API result)

- name
- versions: list of { version, changelog, installed (bool), updateAvailable (bool) }, from new to old
- installedVersion: string or null

`updateAvailable` is true when no version is installed, or the version is newer than the installed one. The "installed" flag is true only for the version equal to the installed one.

## Catalog failure reasons (code)

`not_configured`, `invalid_location`, `unreachable`, `unauthorized`, `forbidden`, `not_found`, `index_invalid`, `index_too_large`, `file_missing`, `file_too_large`, `hash_mismatch`, `redirect_refused`, `timeout`, `entry_not_listed`.

The code `entry_not_listed` is the only reason for a name or version that the index does not list. It is returned before any download.

A failed signature file download uses the same codes as a failed gamepack file download (`file_missing`, `file_too_large`, `unreachable`, `timeout`, and so on).

A missing `index.json` gives `index_invalid` (not `not_found` and not `file_missing`). The code `not_found` applies only to a file download that the catalog answers with "not found" for a listed entry, if used at all.

## State transitions

None stored. A catalog install ends in the existing install outcome states (feature 143 and 146). A failure before `InstallAsync` leaves all state unchanged.
