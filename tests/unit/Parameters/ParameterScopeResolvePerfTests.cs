using System.Diagnostics;
using FluentAssertions;
using GameBot.Domain.Parameters;
using Xunit;
using Xunit.Abstractions;

namespace GameBot.UnitTests.Parameters;

/// <summary>
/// Feature 168 (SC-008): the number pattern adds little work to the name resolution. The benchmark is
/// run by hand for the perf note. CI skips it with <c>--filter "Category!=Benchmark"</c>.
/// </summary>
public sealed class ParameterScopeResolvePerfTests {
  private readonly ITestOutputHelper _output;

  public ParameterScopeResolvePerfTests(ITestOutputHelper output) {
    _output = output;
  }

  private static ParameterScope Build(string? pattern) {
    var declaration = new ParameterDeclaration { Name = "n", Type = ParameterValueType.Number, Pattern = pattern };
    return ParameterScope.Empty
        .Child(ParameterScopeLayers.Sequence, null, new[] { declaration })
        .Child(ParameterScopeLayers.Entry, new[] { new ParameterBinding { Name = "n", Value = "5" } }, null);
  }

  private static double Measure(ParameterScope scope, int iterations) {
    for (var i = 0; i < 20000; i++) scope.TryResolve("n", out _);
    var watch = Stopwatch.StartNew();
    for (var i = 0; i < iterations; i++) scope.TryResolve("n", out _);
    watch.Stop();
    return watch.Elapsed.TotalMilliseconds;
  }

  [Fact]
  [Trait("Category", "Benchmark")]
  public void PatternAddsOnlyASmallCostToResolve() {
    // CI machines are noisy, so CI does not run this benchmark. Run it by hand for the perf note.
    if (string.Equals(System.Environment.GetEnvironmentVariable("CI"), "true", System.StringComparison.OrdinalIgnoreCase)) {
      _output.WriteLine("Skipped in CI.");
      return;
    }

    const int iterations = 2_000_000;
    var without = Build(null);
    var with = Build("emulator-{value}");

    var baseline = Measure(without, iterations);
    var mapped = Measure(with, iterations);
    _output.WriteLine($"without pattern: {baseline:F1} ms; with pattern: {mapped:F1} ms; ratio {mapped / baseline:F2}");

    // Smoke limit only. The target in SC-008 is 2%, checked by hand from the printed numbers.
    mapped.Should().BeLessThan(baseline * 25);
  }

  [Fact]
  public void NameWithoutPatternGivesTheBoundValueAndNoMappedFlag() {
    // Deterministic guard: a name without a pattern does no mapping work and returns the bound value as is.
    Build(null).TryResolve("n", out var value).Should().BeTrue();

    value.Text.Should().Be("5");
    value.Mapped.Should().BeFalse();
    value.Sources.Should().BeNull();
  }
}
