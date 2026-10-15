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

## Where the data is

`<data root>/gamepacks/registry.json` holds the installed gamepacks and the owner records. `<data root>/gamepacks/previous/<name>.json`
holds the kept previous version of each gamepack (the name is in lower case). The service writes each file to a temporary
file first and then replaces the old file.
