# Research: Split the Largest CI Test Shard

## R1: How to find the split points

- **Decision**: Run the contract tests once on a developer machine with the `trx` logger and a
  results folder outside the repository. Sum the class durations for each namespace prefix. Use the
  sums to choose the split.
- **Rationale**: `docs/ci-time.md` already gives this method ("How to measure", step 4). It gives
  class times, so the split follows data and not a guess.
- **Alternatives considered**: Split by file count (poor match: the count does not show time).
  Take times from CI logs (CI times include runner noise, and the rule uses local time).

## R2: How to split the complement shard

- **Decision**: Name the heavy prefixes of `contract-4` in new shards (`contract-4`, `contract-5`,
  and `contract-6` if needed). The last shard stays the complement and keeps the small remainder.
- **Rationale**: The manifest rule says the last shard is the complement, so a new class is never
  lost. `TestShardTests` already proves one shard for each class.
- **Alternatives considered**: Index-based round-robin split of classes (breaks the "one new class
  falls into the complement" rule and moves tests with no reason). A new manifest format (no need).

## R3: How many shards

- **Decision**: Use the smallest number that gives a longest contract shard of at most 1.2 times
  the average. Expected: 3 shards from the old `contract-4` (5 to 6 contract shards in total).
- **Rationale**: Each shard costs about 25 s of fixed time. More shards than needed make CI slower.
  Example: times 1, 1, 1, 4 give average 1.75 and limit 2.1, so the 4 fails. Times 1, 1, 1, 1.33,
  1.33, 1.33 give average 1.17 and limit 1.4, so all pass.
- **Alternatives considered**: Split in 2 (too large if the old shard is 4 times the others).

## R4: How the check script measures time

- **Decision**: `measure-shards.ps1` runs each shard with `dotnet test` and the filter from
  `get-test-shard-filter.ps1`, with `--no-build` after one build. It uses wall time of the test run.
  It repeats a shard that is above the limit once. It accepts `-TimesFile` (JSON map of shard to
  seconds) to skip the run.
- **Rationale**: Wall time of the shard is what the rule means by "local run time". `-TimesFile`
  makes the calculation testable and fast.
- **Alternatives considered**: Sum of `trx` class times (ignores start-up time and parallel runs).
  Run the check in CI (rejected by clarification: CI times differ from local times).

## R5: Where to write the rule

- **Decision**: Put the full rule in `docs/ci-time.md`. Add one bullet to the constitution
  "Development Workflow and Review Process" that states the 20% limit and points to the guide.
  Add `.github/pull_request_template.md` with a check box for the shard check.
- **Rationale**: `docs/ci-time.md` is the existing guide for CI time. The constitution makes the
  rule binding for all contributors. No pull request template exists, so the spec requirement
  FR-009b needs a new file.
- **Alternatives considered**: Rule in the constitution only (too long for a constitution). Rule in
  `docs` only (not binding).

## R6: Impact on required checks and workflow

- **Decision**: No change to `dotnet.yml`. The workflow reads shard names with `-List`. Master has
  no required status check on shard names today (`docs/ci-time.md`, `ci-gate` is the merge signal).
- **Rationale**: Changed shard names do not break the matrix or `ci-gate`.
- **Alternatives considered**: None needed.
