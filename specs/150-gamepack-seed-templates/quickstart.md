# Quickstart: Seed queue templates in a gamepack

Use these steps to check the feature by hand. The service runs on port 8080.

## 1. Make a gamepack file with a seed

Save this file as `seed-demo.json`:

```json
{
  "manifest": { "name": "seed-demo", "version": "1.0.0" },
  "sequences": [ { "name": "Seed Demo Seq", "steps": [] } ],
  "seedTemplates": [
    { "name": "Seed Demo Template",
      "entries": [ { "sequence": "Seed Demo Seq", "scheduleType": "OncePerRun" } ] }
  ]
}
```

## 2. Dry run

```powershell
Invoke-RestMethod -Method Post -Uri "http://localhost:8080/api/gamepacks?dryRun=true" -ContentType "application/json" -InFile "C:\path\to\seed-demo.json"
```

Expect `seedTemplatesCreated` = `Seed Demo Template`. Expect no new template in `GET /api/queue-templates`.

## 3. First install

```powershell
Invoke-RestMethod -Method Post -Uri "http://localhost:8080/api/gamepacks" -ContentType "application/json" -InFile "C:\path\to\seed-demo.json"
```

Expect `seedTemplatesCreated` = `Seed Demo Template`. The template now exists. It has one entry.

## 4. Change the template, then install version 1.1.0

Edit the template (for example, rename an entry parameter). Change `version` to `1.1.0` and add a second seed to the file. Install again.

Expect both seed arrays empty. The template keeps your change. The second seed is not created.

## 5. Delete the template, then install again

Delete `Seed Demo Template` and install the file again. Expect both seed arrays empty. The template does not come back.

## 6. Name conflict on a first install

Delete the gamepack data (use a new gamepack name in the file). Make a template named `seed demo template` by hand. Install the file. Expect `seedTemplatesSkipped` = one item with `reason` = `skipped_exists`. The template is not changed.

## 7. Dangling sequence

Change the seed entry to name `No Such Sequence`. Run a dry run. Expect a `reference` problem and `valid` = `false`.

## 8. Later install with a bad seed reference

Install version 1.2.0 of the same gamepack. Make a seed entry that names `No Such Sequence`. Expect the install to work: the platform ignores seeds on a later install and does not check their references. A seed with a bad schedule type still gives a `structure` problem.

## 9. Manual check of SC-005 (no separate task)

Use a new gamepack name and install the file from step 1 on an instance with no such template. Open the queue template list in the web UI. Without a written guide, start the tasks of the game from `Seed Demo Template`. Expect that the order, the schedule, and the parameters of the tasks are clear from the template alone.

## 10. Run the tests

```powershell
dotnet test "C:\src\GameBot\GameBot.sln" --filter "FullyQualifiedName~Gamepack"
```
