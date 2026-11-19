# Contract: `scripts/ci/find-disabled-tests.ps1`

The guard finds tests that the normal test run does not run. CI runs it in the `changes` job on each push.

## Call

```text
pwsh ./scripts/ci/find-disabled-tests.ps1 [-Root <path>]
```

| Parameter | Type | Default | Meaning |
|-----------|------|---------|---------|
| `-Root` | string | The repository root (two folders above the script) | The folder to search. Unit tests give a temporary folder. |

The script reads only these files below `-Root`:

- `tests/**/*.cs`
- `src/web-ui/src/**/*.ts`, `src/web-ui/src/**/*.tsx`
- `src/web-ui/tests/**/*.ts`

It ignores `bin/`, `obj/`, and `node_modules/` folders.

## Findings

| Kind | Language | Pattern | Example |
|------|----------|---------|---------|
| `skip` | C# | A `[Fact]`, `[Theory]`, or other test attribute with a `Skip =` argument | `[Fact(Skip = "slow")]` |
| `skip` | TS | `it.skip(`, `test.skip(`, `describe.skip(`, `test.describe.skip(`, `xit(`, `xtest(`, `xdescribe(` with a string literal as the first argument | `it.skip('opens', ...)` |
| `skip` | TS | `test.fixme(` with a string literal as the first argument | `test.fixme('opens', ...)` |
| `focus` | TS | `it.only(`, `test.only(`, `describe.only(`, `test.describe.only(`, `fit(`, `fdescribe(` | `test.only('opens', ...)` |
| `commented` | C# | A comment line that holds only a test attribute: `// [Fact]`, `// [Theory]`, `// [Fact(...)]`, `// [Theory(...)]` | `// [Fact]` |
| `commented` | TS | A comment line that starts a test: `// it(`, `// test(`, `// describe(` (with an optional `.skip` or `.only`) | `// it('opens', () => {` |

Each pattern starts at the start of the line, after spaces or tabs only. Thus a pattern in a string literal in the middle of a line is not a finding. The guard tests (`tests/unit/Ci/DisabledTestGuardTests.cs`) MUST put their sample lines in strings that do not start a source line, so that the guard does not report its own tests.

Block comments (`/* ... */`) are out of scope. The guard reads one line at a time. A block comment that holds a test is rare, and a line scan cannot find its start and end in a reliable way.

These are permitted, and the guard does not report them:

- A conditional skip in a test body: `test.skip(<condition>, '<reason>')`. The first argument is not a string literal, and a second argument is present.
- A text that only names a skip in a string or in a longer comment sentence.

## Output

One line for each finding, in this format, sorted by path and line:

```text
<relative path>:<line>: <kind>: <the trimmed source line>
```

Paths use `/` as the separator. After the findings, the script writes one summary line.

- With findings: `find-disabled-tests: <N> disabled test(s) found. Turn each test on, or delete it. A conditional skip needs a condition and a reason.`
- With no finding: `find-disabled-tests: no disabled test found.`

## Exit codes

| Code | Meaning |
|------|---------|
| 0 | No finding. |
| 1 | One or more findings. |
| 2 | The script could not run (for example, `-Root` does not exist). |
