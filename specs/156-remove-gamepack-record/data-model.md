# Data Model: Remove an installed gamepack record

No new stored type. The feature removes data from the existing registry.

## Stored data (existing)

| Item | Where | Change by a removal |
|------|-------|---------------------|
| `InstalledGamepack` (name, version, changelog, requiredApiMajor, installedAtUtc, objects, previous) | `GamepackRegistry.Gamepacks[key]`, key = lower-case name | Entry removed |
| `OwnerEntry` (gamepack, version, id) | `GamepackRegistry.Owners[type:name]` | Every entry with `Gamepack` equal to the name (case ignored) is removed |
| Kept previous version text | `<previous folder>\<key>.json` | File deleted after the registry write |
| Take-over backup | backup folder | No change |
| Sequence, command, image | their stores | No change |

## New code types (in `GamepackService.cs`, Domain)

```text
enum GamepackRemoveStatus { Removed, NotFound, Failed }

class GamepackRemoveOutcome {
  GamepackRemoveStatus Status
  string? Name            // the stored name of the record (for NotFound: the request name)
  string? Version         // the removed version, for Removed
  int OwnersRemoved       // count of owner entries removed
  bool HadPreviousVersion
  string? ErrorMessage    // for Failed
}

Task<GamepackRemoveOutcome> GamepackService.RemoveAsync(string name, CancellationToken ct = default)
```

## State transitions

```text
Installed(name, v) --RemoveAsync--> NotInstalled(name)
NotInstalled(name) --RemoveAsync--> NotInstalled(name)   (outcome NotFound, no change)
NotInstalled(name) --Install--> Installed(name, v')      (first install: no previous version)
```

## Validation rules

- Name: `GamepackNameRules.IsValid`; an invalid name gives `NotFound`.
- Match: `GamepackNameRules.Key(name)`.
- The registry change is one `Update` call. A throw from `Update` gives `Failed` and the registry stays as before.
