# Quickstart: Force Install of the Same Gamepack Version (178)

Use these steps to check the feature by hand. The base address is the local service.

## 1. Install a pack

```text
POST /api/gamepacks/install
Body: the gamepack file (name demo-pack, version 1.0.0)
```

The response has `created` greater than 0.

## 2. Change an object by hand

Edit one sequence of the pack with `PUT /api/sequences/{id}`.

## 3. Check what a forced install overwrites

```text
POST /api/gamepacks/install?force=true&dryRun=true
Body: the same file
```

Expect: `reinstalled` is true. `overwritten` lists the edited sequence. No object changes.

## 4. Run the forced install

```text
POST /api/gamepacks/install?force=true
Body: the same file
```

Expect: `reinstalled` is true. `overwritten` lists the edited sequence. Read the sequence: it has the content of the file.

## 5. Undo the forced install

```text
POST /api/gamepacks/demo-pack/rollback
```

Expect: the version in the answer is 1.0.0. The sequence has the hand edit again. `GET /api/gamepacks` shows `hasReinstallPoint` as false.

## 6. Install again without force

```text
POST /api/gamepacks/install
Body: the same file
```

Expect: the answer has no `reinstalled` member and no `overwritten` member.

## 7. Catalog routes

```text
POST /api/gamepacks/catalog/install   { "name": "demo-pack", "version": "1.0.0", "force": true }
POST /api/gamepacks/catalog/update    { "name": "demo-pack", "version": "1.0.0", "force": true }
```

The managed update (`managed=true`) is a mode of the update routes. It also accepts `force`.

Expect: the objects are written again from the catalog file. Without `force`, the update route does nothing for an equal version.

## 8. Check the list member

```text
GET /api/gamepacks
```

Expect: after a forced install, the item of the pack has `hasReinstallPoint` as true (FR-012).

## SC-007 result

The timed test is `GamepackForceReinstallPerfTests` in `C:\src\GameBot\tests\integration\Gamepacks\`. It uses a pack with 200 objects. The limit is a ratio of 1.5 or less.

The test does one warm-up run. Then it does at least 5 runs each of normal and forced install. It compares the medians. The test has the repo trait for performance tests (`Category` = `Performance`). It is a local measure. It runs in CI only if it is stable.

Fill this table after the test runs. Add the same note to the PR text.

| Measure | Value |
|---|---|
| Normal install, median | not measured yet |
| Forced install, median | not measured yet |
| Ratio (forced / normal) | not measured yet |

## Automated checks

Run the gamepack tests:

```text
dotnet test C:\src\GameBot\GameBot.sln --filter "FullyQualifiedName~Gamepack"
```
