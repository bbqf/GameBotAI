# Perf note: number pattern in name resolution (feature 168)

Principle IV of the constitution asks for a perf note for a hot path. The pattern step is in
`ParameterScope.TryResolve`, which runs once for each placeholder of each dispatched step.

## Limit (SC-008)

One name resolution with a number pattern takes less than 1 µs.

The first draft of SC-008 said "no more than 2% extra". No string operation can meet that: 2% of 0.11 µs is
2 ns, and one `Replace` call takes more. So the limit is now absolute.

## Method

`ParameterScopeResolvePerfTests.PatternAddsOnlyASmallCostToResolve` (Category `Benchmark`). It calls
`TryResolve("n")` 2,000,000 times on a two-layer scope, after a warm-up of 20,000 calls. One scope has a
declaration without a pattern. The other has the pattern `emulator-{value}`. The test checks the limit and
returns at once when the environment variable `CI` is `true`. Run it by hand:

```powershell
dotnet test C:\src\GameBot\tests\unit\GameBot.UnitTests.csproj --filter "Category=Benchmark" --logger "console;verbosity=detailed"
```

## Result (2026-10-29, Debug build, one developer machine)

| Case | Time for 2,000,000 calls | Time for one call |
|------|--------------------------|-------------------|
| No pattern | 218 ms | about 0.11 µs |
| Pattern `emulator-{value}` | 421 ms | about 0.21 µs |

The pattern case meets the limit of 1 µs with a margin of about 5 times. It costs about 0.1 µs more than the
no-pattern case.

## Reading of the result

- A name without a pattern does the same work as before. The deterministic test
  `NameWithoutPatternGivesTheBoundValueAndNoMappedFlag` shows that this case maps nothing.
- One step resolves a few names, so the extra time is below 1 µs for a step. A step dispatch to a device takes
  many milliseconds. The p95 step dispatch time does not change in a way that can be measured.
