# Data Model: Turn On and Repair the Disabled Tests

This feature stores no data. The two records below exist only in the PR text and in the guard output.

## Disabled test record (PR text)

The PR description has one row for each disabled test (FR-010).

| Field | Meaning | Values |
|-------|---------|--------|
| ID | The ID from the spec table | `D-1` to `D-4`, or `D-5` and up for a new item from the full search |
| Side | The part of the product | back end, front end |
| Location | File and test name | Relative path and method or test title |
| Disablement | How the test was off, or how it failed | skip marker, comment, not in CI, failed (not disabled) |
| Root cause | Why the test failed, or why it was off | Free text |
| Repair | What changed | product repair, test repair, test deleted, no change needed |

Rules:

- Each row has a root cause and a repair.
- "Test deleted" needs the reason and the tests that now cover the behavior.

## Guard finding (script output)

| Field | Meaning | Values |
|-------|---------|--------|
| Path | Relative path with `/` | Text |
| Line | Line number, starts at 1 | Integer, 1 or more |
| Kind | The type of finding | `skip`, `focus`, `commented` |
| Source | The trimmed source line | Text |

Rules:

- The guard sorts the findings by path and then by line.
- One source line gives one finding at most.
