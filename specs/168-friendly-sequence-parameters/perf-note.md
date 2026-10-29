# Perf note: number pattern in name resolution (feature 168)

Principle IV of the constitution asks for a perf note for a hot path. The pattern step is in
`ParameterScope.TryResolve`, which runs once for each placeholder of each dispatched step.

## Method

`ParameterScopeResolvePerfTests.PatternAddsOnlyASmallCostToResolve` (Category `Benchmark`). It calls
`TryResolve("n")` 2,000,000 times on a two-layer scope, after a warm-up of 20,000 calls. One scope has a
declaration without a pattern. The other has the pattern `emulator-{value}`. The test returns at once
when the environment variable `CI` is `true`. Run it by hand:

```powershell
dotnet test C:\src\GameBot\tests\unit\GameBot.UnitTests.csproj --filter "Category=Benchmark" --logger "console;verbosity=detailed"
```

## Result (2026-10-29, Debug build, one developer machine)

| Case | Time for 2,000,000 calls | Time for one call |
|------|--------------------------|-------------------|
| No pattern | 218 ms | about 0.11 µs |
| Pattern `emulator-{value}` | 421 ms | about 0.21 µs |

The pattern case costs about 0.1 µs more for one call (ratio 1.93).

## Reading of the result

- **SC-008 asks for no more than 2% extra time for the pattern case. The micro-benchmark does not meet this.** The
  pattern case does a declaration lookup and one string `Replace`, and the no-pattern case does a
  dictionary lookup only. In a micro-benchmark, that work is about as large as the lookup itself.
- A name without a pattern does the same work as before, plus one dictionary lookup of the declaration only
  when the name has a bound value. The deterministic test
  `NameWithoutPatternGivesTheBoundValueAndNoMappedFlag` shows that this case maps nothing.
- One step resolves a few names, so the extra time is below 1 µs for a step. A step dispatch to a device takes
  many milliseconds. The p95 step dispatch time does not change in a way that can be measured.
- The smoke limit of the benchmark is 25 times the no-pattern time. It guards against a large regression only.

## Decision needed

Either accept the 0.1 µs for one call and change SC-008 to an absolute limit (for example "less than 1 µs
for one name"), or add a cache of the mapped value. The spec text of SC-008 is not changed by this note.
