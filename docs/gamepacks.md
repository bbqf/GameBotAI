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
  "images": [ { "id": "help-icon", "contentType": "image/png", "filename": "help-icon.png", "dataBase64": "iVBOR..." } ],
  "seedTemplates": [ { "name": "Daily Help", "entries": [ { "sequence": "Help All", "scheduleType": "Timer", "timerTimeOfDay": "03:30:00" } ] } ]
}
```

- A file with three empty lists is valid. It installs a gamepack with no objects.
- A sequence entry and a command entry use the JSON of the stored object, as the service writes it to the data folder.
  Names of members are not case-sensitive. An enum can be a number or a name.
- An Action step that runs a command has these forms. Each form keeps the command action after the install:
  - `primitiveAction` with `payload`, for example `"primitiveAction": { "type": "command", "payload": { "commandId": "c1" } }`.
  - `commandReference` with `commandId` and `commandName`. The member `isResolved` is accepted. The install does not store it.
  - The step member `commandId`.
  - The stored form: `action` with `parameters`. An export writes this form. If a step has `action` and `primitiveAction`,
    `action` wins.
  - A step in a Loop `body`, and in the `body` or `elseBody` of a conditional step, follows the same rules.
- Only an Action step takes `primitiveAction`, `commandReference`, and `commandId` as these forms. On another step type,
  these members give a `structure` problem. A Command step keeps its stored member `commandId`.
- The dry run gives a `structure` problem (with the sequence name, the step ID, and the field name) for a step member that
  the install cannot keep, and for members that name different commands. A step that names a command that is not in the
  file and not on the system gives a `reference` problem. If there is a problem, the install does not create the sequence.
- `manifest.name`: 1 to 64 characters from letters, digits, `_`, `-`, and `.`. The name does not start with `.`. The name
  is not a reserved Windows device name (`CON`, `PRN`, `AUX`, `NUL`, `COM1` to `COM9`, `LPT1` to `LPT9`, with or without
  an extension). Two names that differ only in case are the same gamepack.
- `manifest.version`: text. It must not be empty. The service does not order versions.
- `manifest.requiredApiMajor`: optional. It is a whole number of 1 or more. The install and the dry run refuse a file when
  it differs from the running API major version. The service stores it and shows it in the list. See the section "API
  version and compatibility".
- An image has `id` (its name), `contentType` (`image/png` or `image/jpeg`), an optional `filename`, and `dataBase64`. The
  decoded image has at most 10,000,000 bytes. The image `id` follows the rule of the existing image ids.
- An image can have `alternates`: a list of image ids (feature 153; it extends the install in
  `specs\143-gamepack-install\spec.md`). The rules are the rules of `PUT /api/images/{id}/alternates`: at most 8 ids, no
  duplicate, no own id. Each id names an image of the file, or a stored image that the install keeps. If the member is
  absent, the install does not change the stored list, and a new image has an empty list. If the member is an empty list,
  the install clears the stored list. A rollback or an update capture writes `alternates` only when the stored list is
  not empty. A take-over backup of an image holds `alternates` only when the old list is not empty. A restore of the
  backup puts the old list back.
- An unknown member in an image entry is an error. The dry run names the image and the member, and the install is refused.
- `seedTemplates` is optional. A file without it installs as before. See the section "Seed templates".
- A request has at most 100 MB. A larger request gets a 413 problem response, and nothing changes.

## Routes

### POST /api/gamepacks/install

Query `dryRun=true` checks the file and changes no data. Without `dryRun`, the route installs the gamepack. Query `takeOver=true`
takes over objects of the user with the same type and name (see the section "Take-over").

| Status | Meaning |
|--------|---------|
| 200 | The file is valid and the install worked. For a dry run: the response shows `valid` and the planned counts. A dry run with problems also gives 200 with `valid: false`. |
| 400 | The body is not valid JSON, or a condition or loop config has a missing, unknown, duplicate, or not-string type member. The message gives the JSON path. The type member can be at any position. |
| 413 | The request is larger than 100 MB. |
| 422 | The check found problems. Nothing changed. The body has the list `problems`. |
| 500 | A write failed. All objects stay as before. If the restore also failed, the body has `"result": "partial_restore"`. |

The check finds all problems. It does not stop at the first. A problem has `category` (`structure`, `reference`, or
`conflict`), `objectType`, `name`, `owner` (text), and `message`.

- **Structure**: a missing manifest field, a bad name, two objects with the same name in the file, a bad image type, bad
  base64, an image larger than the limit, or a `requiredApiMajor` that differs from the running API major version.
- **Reference**: a step names a command or an image that is not in the file and not in the installation. A target that the
  install removes does not count. The check covers each command id of a step (`commandId`, `commandReference.commandId`,
  and the `commandId` of a `command` action). A step that has only `commandReference.commandName` is checked by name
  (names match without case).
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

### DELETE /api/gamepacks/{name}

The route removes the record of an installed gamepack. The name matches without case.

- The route removes the registry record and all owner entries of the gamepack. The sequences, commands, and images stay.
  They belong to you after the removal.
- The route deletes the kept previous version. Take-over backup files stay.
- A later install of the same name is a first install. If an object of the removed gamepack still exists, the install
  treats it as your object (conflict, or take-over with `takeOver=true`).
- 204 with no body, 404 `not_found` (unknown or not valid name, or a repeat request), and 500 `failed` (the registry stays as
  before).
- The route takes the same gate as install and rollback.

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
3. For each entry of `sequences`, `commands`, `images`, and `seedTemplates`, add one item to `files`. `type` is `sequence`,
   `command`, `image`, or `seedTemplate`. `name` is the `name` of a sequence, command, or seed template, or the `id` of an image.
   `sha256` is the hash of the canonical entry. A file without `seedTemplates` has no `seedTemplate` item.
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

The area also has the check box "Take over objects of the user with the same name". It is off at load and the page does not keep it. It applies to the Install and Update buttons of the list. The page sends it as `takeOver` in `POST /api/gamepacks/catalog/install`.

- With the box on and "Dry run" off, an Install or Update button works only after a valid dry run of the same version with take-over on. Before that, the button is disabled and the row shows "Run the dry run with take-over first."
- A dry run with the box on shows "Take over N." in the result, also when the dry run is not valid. With the box off, the result has no count, and it lists the conflicts.
- A change of the take-over box clears the result and the saved dry run. A change of the "Dry run" box keeps them. The page never starts a dry run by itself.
- The page has no file install. So the file part of the issue criterion 6 does not apply to this page.

## Managed update

A managed update does all steps of an update in one call. It runs in this order: check the request, find the affected queues, stop them, store a backup, install, book disabled entries for new sequences, and restart the queues that the call stopped.

### Routes

- `POST /api/gamepacks/update` takes a gamepack file (JSON, or multipart with the parts `gamepack` and `signature`, 100 MB at most).
- `POST /api/gamepacks/catalog/update` takes `{ "name", "version", "dryRun", "templates" }` and gets the file from the catalog.
- `POST /api/gamepacks/{name}/rollback?managed=true` uses the same flow. The install step is the rollback. Without `managed=true`, the rollback works as before.
- Query values: `dryRun=true` returns the plan and all problems and changes nothing. `templates` (repeat it for each name) limits the call to the named queue templates. Names match without case.

### What the call does

- A template is affected when one of its entries refers to a sequence of the installed version. A queue is affected when it links to an affected template.
- The call stops only the affected queues that run. Each queue has 60 seconds to stop. The call restarts only the queues that it stopped. A queue that you stopped stays stopped.
- The backup holds the affected templates and the queues that link to them. Its id is a time stamp and the gamepack name. The files are in `<data root>/gamepacks/backups`. The platform keeps the newest 20. If no template is affected, there is no backup and no `backupId`.
- For each new sequence, each affected template gets one entry. The entry is disabled and has no schedule and no parameter values. The platform never enables an entry. It keeps all entries that exist. An entry whose sequence the new version removed stays and is listed in `removedSequenceEntries`.
- If the new version text is exactly the installed version text, the call does nothing and returns `done` with no stopped queue.
- Only one managed call runs at a time on the instance. A second call gets 409 `update_in_progress`.

### The report

The report has `result` (`done`, `planned`, `failed`, or `rejected`), `reason`, `stoppedQueues`, `restartedQueues`, `notRestartedQueues`, `backupId`, `created`, `updated`, `removed`, `newEntries`, `removedSequenceEntries`, `notAffectedTemplates`, `problems`, `signature`, and `restore`. A queue in `notRestartedQueues` does not change `result`.

If a step fails after the first stop, the call restores the previous version, imports the backup, and starts the stopped queues. The report has `result` `failed` (status 500) and `restore` with `happened`, `restored`, the `steps`, and `failedObjects`. A failed first install returns to "no gamepack installed". A failed managed rollback does not call the rollback a second time. It puts back the recorded state.

A dry run gives 200 for file, signature, and validation problems and lists all problems together. A missing template name gives 400 `template_not_found` for a dry run and for a normal call.

Reasons: `invalid_request`, `invalid_file`, `signature_rejected`, `validation_failed`, `template_not_found`, `gamepack_not_found`, `no_previous_version`, `entry_not_listed`, `not_configured`, `catalog_error`, `too_large`, `update_in_progress`, `stop_timeout`, `backup_failed`, `install_failed`, `booking_failed`, `rollback_refused`, `unexpected`.

## Take-over

A conflict with an object of the user stops an install. With the option `takeOver`, the gamepack takes the object over instead.
The gamepack then owns the object, and the stored id does not change. So every link to the object by id or by name stays valid.
A take-over fits a user who made objects by hand and now installs a gamepack that has the same objects.

### The option

- `POST /api/gamepacks/install?takeOver=true` and `POST /api/gamepacks/update?takeOver=true` (query).
- `POST /api/gamepacks/catalog/install` and `POST /api/gamepacks/catalog/update` (body field `takeOver`).
- A missing value means false. The managed rollback has no such option. A rollback never takes an object over.

### Rules

- Only an object of the user can be taken over. The object must have the same type and name as an object in the gamepack. Names match without case.
- A conflict with an object of another gamepack stays a conflict, with the option on or off. An install gives 422. A dry run gives 200 with `valid: false`. The install route never gives 409 for a conflict.
- An object that the gamepack already owns is an update, not a take-over. It is not in the count `takenOver`.
- If two objects of the user have the same name, the platform takes over the first one. The other one is a conflict.
- An object of the user that is not in the gamepack does not change.
- The option does not change which queues a managed call stops or starts.

### The count, the list, and the backup

- `takenOver` is always in the install response. It is 0 when the option is off or when nothing was taken over.
- A dry run with `takeOver=true` and no problem also has the list `takeOvers`. Each entry has `type`, `name`, and `identical`. `identical` is true when the content of the user object equals the content of the gamepack object (the file name of an image does not count). A dry run changes nothing and writes no backup.
- A real install that replaces at least one object writes one backup before it changes the first object. The response has `takeOverBackupId`. The managed report has the fields `takenOver`, `takeOverBackupId`, and `takeOvers` too. `backupId` of the managed report stays the backup of the queue templates.
- The backup is the file `<data root>/gamepacks/takeover-backups/<id>.json`. The id has the form `to-yyyyMMddTHHmmssfff-<8 hex>`. The platform never deletes a backup on its own.
- An install with a take-over is atomic. If a write fails, all objects stay as before, and the backup file is deleted.

### Restore a backup

`POST /api/gamepacks/takeover-backups/{backupId}/restore` gives the objects of the backup back to the user, with their old content. The route takes the same gate as install and rollback. It does not stop or start a queue.

| Status | Meaning |
|--------|---------|
| 200 | All objects are back. The body is `{ "backupId", "result": "restored", "restored": <count> }`. |
| 404 | The id is unknown or has a wrong form. |
| 409 | A check failed. Nothing changed. The body has `reasons`. |
| 500 | A write failed. The state is as before. |

Each reason has `code`, `type`, `name`, and `message`. There are three codes:

- `object_missing`: the object no longer exists.
- `object_changed`: the object differs from the content that the install wrote (a user edited it after the install).
- `owner_changed`: the gamepack no longer owns the object, or the gamepack is not installed.

The backup file stays after a restore. A second restore of the same backup gives 409 with `owner_changed` and changes nothing. If a later rollback of the gamepack deleted a taken-over object, a restore gives `object_missing`.

### Failure in a managed update

If a managed call fails after the install worked, the flow gives the user objects back before it rolls the gamepack back. The restore report has the step `takeover_restore`. The failed report has no `takeOverBackupId`.

## Seed templates

A seed template is a starting queue template that a gamepack gives to a new user. The file member `seedTemplates` is a list of
queue templates in the shape of the queue settings document (see "Back up and restore settings"): each has `name` and `entries`.
Each entry has `sequence` (a sequence name), `enabled`, `scheduleType`, `timerTimeOfDay` or `timerRelativeOffset`, and
`parameterValues`.

Rules:

- Only a **first install** of the gamepack creates seeds. A first install is an install of a gamepack name that has no record in the
  registry. A later install, a managed update, and a rollback ignore the seeds. They do not create, change, or delete a seed
  template, also when the user deleted the template.
- A seed is created only when no queue template has the same name (case is ignored). If a template has the name, the platform does
  not change it and reports the seed as skipped.
- A seed is an ordinary queue template. It has no link to the gamepack and no owner entry. The settings export lists it like a
  template that the user made. The user can change or delete it.
- Each entry `sequence` must name a sequence in the same file, or a sequence on the instance (case is ignored). If not, the check
  gives a `reference` problem (object type `seedTemplate`). The install gives 422 and changes nothing. A dry run gives
  `valid: false`. The reference check runs only on a first install.
- The structure of each seed is checked on every install and dry run: template name rules, unique seed names, non-empty entry
  sequence, schedule type and timer fields, and parameter names. A fault gives a `structure` problem (object type `seedTemplate`).
- The install is atomic. If a write fails, the platform deletes the seed templates that it created in this install. This undo is best
  effort: it logs a failed delete and goes on with the others.

The result of an install, a dry run, a catalog install, and a managed update has two lists. Both are always present, also when
empty. For a dry run they show what the install would do.

- `seedTemplatesCreated`: the names of the created templates.
- `seedTemplatesSkipped`: items with `name` and `reason`. The only reason is `skipped_exists`.

A managed update of an installed gamepack gives two empty lists.

The signature covers the seeds. The signature file lists each seed as an item with `type` `seedTemplate` and the `name` of the
template. `sha256` is the hash of the canonical entry. A file without seeds has the same file list as before, so an old
signature stays valid. A file with seeds needs a signer that hashes them. A signer that does not know `seedTemplates` makes a
signature that fails for such a file.

## Where the data is

`<data root>/gamepack-signature.json` holds the signature policy and the trusted keys.
`<data root>/gamepack-catalog.json` holds the catalog location, the allow insecure flag, and the token.
`<data root>/gamepacks/registry.json` holds the installed gamepacks and the owner records. `<data root>/gamepacks/previous/<name>.json`
holds the kept previous version of each gamepack (the name is in lower case). `<data root>/gamepacks/takeover-backups/<id>.json`
holds the old content of the user objects that one install replaced (the section "Take-over"). The service writes each file to a temporary
file first and then replaces the old file.
