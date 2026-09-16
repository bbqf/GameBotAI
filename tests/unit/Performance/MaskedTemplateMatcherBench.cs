using System;
using System.Diagnostics;
using System.Threading.Tasks;
using FluentAssertions;
using GameBot.Domain.Vision;
using GameBot.UnitTests.Vision;
using OpenCvSharp;
using Xunit;
using Xunit.Abstractions;

namespace GameBot.Tests.Unit.Performance;

/// <summary>
/// Perf backstop for the masked detection path (feature 089, issue #190).
/// </summary>
/// <remarks>
/// A detection that overruns <c>Service:Detections:TimeoutMs</c> is reported as an empty match set,
/// which is indistinguishable from "nothing was there" — exactly the silent failure this feature
/// exists to remove. So the masked path being merely "slower" would resurrect that failure in a new
/// guise, and it needs a hard bound, not a ratio.
/// </remarks>
public sealed class MaskedTemplateMatcherBench {
  /// <summary>The service's default detection timeout, which a real detection must fit inside.</summary>
  private const int DetectionTimeoutMs = 500;

  private readonly ITestOutputHelper _output;

  public MaskedTemplateMatcherBench(ITestOutputHelper output) => _output = output;

  [Fact(DisplayName = "Masked detection of a badge over a full-size frame fits the detection timeout")]
  public async Task MaskedDetectionFitsDetectionTimeout() {
    using var frame = MaskFixtures.CreateFrame(1080, 1920, MaskFixtures.Backdrop.Bright, new Point(400, 900));
    using var masked = MaskFixtures.CreateMaskedTemplate();
    using var opaque = MaskFixtures.CreateOpaqueTemplate(MaskFixtures.Backdrop.Bright);

    var matcher = new TemplateMatcher();
    var cfg = new TemplateMatcherConfig(0.85, 5, 0.3);

    async Task<double> Run(Mat tpl) {
      // One warm-up pass so OpenCV's DFT plans and the JIT are not charged to the measurement.
      await matcher.MatchAllAsync(frame, tpl, cfg).ConfigureAwait(false);
      var sw = Stopwatch.StartNew();
      await matcher.MatchAllAsync(frame, tpl, cfg).ConfigureAwait(false);
      sw.Stop();
      return sw.Elapsed.TotalMilliseconds;
    }

    var unmaskedMs = await Run(opaque).ConfigureAwait(false);
    var maskedMs = await Run(masked).ConfigureAwait(false);

    // Informational, not asserted: a ratio is too noisy on a shared runner to gate on.
    _output.WriteLine($"unmasked={unmaskedMs:F1}ms masked={maskedMs:F1}ms ratio={maskedMs / Math.Max(1.0, unmaskedMs):F2}x");

    var isCi = string.Equals(Environment.GetEnvironmentVariable("CI"), "true", StringComparison.OrdinalIgnoreCase);
    var cap = isCi ? DetectionTimeoutMs * 4 : DetectionTimeoutMs;
    maskedMs.Should().BeLessThan(cap,
      "a masked detection that overruns the detection timeout is reported as an absence");
  }

  [Fact(DisplayName = "Masked detection of a badge over a featureless frame fits the detection timeout")]
  public async Task MaskedDetectionOnFeaturelessFrameFitsDetectionTimeout() {
    // Feature 090 (issue #196) added a per-position cutoff, a masked compare and two clamps over
    // the whole result map. A flat frame is the worst case for them: every one of the ~2M positions
    // takes the suppression branch. If the fix were to cost real time, it would cost it here — and
    // a detection that overruns the timeout is reported as an absence, which is the failure mode
    // this whole feature exists to remove.
    using var flat = MaskFixtures.CreateFlatFrame(1080, 1920, 200);
    using var masked = MaskFixtures.CreateMaskedTemplate();

    var matcher = new TemplateMatcher();
    var cfg = new TemplateMatcherConfig(0.85, 5, 0.3);

    await matcher.MatchAllAsync(flat, masked, cfg).ConfigureAwait(false);
    var sw = Stopwatch.StartNew();
    var result = await matcher.MatchAllAsync(flat, masked, cfg).ConfigureAwait(false);
    sw.Stop();

    _output.WriteLine($"masked-on-flat={sw.Elapsed.TotalMilliseconds:F1}ms suppressed={result.NoInformationPositionCount}");

    result.Matches.Should().BeEmpty();
    result.NoInformationPositionCount.Should().BeGreaterThan(0, "every position here is featureless");

    var isCi = string.Equals(Environment.GetEnvironmentVariable("CI"), "true", StringComparison.OrdinalIgnoreCase);
    var cap = isCi ? DetectionTimeoutMs * 4 : DetectionTimeoutMs;
    sw.Elapsed.TotalMilliseconds.Should().BeLessThan(cap);
  }
}
