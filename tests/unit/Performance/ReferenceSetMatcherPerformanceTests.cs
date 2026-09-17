#pragma warning disable CA2007, CA1861, CA1859, CA2000
using System;
using System.Diagnostics;
using System.Threading.Tasks;
using FluentAssertions;
using GameBot.Domain.Vision;
using OpenCvSharp;
using Xunit;
using Xunit.Abstractions;

namespace GameBot.UnitTests.Performance {
  /// <summary>
  /// Feature 097 (SC-003): detecting an image with alternates costs about one single match per reference,
  /// and nothing extra for an image without alternates (the decorator is never constructed then).
  /// </summary>
  [Trait("Category", "Performance")]
  public sealed class ReferenceSetMatcherPerformanceTests {
    private const int Iterations = 5;
    private readonly ITestOutputHelper _output;

    public ReferenceSetMatcherPerformanceTests(ITestOutputHelper output) {
      _output = output;
    }

    private static Mat Noise(int width, int height, int seed) {
      var mat = new Mat(height, width, MatType.CV_8UC3);
      var rng = new RNG((ulong)seed);
      rng.Fill(mat, DistributionType.Uniform, Scalar.All(0), Scalar.All(255));
      return mat;
    }

    private static async Task<double> MedianMsAsync(ITemplateMatcher matcher, Mat screen, Mat template, TemplateMatcherConfig config) {
      var samples = new double[Iterations];
      await matcher.MatchAllAsync(screen, template, config); // warm-up
      for (var i = 0; i < Iterations; i++) {
        var sw = Stopwatch.StartNew();
        await matcher.MatchAllAsync(screen, template, config);
        samples[i] = sw.Elapsed.TotalMilliseconds;
      }
      Array.Sort(samples);
      return samples[Iterations / 2];
    }

    [Fact(DisplayName = "Perf: primary + 3 alternates stays within ~N+1 single matches on a 1280x720 frame")]
    public async Task ThreeAlternatesCostRoughlyFourSingleMatches() {
      using var screen = Noise(1280, 720, 1);
      using var primary = Noise(64, 64, 2);
      using var alt1 = Noise(64, 64, 3);
      using var alt2 = Noise(64, 64, 4);
      using var alt3 = Noise(64, 64, 5);
      var config = new TemplateMatcherConfig(0.85, 10, 0.3);
      var inner = new TemplateMatcher();
      var set = new ReferenceSetTemplateMatcher(inner, "primary", new[] { ("a1", alt1), ("a2", alt2), ("a3", alt3) });

      var single = await MedianMsAsync(inner, screen, primary, config);
      var withAlternates = await MedianMsAsync(set, screen, primary, config);

      _output.WriteLine($"single reference: {single:F1} ms; primary + 3 alternates: {withAlternates:F1} ms; ratio {withAlternates / single:F2}");
      withAlternates.Should().BeLessThan(single * 6, "four references should cost about four single matches, with headroom for CI noise");
    }
  }
}
