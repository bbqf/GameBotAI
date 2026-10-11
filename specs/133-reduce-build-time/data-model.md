# Data Model: Measurement Records

This feature has no product data. These records live in `build-baseline.md`.

## Build baseline

| Field | Meaning |
|-------|---------|
| pc | CPU model, cores, RAM, disk type |
| sdk | Output of `dotnet --version` |
| state | "clean": no `bin`/`obj`, packages restored |
| runs | 3 wall-clock times in seconds; worst and median |

## Project timing

| Field | Meaning |
|-------|---------|
| project | Project name |
| compileSeconds | Time of `CoreCompile` |
| totalSeconds | Time of the whole project build |
| onCriticalPath | yes or no |
| buildCount | How many times the project built in one solution build (must be 1) |

## Analyzer cost

| Field | Meaning |
|-------|---------|
| project | Project name |
| analyzer | Analyzer name or rule group |
| seconds | CPU time from the `ReportAnalyzer` table |

## Cause

| Field | Meaning |
|-------|---------|
| id | Short name |
| kind | redundant step, code structure, package, graph, config |
| seconds | Measured cost |
| shareOfTotal | Percent of the clean build |
| action | Change made, or "none" with the reason |
| afterSeconds | Measured cost after the change |

## Result

Baseline and final values side by side: clean worst, clean median, fall in percent, incremental time, test result.
