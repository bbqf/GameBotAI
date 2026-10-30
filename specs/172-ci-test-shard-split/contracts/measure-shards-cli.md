# Contract: `scripts/ci/measure-shards.ps1`

## Command

```powershell
& "C:\src\GameBot\scripts\ci\measure-shards.ps1" -Project contract
& "C:\src\GameBot\scripts\ci\measure-shards.ps1" -Project contract -TimesFile "<path>\times.json"
```

## Parameters

| Parameter | Required | Meaning |
|-----------|----------|---------|
| `-Project` | Yes | `unit`, `contract`, or `integration` |
| `-TimesFile` | No | JSON map of shard name to seconds. When given, the script runs no test |
| `-Limit` | No | Allowed excess over the average. Default `0.2` (20%) |
| `-Manifest` | No | Path of the shard list. Default `scripts/ci/test-shards.json` |

## Output

A table with one row for each shard: `Shard`, `Seconds`, `Status`. Then two lines: the average and
the limit. Then, for each shard above the limit, one line: `Split <name>: <seconds> s is above <limit> s.`

## Exit status

| Code | Meaning |
|------|---------|
| 0 | All shards are within the limit, or the project has one shard |
| 1 | At least one shard is above the limit |
| 2 | Wrong input (unknown project, bad times file, a shard with no time) |

## Behavior

- A shard above the limit runs a second time. The lower time is used.
- A project with one shard has no average. The script reports this and exits with 0.
- The script never changes `test-shards.json`.
