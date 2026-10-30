# Data Model: Split the Largest CI Test Shard

## Shard list (`scripts/ci/test-shards.json`)

One object for each test project (`unit`, `contract`, `integration`).

| Field | Meaning |
|-------|---------|
| `shards[]` | Ordered list of shards. The last shard of a project is the complement |
| `shards[].name` | Unique shard name, for example `contract-4` |
| `shards[].include[]` | Class-name prefixes that the shard runs |
| `shards[].complement` | `true` for the shard that runs every test no other shard names |
| `shards[].collectsCoverage` | Optional. At most one shard in a project has it |
| `shards[].performanceGate` | Optional. At most one shard in a project has it |
| `pinned[]` | Classes that must stay in one named shard |

Rules:
- Every test class is in exactly one shard.
- A new test class falls into the complement shard.
- A pinned class stays in its shard.
- No change to the format. Only the contract shard entries change.

## Shard times (input to `measure-shards.ps1 -TimesFile`)

A JSON object that maps a shard name to its local run time in seconds.

```json
{ "contract-1": 40.2, "contract-2": 38.9, "contract-3": 41.5 }
```

## Balance result (output of `measure-shards.ps1`)

| Field | Meaning |
|-------|---------|
| `Shard` | Shard name |
| `Seconds` | Local run time |
| `Average` | Average of all shard times of the project |
| `Limit` | Average times 1.2 |
| `Status` | `ok` when `Seconds` is at most `Limit`, `split` when it is above |

State: none. The script keeps no data between runs.
