using System.Diagnostics;
using System.Threading.Tasks;
using FluentAssertions;
using GameBot.Domain.Vision;
using OpenCvSharp;
using Xunit;

namespace GameBot.Tests.Unit.Performance;

public sealed class TemplateMatcherBench {
  private static Mat MakeSolid(int w, int h, byte val = 255) {
    var img = new Mat(h, w, MatType.CV_8UC3, new Scalar(val, val, val));
    return img;
  }

  [Fact(Skip = "Benchmark harness - enable locally to measure perf")]
  public async Task BenchmarkVariousSizes() {
    using var screen = MakeSolid(640, 480, 200);
    using var tplSmall = MakeSolid(16, 16, 180);
    using var tplMed = MakeSolid(64, 64, 180);
    using var tplLarge = MakeSolid(128, 128, 180);

    var matcher = new TemplateMatcher();
    var cfg = new TemplateMatcherConfig(0.5, 10, 0.3);

    async Task<long> Run(Mat tpl) {
      var sw = Stopwatch.StartNew();
      var _ = await matcher.MatchAllAsync(screen, tpl, cfg).ConfigureAwait(false);
      sw.Stop();
      return (long)sw.Elapsed.TotalMilliseconds;
    }

    var smallMs = await Run(tplSmall).ConfigureAwait(false);
    var medMs = await Run(tplMed).ConfigureAwait(false);
    var largeMs = await Run(tplLarge).ConfigureAwait(false);

    smallMs.Should().BeLessThan(200);
    medMs.Should().BeLessThan(400);
    largeMs.Should().BeLessThan(800);
  }
}
