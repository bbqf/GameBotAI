# Contract: Catalog index document

## Location

- Web address: `<location>/index.json`. The location gets a trailing "/" when it has none.
- Folder: the file `index.json` in the folder.
- A file location in the index is relative to the catalog location.

## Shape

```json
{
  "formatVersion": 1,
  "gamepacks": [
    {
      "name": "pns-daily",
      "versions": [
        {
          "version": "1.2.0",
          "changelog": "Fix the bank step.",
          "file": "pns-daily/1.2.0/pns-daily.gamepack.json",
          "signature": "pns-daily/1.2.0/pns-daily.sig.json",
          "sha256": "<64 hex chars>"
        }
      ]
    }
  ]
}
```

## Rules

1. `formatVersion` must be 1. Other values give `index_invalid`.
2. `signature`, `sha256`, and `changelog` are optional. `signature` is the location of the detached signature file of that version, in the same version entry as `file`. It is relative to the catalog location. The platform downloads it with the same rules as `file`, with a 1 MB size limit.
3. A `file` or `signature` value is rejected when it: is absolute, has a scheme or host, has "..", has a backslash, or resolves outside the base.
4. A rejected or invalid version entry is skipped. Other entries stay.
5. Duplicate (name, version): the first entry wins.
6. The index is at most 5 MB.
