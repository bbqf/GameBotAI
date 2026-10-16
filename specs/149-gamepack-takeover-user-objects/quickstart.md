# Quickstart: Take over same-named user objects

Use this guide to check the feature by hand. The service runs on port 8080. Replace `$base` with the service address.

## 1. Prepare user objects

1. Create a sequence named `Daily` and a command named `Tap Home` by hand.
2. Create a queue template that has an entry for the sequence `Daily`.
3. Prepare a gamepack file `demo.json` that has a sequence `Daily` and a command `Tap Home` with different content.

## 2. Install without the option

```powershell
Invoke-RestMethod -Method Post -Uri "$base/api/gamepacks/install" -ContentType "application/json" -InFile "demo.json"
```

Expected: status 422. The problems say that the user owns the objects. Nothing changed.

## 3. Preview the take-over

```powershell
Invoke-RestMethod -Method Post -Uri "$base/api/gamepacks/install?dryRun=true&takeOver=true" -ContentType "application/json" -InFile "demo.json"
```

Expected: `valid` is true. `takeOvers` lists `Daily` and `Tap Home`, each with `identical` true or false. No object, owner, or backup file changed.

## 4. Install with the option

```powershell
Invoke-RestMethod -Method Post -Uri "$base/api/gamepacks/install?takeOver=true" -ContentType "application/json" -InFile "demo.json"
```

Expected: status 200. `takenOver` is 2. `takeOverBackupId` has a value. The objects have the content of the gamepack. The list of gamepacks shows the owner. The queue template still references `Daily`.

## 5. Restore

```powershell
Invoke-RestMethod -Method Post -Uri "$base/api/gamepacks/takeover-backups/<takeOverBackupId>/restore"
```

Expected: status 200 and `restored` is 2. The objects have their old content. The owner is `user` again.

Try the same call a second time. Expected: status 409 with the reason `owner_changed`.

Try an unknown id. Expected: status 404.

## 6. Managed update

```powershell
Invoke-RestMethod -Method Post -Uri "$base/api/gamepacks/update?takeOver=true" -ContentType "application/json" -InFile "demo.json"
```

Expected: the report has `takenOver` and `takeOverBackupId`. No queue changes its run state because of the option.

Catalog routes: send `"takeOver": true` in the body of `POST /api/gamepacks/catalog/install` or `POST /api/gamepacks/catalog/update`. Expected: the same result fields (`takenOver` and `takeOverBackupId`). The catalog install result is enough to call the restore route.

Performance check: run the performance test. Expected: the take-over install over 144 objects takes at most 2 times the normal install of the same gamepack on the same machine.

## 7. Failure check

Run the unit test `GamepackApplierTakeOverBackupTests` (and `GamepackTakeOverRestoreTests` in the integration project). It injects a fault at each write step. Expected: after each fault, no object differs from the state before the call, and no backup file stays.

## Test commands

```powershell
dotnet test "C:\src\GameBot\tests\unit\GameBot.UnitTests.csproj" --filter "FullyQualifiedName~Gamepack"
```

Use the contract and integration test projects in the same way.
