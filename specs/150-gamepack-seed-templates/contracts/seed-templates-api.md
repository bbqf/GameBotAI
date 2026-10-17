# Contract: Seed templates in the gamepack API

No new route. The change is in the gamepack file and in the results of existing routes.

## 1. Gamepack file

New optional member `seedTemplates` (array). Example:

```json
{
  "manifest": { "name": "pns", "version": "1.2.0" },
  "sequences": [ { "name": "Daily Gift" } ],
  "seedTemplates": [
    {
      "name": "Daily start",
      "entries": [
        {
          "sequence": "Daily Gift",
          "enabled": true,
          "scheduleType": "Timer",
          "timerTimeOfDay": "04:00:00",
          "parameterValues": []
        }
      ]
    }
  ]
}
```

- Member names match without case, as for the other file members.
- An entry uses the sequence name, not the id.
- `scheduleType`: `OncePerRun`, `EveryStep`, `Timer`, `AtQueueStart`, `BeforeEachRun`.
- A `Timer` entry sets exactly one of `timerTimeOfDay` and `timerRelativeOffset`.

## 2. Install and dry run: `POST /api/gamepacks` (and `?dryRun=true`)

Two new members in the 200 response. Both are always present.

```json
{
  "dryRun": false,
  "valid": true,
  "created": 1, "updated": 0, "removed": 0, "takenOver": 0,
  "seedTemplatesCreated": [ "Daily start" ],
  "seedTemplatesSkipped": [ { "name": "Weekly", "reason": "skipped_exists" } ]
}
```

| Member | Type | Meaning |
|--------|------|---------|
| `seedTemplatesCreated` | array of string | Names of the templates that the install created. For a dry run: names that it would create. |
| `seedTemplatesSkipped` | array of object | Seeds that the install did not create. Each has `name` and `reason`. For a dry run: seeds that it would skip. |

Rules:

- Not a first install: both arrays are empty.
- A file without `seedTemplates`: both arrays are empty.
- `reason` is `skipped_exists` when a template with the same name exists. Names match without case. The existing template does not change. The platform checks the name again just before it writes the template (FR-016), so a name used after the plan also gives `skipped_exists`.
- On a later install or update, the platform ignores the seeds. It checks their structure but not their sequence references (FR-015).
- A dry run changes no stored item. Its lists equal the lists of the real install when no concurrent change happens between the dry run and the install (SC-003).
- If a first install fails, no seed template stays. The platform removes the templates it created, by id, on a best effort basis (FR-014).

### Problems

Status codes do not change (422 for an install, 200 with `valid: false` for a dry run).

| Category | `objectType` | Case |
|----------|--------------|------|
| `structure` | `seedTemplate` | Name rule, duplicate name, schedule, timer, or parameter name rule is broken. Also a wrong JSON shape. |
| `reference` | `seedTemplate` | First install only. An entry names a sequence that is not in the file and not on the instance. |

## 3. Other routes

| Route | Change |
|-------|--------|
| `POST /api/gamepacks/{name}/rollback` | None. Seeds are not touched. A rollback is always "not first install" for seeds. The response has no new member. |
| `POST /api/gamepacks/catalog/...` install | The result has `seedTemplatesCreated` and `seedTemplatesSkipped`, as the file route. |
| Managed update (`.../update`) | The report has the same two members. They are empty for an installed gamepack. |
| `GET /api/queue-templates` and the settings export | None. A created seed is an ordinary template. |

## 4. Signature

The signed file list can include entries with type `seedTemplate` and the template name. The hash is the canonical SHA-256 of the raw JSON of the seed entry, as for a sequence. A signed file without `seedTemplates` has no seed entry in its file list, keeps its hash, and gives the same signature result as before this feature (FR-017).

## 5. OpenAPI

`GamepackSchemaFilter` describes: the file member `seedTemplates`, `seedTemplatesCreated`, `seedTemplatesSkipped`, and the type `GamepackSeedSkipDto` (`name`, `reason`). `ManagedUpdateSchemaFilter` and the catalog DTO descriptions add the same text. A contract test checks that each new member has a description.
