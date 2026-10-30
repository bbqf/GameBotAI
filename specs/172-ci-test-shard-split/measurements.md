# Measurements: Split the Largest CI Test Shard

## CI baseline before the change (T005)

Source: `scripts/ci/measure-ci.ps1 -Branch master -Limit 5 -Conclusion success`, 2026-10-30.

| Item | Median (s) | Worst (s) |
|------|-----------|-----------|
| Critical path | 487 | 568 |
| test-contract (contract-1) | 107 | 122 |
| test-contract (contract-2) | 132 | 148 |
| test-contract (contract-3) | 135 | 136 |
| test-contract (contract-4) | 314 | 351 |

The longest job in 4 of 5 runs is `test-contract (contract-4)`.

## CI after the change (T024)

Source: `measure-ci.ps1 -Branch 172-ci-test-shard-split`, run 41133408185 (success). One run only, so the noise of up to 10% applies.

| Item | Before, median (s) | After (s) |
|------|--------------------|-----------|
| Critical path | 487 | 284 |
| Longest contract job | 314 (`contract-4`) | 142 (`contract-4`) |
| Longest job overall | `contract-4` | `analyze` (221 s) |

Contract jobs after: 110, 125, 118, 142, 132, 137 s. The longest test job is now 142 s. The critical path is 42% lower than the baseline. CI time is lower (SC-002, FR-011). The contract jobs no longer set the critical path.

## Local class times (T003, T004)

Source: one full local run of the contract project with the `trx` logger (1177 results, 176 classes, 670 s in total).
Times are the sum of the test durations in each shard.

| Shard | Before (s) | Main content |
|-------|-----------|--------------|
| contract-1 | 75.4 | unchanged |
| contract-2 | 123.2 | unchanged |
| contract-3 | 122.0 | unchanged |
| contract-4 (old complement) | 349.5 | Gamepacks 141, Sequences 108, Commands 37, root classes 26, Quiz 18, other 20 |

The old `contract-4` is 3.1 times the average of the other three. The 4 shards had an average of 167.5 s, so the limit was 201 s.

## Chosen split (T006)

CI time model from the baseline: job time is about 50 s plus 0.755 s for each second of test time.
5 shards give a longest shard of about 134 s of test time (about 151 s job). 6 shards give about 123 s (about 143 s job).
A 7th shard saves less than 10 s and adds one more fixed cost, so 6 shards is the choice.

| Shard | Content | Expected (s) |
|-------|---------|--------------|
| contract-4 | Gamepacks catalog, signature, remove, managed; Commands | 115.6 |
| contract-5 | remaining Sequences classes; ExecutionLogs | 115.5 |
| contract-6 (complement) | other Gamepacks, root classes, Quiz, QueueSettings, StepThrough, Installer | 118.4 |

Average of the 6 shards 111.7 s, limit 134.0 s, longest shard 123.2 s (`contract-2`, unchanged). Rule met.

## Local check of the new contract shards (T017)

`scripts/ci/measure-shards.ps1 -Project contract`, exit status 0.

| Shard | Seconds | Status |
|-------|---------|--------|
| contract-1 | 60.4 | ok |
| contract-2 | 81.6 | ok |
| contract-3 | 81.4 | ok |
| contract-4 | 79.2 | ok |
| contract-5 | 85.6 | ok |
| contract-6 | 89.4 | ok |

Average 79.6 s, limit 95.5 s, longest shard 89.4 s (1.12 times the average). SC-001 is met.
The times are lower than the `trx` sums because the shards run with a warm build and no logger.

## Findings for unit and integration (T018)

The rule applies to all projects (spec clarification). Both projects have shards above the limit today.
This change does not touch them (FR-003a). Each finding needs its own change.

| Project | Shard | Seconds | Limit (s) | Status |
|---------|-------|---------|-----------|--------|
| unit | unit-1 | 47.8 | 67.6 | ok |
| unit | unit-2 | 47.0 | 67.6 | ok |
| unit | unit-3 | 74.3 | 67.6 | split |
| integration | integration-1 | 88.1 | 86.7 | split |
| integration | integration-2 | 102.6 | 86.7 | split |
| integration | integration-3 | 63.2 | 86.7 | ok |
| integration | integration-pinned | 13.2 | 86.7 | ok |
| integration | integration-rest | 94.3 | 86.7 | split |

Note: `integration-pinned` is small because the pinned classes have a duty (coverage and the performance gate). A split of the other integration shards must keep that duty in place.

## Test counts (T010)

`dotnet test --list-tests` for the whole contract project: 1119 tests.
Sum of the 6 new shard filters: 108 + 146 + 198 + 179 + 207 + 281 = 1119 tests. The counts are equal.
