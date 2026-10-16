# Quickstart: Managed gamepack update

Use this guide to check the feature by hand after the build. The service runs on port 8080. Use the API token header if the instance needs it.

## Prepare

1. Install version 1 of a test gamepack. Use `POST /api/gamepacks/install` with a file that has two sequences.
2. Make two queue templates. Put an entry for a gamepack sequence in each template. Link one template to a queue, and start that queue.
3. Make a version 2 file of the same gamepack with one new sequence.

## Dry run

```powershell
Invoke-RestMethod -Method Post -Uri "http://localhost:8080/api/gamepacks/update?dryRun=true" -ContentType "application/json" -InFile "C:\temp\pack-v2.json"
```

Check: `result` is `planned`. `stoppedQueues` lists the running queue. `newEntries` lists the new sequence. The queue still runs. `GET /api/gamepacks` still shows version 1. No file exists in `<dataRoot>\gamepacks\backups`.

## Update

Send the same request without `dryRun`.

Check: `result` is `done`. `backupId` has a value. `restartedQueues` equals `stoppedQueues`. Each affected template has one new entry with `enabled` false. Old entries keep their schedule and parameter values.

## Update from the catalog

```powershell
$body = '{"name":"pns","version":"2.0.0","dryRun":false,"templates":["pns-daily"]}'
Invoke-RestMethod -Method Post -Uri "http://localhost:8080/api/gamepacks/catalog/update" -ContentType "application/json" -Body $body
```

Check: only `pns-daily` changes. Other templates stay the same.

## Failure and restore

1. Make a version 3 file that fails the install, for example with a sequence that refers to a missing command.
2. Send it as an update.

Check: the response has `result` `rejected` (check problem) and no queue stopped. To force a failure after the stop, run the integration test `GamepackManagedUpdateRestoreTests`, which injects a write fault through `IGamepackWriteGate`.

## Conflict and same version

1. Start a managed call and, while it runs, send a second managed call for any gamepack. Check: the second call returns 409 with `update_in_progress`.
2. Send an update with the version that is already installed. Check: `stoppedQueues` is empty, `backupId` is absent, and no template changes.
3. Send an update (also with `dryRun=true`) with a template name that does not exist. Check: 400 with `template_not_found`.

## Managed rollback

```powershell
Invoke-RestMethod -Method Post -Uri "http://localhost:8080/api/gamepacks/pns/rollback?managed=true"
```

Check: the report has the same fields as an update report. The version is 1 again. The queues run again.

## Run the tests

Use the project test commands for `tests\unit`, `tests\contract`, and `tests\integration`. Run the filter `FullyQualifiedName~GamepackManaged` for the new tests.
