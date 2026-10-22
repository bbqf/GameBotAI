# Contract: Install answer with failure explanation

Routes: `POST /api/gamepacks/install` and `POST /api/gamepacks/catalog/install`. The request, the query options, and the
status codes do not change. The answer body gets additions only.

## Answer body (shown for a dry run that is not valid, status 200)

```json
{
  "dryRun": true,
  "valid": false,
  "summary": {
    "headline": "20 conflicts.",
    "entries": [
      {
        "category": "conflict",
        "count": 20,
        "label": "conflicts: objects owned by the user",
        "remedy": "Install with take-over to replace them, or rename or delete them.",
        "resolvedByTakeOver": true
      }
    ]
  },
  "wouldInstallWithTakeOver": true,
  "problems": [
    {
      "category": "conflict",
      "objectType": "sequence",
      "name": "Daily run",
      "owner": "user",
      "message": "A sequence with the name 'Daily run' exists and the user owns it.",
      "takeOverResolves": true,
      "remedy": "Install with take-over to replace it, or rename or delete it."
    }
  ],
  "gamepack": { "name": "PNS", "version": "1.1.0" },
  "created": 0, "updated": 0, "removed": 0, "takenOver": 0,
  "seedTemplatesCreated": [], "seedTemplatesSkipped": []
}
```

## Rules

1. `summary` is in every answer that has a validation result (status 200, 400, 422). It comes before `problems` in the JSON text.
2. `summary.entries` has one entry for each category that has a problem. The order is structure, signature, reference, conflict.
   An answer with no problem has `headline` `No problems.` and an empty `entries`.
3. `count` is at least 1. `label` uses the singular form when `count` is 1. `resolvedByTakeOver` is a bool. It is true only if
   every problem in the entry has `takeOverResolves` true.
4. Each problem has `takeOverResolves` and `remedy`. `takeOverResolves` is true only for a conflict with owner `user` and no `code`,
   when the request has take-over off. With take-over on, each remaining problem gives false. A second user object with the
   same name is a conflict that take-over resolves.
5. `wouldInstallWithTakeOver` is always present (see the state table in data-model.md). A valid check gives true. An invalid check
   with take-over on gives false. An invalid check with take-over off gives the result of a second check with take-over on.
6. A dry run (200) and a real install (422) of the same file give the same `summary`, `problems`, and `wouldInstallWithTakeOver`.
7. The answers of write failures (500) and of 413 do not change.
8. The rollback refused answer (422) and the managed update report get `takeOverResolves` and `remedy` in each problem.
   They get no `summary`.

## Remedy text (STE)

| Case | Remedy |
|---|---|
| structure | Correct the gamepack file. Take-over cannot resolve this. |
| signature | Sign the file with a trusted key, or change the signature policy. Take-over cannot resolve this. |
| reference | Add the missing object to the file, or install it on the system first. Take-over cannot resolve this. |
| conflict, owner user | Install with take-over to replace it, or rename or delete it. |
| conflict, owner another gamepack | Rename the object in the file, or remove the other gamepack. Take-over cannot resolve this. |
| conflict, `update_in_progress` | Try again when the other call ends. Take-over cannot resolve this. |

The entry remedies use the plural form of the same sentences. The final wording is in the code and the tests.

## OpenAPI

`GamepackProblemDto`, `GamepackInstallResponse`, and the two new summary schemas get descriptions in STE
(`GamepackSchemaFilter` and the route `WithDescription` text). `GamepackOpenApiTests` checks them.
