# Gamepacks

A gamepack is one JSON file. It has a manifest and lists of sequences, commands, and images. The service installs
all objects of a gamepack as one unit: all objects are installed, or none. The service records the gamepack name and
version as the owner of each object. This page tells how to use the three routes. For the design, read
`docs/architecture.md` (section "Gamepack install") and `specs/143-gamepack-install/`.

## File format

```json
{
  "manifest": { "name": "pns-daily", "version": "1.2.0", "changelog": "Fix help sequence.", "requiredApiMajor": 1 },
  "sequences": [ { "id": "a1", "name": "Help All", "steps": [ { "order": 0, "stepId": "s0", "commandId": "c1", "stepType": "Command" } ] } ],
  "commands": [ { "id": "c1", "name": "Tap help", "steps": [ { "type": "KeyInput", "order": 0, "keyInput": { "key": "BACK" } } ] } ],
  "images": [ { "id": "help-icon", "contentType": "image/png", "filename": "help-icon.png", "dataBase64": "iVBOR..." } ]
}
```

- A file with three empty lists is valid. It installs a gamepack with no objects.
- A sequence entry and a command entry use the JSON of the stored object, as the service writes it to the data folder.
  Names of members are not case-sensitive. An enum can be a number or a name.
- `manifest.name`: 1 to 64 characters from letters, digits, `_`, `-`, and `.`. The name does not start with `.`. The name
  is not a reserved Windows device name (`CON`, `PRN`, `AUX`, `NUL`, `COM1` to `COM9`, `LPT1` to `LPT9`, with or without
  an extension). Two names that differ only in case are the same gamepack.
- `manifest.version`: text. It must not be empty. The service does not order versions.
- `manifest.requiredApiMajor`: optional. It is a whole number of 1 or more. The install and the dry run refuse a file when
  it differs from the running API major version. The service stores it and shows it in the list. See the section "API
  version and compatibility".
- An image has `id` (its name), `contentType` (`image/png` or `image/jpeg`), an optional `filename`, and `dataBase64`. The
  decoded image has at most 10,000,000 bytes. The image `id` follows the rule of the existing image ids.
- A request has at most 100 MB. A larger request gets a 413 problem response, and nothing changes.

## Routes

### POST /api/gamepacks/install

Query `dryRun=true` checks the file and changes no data. Without `dryRun`, the route installs the gamepack.

| Status | Meaning |
|--------|---------|
| 200 | The file is valid and the install worked. For a dry run: the response shows `valid` and the planned counts. A dry run with problems also gives 200 with `valid: false`. |
| 400 | The body is not valid JSON. |
| 413 | The request is larger than 100 MB. |
| 422 | The check found problems. Nothing changed. The body has the list `problems`. |
| 500 | A write failed. All objects stay as before. If the restore also failed, the body has `"result": "partial_restore"`. |

The check finds all problems. It does not stop at the first. A problem has `category` (`structure`, `reference`, or
`conflict`), `objectType`, `name`, `owner` (text), and `message`.

- **Structure**: a missing manifest field, a bad name, two objects with the same name in the file, a bad image type, bad
  base64, an image larger than the limit, or a `requiredApiMajor` that differs from the running API major version.
- **Reference**: a step names a command or an image that is not in the file and not in the installation. A target that the
  install removes does not count.
- **Conflict**: an object in the file has the same type and name as an object that the user made or that another gamepack
  owns. Names match without case. The `owner` is `user`, or `<name> <version>` for a gamepack. An object of the same gamepack
  is an update, not a conflict.

An update keeps the stored id of each existing object with the same type and name. The service rewrites the references in
the file to the kept ids. A new object takes the id of the file when that id is free. A new object never takes the id of a
stored object. When an update drops an object that the old version owned, the service removes it. The service never
changes or removes an object that no gamepack owns.

### GET /api/gamepacks

The route lists the installed gamepacks: `name`, `version`, `changelog`, `requiredApiMajor`, `hasPreviousVersion`, and
`installedAtUtc`. An empty installation gives `[]`.

### POST /api/gamepacks/{name}/rollback

The route returns a gamepack to its kept previous version. The name matches without case.

- When an install replaces a different version, the service first saves the live content of the old version (including your
  hand edits) as the previous version. An install of the same version again does not replace the previous version.
- Only one previous version is kept. After a rollback, no previous version is kept.
- The rollback runs the same conflict and reference check as an install on the kept content. If the check finds a problem,
  the route gives 422 with `"result": "refused"` and the problems. Nothing changes.
- 200 `rolled_back` (with `created`, `updated`, and `removed`), 404 `not_found`, 409 `no_previous_version`, and 500 as for an
  install.
- Hand edits that you make after the install of the newer version are lost by a rollback.

No route uninstalls a gamepack.

## API version and compatibility

The platform has one API version. The next line shows it. A test keeps this line equal to the code.

API version: 1.0.0, schema format 1

The rule: inside one major version, a sequence or command that was valid stays valid. A change that breaks this rule
raises the major version.

- **Major**: a change that can make an existing valid sequence or command invalid.
- **Minor**: a new feature that keeps all existing content valid.
- **Patch**: a fix that does not change the contract.
- **Schema format**: the version of the stored format of sequences and commands.

Read the version with `GET /api/version`. The response has `major`, `minor`, `patch`, and `schemaFormat`. Two calls give the
same values.

A gamepack can state `manifest.requiredApiMajor`. If the value differs from the running major version, the install and the
dry run refuse the file. The install gives 422, and the dry run gives 200 with `valid: false`. The problem has the
category `structure`, the object type `manifest`, and a message such as
`The gamepack needs API major version 2. This platform runs API major version 1.` A value below 1 gives the message
`The required API major version must be a whole number of 1 or more.` A value that is not a whole number gives the message
`The manifest has a wrong shape.` Nothing changes when the check finds a problem. Minor and patch numbers are not compared.

The rollback does not check the API major version. It restores content that the platform accepted before.

## The owner field

A read of a sequence, a command, or an image shows a read-only `owner`:

- `{ "kind": "user" }` for an object that no gamepack owns (every object made by hand).
- `{ "kind": "gamepack", "name": "pns-daily", "version": "1.2.0" }` for an object of a gamepack.

A write request ignores the field. A conflict report uses text instead: `user` or `pns-daily 1.2.0`.

The sequence and command reads (single and list) show `owner`. The image metadata route (`GET /api/images/{id}/metadata`)
shows `owner`. The image list route adds `items` (`id` and `owner`) next to `ids`.

## Hand changes

- A hand edit of an owned object works as before. The owner stays. The next install or rollback of the gamepack replaces
  the edit.
- A hand create with the name of an owned object of the same type gives 409. The owner does not change.
- A hand delete of an owned object works as before. The service removes the owner entry at once. An object that you make by
  hand later with the same name belongs to the user, and the next install of the gamepack is then refused as a conflict.
  If no such object exists, the next install makes the deleted object again, and a rollback restores it.
- Avoid hand changes while an install runs. The hand routes do not wait for the install. A change that runs at the same time
  can be lost or can fail.
- The service does not check the references from your own objects to an object that an install drops.

## Back up and restore settings

Use `GET /api/queue-settings/export` to save the queue settings and your own artifacts in one JSON document. Use
`POST /api/queue-settings/import` to put the document back on this or on another instance. One export request and one import
request are enough.

What the document holds:

- The settings of all queues, with the entries of each queue as sequence names. The device fields (`emulatorSerial`,
  `emulatorInstanceName`, `emulatorInstanceIndex`) are exported as stored and imported as given. Edit them after you import on a
  new machine.
- All queue templates.
- The sequences and commands that no gamepack owns, in full.
- A `references` list. It names each gamepack-owned object that your queues, templates, sequences, or commands use, with the
  gamepack name and version. It has no content of these objects. Install the gamepack on the target instance first.
- No id and no time stamp of the instance. All links use names. Case is ignored in names.

How the import works:

- It checks the whole document first. A bad JSON body, a `formatVersion` other than 1, an empty name, or a repeated name gives
  status 400 and changes nothing.
- It makes or updates objects by name, in this order: commands, sequences, queue templates, queues.
- It never changes an object that a gamepack owns. An object in the document with the name of an owned object is `skipped`.
- It never deletes an object.
- A second import of the same document changes nothing (all items are `unchanged`).
- A link whose target is missing does not fail the import. The report lists it in `unresolvedReferences`, with the gamepack
  name and version when the document has them.
- It does not stop or start a queue. A queue that is not stopped and that the import updates has `restartRequired: true`.
  Stop and start it to use the new settings.
- It waits while a gamepack install or rollback runs, and an install waits while the import runs.

If an import fails with status 500, send the same document again. The import is safe to repeat and it finishes the items that it
did not apply. The 500 body has a `report` with `items` (applied), `failed` (the item that failed), and `notApplied`.

## Signed gamepacks

A gamepack can have one detached signature file. The service can check the signature before an install. The check tells you
if the content is the content that a builder you trust has signed.

### The policy

The instance has one policy. The default is `off`.

| Policy | What the install does |
|--------|-----------------------|
| `required` | A gamepack with a status other than `valid` gets 422. Nothing changes. |
| `optional` | Every gamepack installs. The result shows the status. |
| `off` | No check. The route does not read the signature. The status is `not_checked`. |

Read the policy with `GET /api/gamepacks/signature/policy`. Set it with `PUT /api/gamepacks/signature/policy` and the body
`{ "policy": "required" }`. A value that is not known gives 400. The next install uses the new value. A restart is not needed.

### The trusted keys

A gamepack is `valid` only if the key that signed it is on the trusted list. The label of a key does not change trust.

- `GET /api/gamepacks/signature/keys` lists the label and the fingerprint of each key. It does not show the key text.
- `POST /api/gamepacks/signature/keys` adds a key. The body is `{ "label": "Builder A", "publicKey": "<base64>" }`. The key is an
  ECDSA P-256 public key in the base64 SubjectPublicKeyInfo form. The response is 201. A bad key or label gives 400, and
  nothing is saved. A key that is on the list already gives 409.
- `DELETE /api/gamepacks/signature/keys/{fingerprint}` removes a key. The response is 204, or 404 if no key has this fingerprint.

The fingerprint is the SHA-256 of the SubjectPublicKeyInfo bytes, in lower case hex. The policy and the keys are in the file
`<data root>/gamepack-signature.json`. If the file is damaged, the service uses the policy `required` and no keys, so a bad
file never opens the gate. Set the policy again to repair the file.

### Send a signature with an install

Send `POST /api/gamepacks/install` as `multipart/form-data` with the part `gamepack` (the gamepack file) and the part `signature`
(the signature file). A request with the gamepack file as `application/json` still works. It has no signature, so its status is
`missing` (or `not_checked` when the policy is `off`).

The response has a `signature` block: `{ "status": "valid", "policy": "required", "wouldReject": false }`.

| Status | Meaning |
|--------|---------|
| `valid` | The signature is correct, the content matches, and the key is trusted. |
| `missing` | No signature was sent. |
| `invalid` | The signature is empty, has a wrong format, or does not pass the crypto test. |
| `content_mismatch` | The gamepack is not the content that was signed (a changed, extra, or missing file). |
| `untrusted` | The signature is correct, but the key is not on the trusted list. |
| `not_checked` | The policy is `off`. |

If more than one fault applies, the status is the first one in this order: `missing`, `invalid`, `content_mismatch`, `untrusted`.
A rejected install gives 422 with `result: "signature_rejected"` and a problem with the category `signature` and the code
`signature_<status>`. The text names the status only. A dry run (`dryRun=true`) gives 200 and shows `wouldReject`. It is `true`
only when the policy is `required` and the status is not `valid`. Rollback and list do not check a signature.

### The signature file

The signature file is UTF-8 JSON:

```json
{
  "format": "gamepack-signature-1",
  "algorithm": "ecdsa-p256-sha256",
  "publicKey": "<base64 SubjectPublicKeyInfo of a P-256 key>",
  "manifestSha256": "<64 lower case hex characters>",
  "files": [
    { "type": "command", "name": "Help All", "sha256": "<64 hex>" },
    { "type": "image", "name": "pit-title", "sha256": "<64 hex>" },
    { "type": "sequence", "name": "Pit Mining", "sha256": "<64 hex>" }
  ],
  "signature": "<base64 of 64 bytes: r then s>"
}
```

How to sign a gamepack:

1. Read the gamepack file as raw JSON. Make the canonical JSON of each value: object keys sorted by ordinal compare, no white
   space, string escapes `\"`, `\\`, `\b`, `\f`, `\n`, `\r`, `\t`, and `\u00xx` (lower case hex) for other control characters
   below U+0020. Other characters stay as UTF-8. An integer has no fraction and no exponent. A field that the platform does not
   know is part of the hash.
2. `manifestSha256` is the SHA-256 (lower case hex) of the canonical `manifest` object.
3. For each entry of `sequences`, `commands`, and `images`, add one item to `files`. `type` is `sequence`, `command`, or `image`.
   `name` is the `name` of a sequence or command, or the `id` of an image. `sha256` is the hash of the canonical entry.
4. Make the bytes to sign: the canonical JSON of `{ "files": [...], "format": "gamepack-signature-1", "manifestSha256": "..." }`.
   Sort `files` by `type`, then by lower case `name` (ordinal). Each item has the keys `name`, `sha256`, and `type`.
5. Sign the bytes with ECDSA P-256 and SHA-256 in the IEEE P1363 form (r then s, 64 bytes). In .NET, use
   `ECDsa.SignData(bytes, HashAlgorithmName.SHA256)`. Write the result as base64.
6. Write the signature file with your public key (base64 SubjectPublicKeyInfo). Give the public key to the people who install
   your gamepack, so they can add it to the trusted list.

The key in the signature file is only used for the crypto test. Trust comes only from the trusted list of the instance. Each
check writes one log message with the status and the policy. The message has no key text.

## Remote catalog

An operator can set one catalog. A catalog is a web address or a local folder. It has one file, `index.json`, and the gamepack files. The operator lists the versions, reads the change text, and installs one version by hand. The platform never installs from the catalog by itself. It has no timer and no cache. Each list request reads the index again.

### Settings

`GET /api/gamepacks/catalog/settings` gives `location`, `allowInsecure`, and `tokenSet`. It never gives the token. `PUT /api/gamepacks/catalog/settings` sets them. In the body, `token` null keeps the saved token, an empty text clears it, and other text sets it. A change applies to the next request. A restart is not needed. The settings are in `<data root>/gamepack-catalog.json`.

The token and the "allow insecure" flag apply to web addresses only. A folder catalog ignores both. Scheme rules: `https` always works. `http` works for a loopback host, or when `allowInsecure` is true. The token goes only to the catalog host.

### The index

The index is at `<location>/index.json`. A file location in the index is relative to the catalog location. The index has at most 5 MB.

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

`formatVersion` must be 1. `changelog`, `signature`, and `sha256` are optional. The platform skips a version entry that has an invalid version, an invalid hash, or a file location that is absolute, has "..", has a backslash, or leaves the catalog. For a duplicate name and version, the first entry wins.

### Routes

- `GET /api/gamepacks/catalog` lists the gamepacks with the versions (new to old), the change text, `installed`, and `updateAvailable`. It gives 409 `not_configured` when no catalog is set.
- `POST /api/gamepacks/catalog/install` with `{ "name", "version", "dryRun" }` installs one listed version, or checks it with a dry run. An unlisted name or version gives 404 `entry_not_listed` before any download. The install uses the same flow and the same signature policy as `POST /api/gamepacks/install`. It downloads the optional signature file of the entry with the same rules as the gamepack file. When the entry has a `sha256`, the platform compares it with the file.

### Limits and errors

Limits: index 5 MB, gamepack file 100 MB, signature file 1 MB, 30 seconds for each request, and at most 3 redirects to the same host and scheme. A failure leaves the instance as it was. Reason codes: `not_configured`, `invalid_location`, `unreachable`, `unauthorized`, `forbidden`, `not_found`, `index_invalid` (also for a missing `index.json`), `index_too_large`, `file_missing`, `file_too_large`, `hash_mismatch`, `redirect_refused`, `timeout`, `entry_not_listed`.

### Web interface

The "Gamepacks" area (`/gamepacks`) has full phone support. It has the settings form, the list, an Update button (label "Install" when none is installed) for each newer version, a "Dry run" check box, and a "Refresh" button. The list loads on open and on Refresh only.

## Where the data is

`<data root>/gamepack-signature.json` holds the signature policy and the trusted keys.
`<data root>/gamepack-catalog.json` holds the catalog location, the allow insecure flag, and the token.
`<data root>/gamepacks/registry.json` holds the installed gamepacks and the owner records. `<data root>/gamepacks/previous/<name>.json`
holds the kept previous version of each gamepack (the name is in lower case). The service writes each file to a temporary
file first and then replaces the old file.
