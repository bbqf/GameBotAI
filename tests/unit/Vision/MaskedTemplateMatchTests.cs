using System.Threading.Tasks;
using FluentAssertions;
using GameBot.Domain.Vision;
using OpenCvSharp;
using Xunit;

namespace GameBot.UnitTests.Vision {
  /// <summary>
  /// Regression cover for issue #190: a circular badge whose rectangular crop carries dead
  /// background cannot match over a different backdrop, and the fix must not move any existing
  /// score by so much as a float bit.
  /// </summary>
  public class MaskedTemplateMatchTests {
    private const double Gate = 0.85;
    private const int FrameWidth = 320;
    private const int FrameHeight = 240;
    private static readonly Point BadgeAt = new(150, 100);

    private static TemplateMatcherConfig Config(double threshold = 0.0) =>
      new(Threshold: threshold, MaxResults: 1, Overlap: 0.3);

    private static uint Noise(int x, int y) {
      unchecked {
        var h = ((uint)x * 2654435761u) ^ ((uint)y * 2246822519u);
        h ^= h >> 13;
        h *= 0x5BD1E995u;
        return h ^ (h >> 15);
      }
    }

    [Fact(DisplayName = "Issue 190: the rectangular template fails over a foreign backdrop while the masked one passes")]
    public async Task MaskedTemplatePassesGateWhereRectangularTemplateFails() {
      using var frame = MaskFixtures.CreateFrame(FrameWidth, FrameHeight, MaskFixtures.Backdrop.Bright, BadgeAt);
      // Authored on the dark instance; the badge art is pixel-identical, only the backdrop differs.
      using var rectangular = MaskFixtures.CreateOpaqueTemplate(MaskFixtures.Backdrop.Dark);
      using var masked = MaskFixtures.CreateMaskedTemplate();
      var matcher = new TemplateMatcher();

      var rectangularResult = await matcher.MatchAllAsync(frame, rectangular, Config());
      var maskedResult = await matcher.MatchAllAsync(frame, masked, Config());

      rectangularResult.Matches[0].Confidence.Should().BeLessThan(Gate,
        "roughly a third of the rectangular crop is backdrop that does not exist on this instance");
      rectangularResult.Masked.Should().BeFalse();

      maskedResult.Matches[0].Confidence.Should().BeGreaterThanOrEqualTo(Gate);
      maskedResult.Masked.Should().BeTrue();
      maskedResult.RetainedPixelCount.Should().Be(MaskFixtures.CountBadgePixels());
    }

    [Fact(DisplayName = "A masked template reports no match on a control frame with no badge")]
    public async Task MaskedTemplateReportsNoMatchOnControlFrame() {
      using var control = MaskFixtures.CreateFrame(FrameWidth, FrameHeight, MaskFixtures.Backdrop.Bright, badgeAt: null);
      using var masked = MaskFixtures.CreateMaskedTemplate();
      var matcher = new TemplateMatcher();

      var result = await matcher.MatchAllAsync(control, masked, Config(Gate));

      result.Matches.Should().BeEmpty("masking must not turn the template into something that matches anything");
      result.Masked.Should().BeTrue();
    }

    [Fact(DisplayName = "A masked match reports the full template rectangle, not the retained bounding box")]
    public async Task MaskedMatchReportsFullTemplateRectangle() {
      using var frame = MaskFixtures.CreateFrame(FrameWidth, FrameHeight, MaskFixtures.Backdrop.Bright, BadgeAt);
      using var masked = MaskFixtures.CreateMaskedTemplate();
      var matcher = new TemplateMatcher();

      var result = await matcher.MatchAllAsync(frame, masked, Config());

      var bbox = result.Matches[0].BBox;
      bbox.Width.Should().Be(MaskFixtures.BadgeWidth);
      bbox.Height.Should().Be(MaskFixtures.BadgeHeight);
      bbox.X.Should().Be(BadgeAt.X);
      bbox.Y.Should().Be(BadgeAt.Y);
    }

    [Fact(DisplayName = "Repainting the frame under the transparent region leaves the score unchanged")]
    public async Task RepaintingUnderTransparentRegionLeavesScoreUnchanged() {
      using var frame = MaskFixtures.CreateFrame(FrameWidth, FrameHeight, MaskFixtures.Backdrop.Bright, BadgeAt);
      using var repainted = frame.Clone();
      MaskFixtures.RepaintUnderMask(repainted, BadgeAt);
      using var masked = MaskFixtures.CreateMaskedTemplate();
      var matcher = new TemplateMatcher();

      var before = await matcher.MatchAllAsync(frame, masked, Config());
      var after = await matcher.MatchAllAsync(repainted, masked, Config());

      // The masked score is computed in float32 through OpenCV's correlation, whose rounding mixes in
      // every frame pixel and depends on the CPU's vector path: repainting the masked-out region moves
      // the score by up to a few 1e-6 on some CI runners (0.999997 vs 1.0 observed). 1e-9 sat below
      // float32 resolution and failed intermittently. 1e-5 matches the border-mask test below and is
      // still orders of magnitude under what a leak would cause — the control asserts that.
      after.Matches[0].Confidence.Should().BeApproximately(before.Matches[0].Confidence, 1e-5,
        "masked-out pixels must contribute to neither the correlation nor its normalisation");
      after.Matches[0].BBox.X.Should().Be(before.Matches[0].BBox.X);

      // Control: the same repaint under an unmasked template (which does see those pixels) moves its
      // score far beyond the tolerance, so the assertion above would catch a real leak.
      using var opaque = MaskFixtures.CreateOpaqueTemplate(MaskFixtures.Backdrop.Bright);
      var opaqueBefore = await matcher.MatchAllAsync(frame, opaque, Config(-1.0));
      var opaqueAfter = await matcher.MatchAllAsync(repainted, opaque, Config(-1.0));
      System.Math.Abs(opaqueAfter.Matches[0].Confidence - opaqueBefore.Matches[0].Confidence)
        .Should().BeGreaterThan(0.01, "the repaint must be large enough that leakage would be visible");
    }

    [Fact(DisplayName = "A masked template with a uniform retained region reports no match")]
    public async Task MaskedTemplateWithUniformRetainedRegionReportsNoMatch() {
      using var frame = MaskFixtures.CreateFrame(FrameWidth, FrameHeight, MaskFixtures.Backdrop.Bright, BadgeAt);
      using var uniform = MaskFixtures.CreateMaskedTemplate(uniformInterior: true);
      var matcher = new TemplateMatcher();

      var result = await matcher.MatchAllAsync(frame, uniform, Config(-1.0));

      result.Matches.Should().BeEmpty("the correlation is undefined with no variation under the mask");
      result.Masked.Should().BeTrue();
    }

    [Fact(DisplayName = "A fully transparent template reports no match")]
    public async Task FullyTransparentTemplateReportsNoMatch() {
      using var frame = MaskFixtures.CreateFrame(FrameWidth, FrameHeight, MaskFixtures.Backdrop.Bright, BadgeAt);
      using var empty = new Mat(new Size(8, 8), MatType.CV_8UC4, new Scalar(255, 0, 255, 0));
      var matcher = new TemplateMatcher();

      var result = await matcher.MatchAllAsync(frame, empty, Config(-1.0));

      result.Matches.Should().BeEmpty();
      result.Masked.Should().BeTrue();
      result.RetainedPixelCount.Should().Be(0);
    }

    [Fact(DisplayName = "Masking a border reproduces the score of the equivalent cropped template")]
    public async Task MaskingBorderReproducesScoreOfEquivalentCroppedTemplate() {
      // The masked score must sit on the same 0..1 scale as an unmasked one, or an operator could
      // not reuse a threshold calibrated before the image was masked (FR-003a).
      const int patch = 20;
      const int border = 5;
      const int at = 10;

      // Hash-based, not periodic: a repeating pattern would make several offsets score alike and
      // the best-match location ambiguous.
      using var frame = new Mat(new Size(60, 60), MatType.CV_8UC3, new Scalar(0, 0, 0));
      for (var y = 0; y < 60; y++) {
        for (var x = 0; x < 60; x++) {
          var h = Noise(x, y);
          frame.Set(y, x, new Vec3b((byte)(h & 0xFF), (byte)((h >> 8) & 0xFF), (byte)((h >> 16) & 0xFF)));
        }
      }

      // A cropped template that does not match the frame perfectly, so the score is a non-trivial
      // value rather than a saturated 1.0 that any formulation would produce.
      using var cropped = new Mat(new Size(patch, patch), MatType.CV_8UC3, new Scalar(0, 0, 0));
      using var maskedWithBorder = new Mat(new Size(patch + (2 * border), patch + (2 * border)), MatType.CV_8UC4, new Scalar(0, 0, 0, 0));
      for (var y = 0; y < patch; y++) {
        for (var x = 0; x < patch; x++) {
          var src = frame.At<Vec3b>(at + y, at + x);
          var tweaked = new Vec3b((byte)((src.Item0 + 9) % 256), src.Item1, (byte)((src.Item2 + 3) % 256));
          cropped.Set(y, x, tweaked);
          maskedWithBorder.Set(border + y, border + x, new Vec4b(tweaked.Item0, tweaked.Item1, tweaked.Item2, 255));
        }
      }

      var matcher = new TemplateMatcher();
      var croppedResult = await matcher.MatchAllAsync(frame, cropped, Config());
      var maskedResult = await matcher.MatchAllAsync(frame, maskedWithBorder, Config());

      maskedResult.Masked.Should().BeTrue();
      maskedResult.Matches[0].Confidence.Should().BeApproximately(croppedResult.Matches[0].Confidence, 1e-5);
      maskedResult.Matches[0].BBox.X.Should().Be(croppedResult.Matches[0].BBox.X - border);
      maskedResult.Matches[0].BBox.Y.Should().Be(croppedResult.Matches[0].BBox.Y - border);
    }

    [Fact(DisplayName = "An unmasked template scores exactly what OpenCV's CCoeffNormed produces")]
    public async Task UnmaskedTemplateScoresExactlyWhatCCoeffNormedProduces() {
      using var frame = MaskFixtures.CreateFrame(FrameWidth, FrameHeight, MaskFixtures.Backdrop.Bright, BadgeAt);
      using var rectangular = MaskFixtures.CreateOpaqueTemplate(MaskFixtures.Backdrop.Bright);

      using var graySrc = frame.CvtColor(ColorConversionCodes.BGR2GRAY);
      using var grayTpl = rectangular.CvtColor(ColorConversionCodes.BGR2GRAY);
      using var reference = new Mat();
      Cv2.MatchTemplate(graySrc, grayTpl, reference, TemplateMatchModes.CCoeffNormed);
      reference.MinMaxLoc(out _, out double expected);

      var result = await new TemplateMatcher().MatchAllAsync(frame, rectangular, Config());

      result.Matches[0].Confidence.Should().Be(expected,
        "the unmasked path must stay bit-identical or every hand-calibrated live threshold drifts");
      result.Masked.Should().BeFalse();
      result.RetainedPixelCount.Should().Be(0);
      result.NoInformationPositionCount.Should().Be(0, "the unmasked path has no no-information rule");
    }

    [Fact(DisplayName = "Issue 196: the unmasked path is untouched even on a flat frame")]
    public async Task UnmaskedTemplateOnFlatFrameStillScoresExactlyWhatCCoeffNormedProduces() {
      // The flat frame is where the masked and unmasked paths previously disagreed most — one
      // returned infinity, the other did not. Zero drift has to hold here too, and it holds
      // structurally: this template is not masked, so none of the new code is reached.
      using var flat = MaskFixtures.CreateFlatFrame(FrameWidth, FrameHeight, 200);
      using var rectangular = MaskFixtures.CreateOpaqueTemplate(MaskFixtures.Backdrop.Bright);

      using var graySrc = flat.CvtColor(ColorConversionCodes.BGR2GRAY);
      using var grayTpl = rectangular.CvtColor(ColorConversionCodes.BGR2GRAY);
      using var reference = new Mat();
      Cv2.MatchTemplate(graySrc, grayTpl, reference, TemplateMatchModes.CCoeffNormed);
      reference.MinMaxLoc(out _, out double expected);

      var result = await new TemplateMatcher().MatchAllAsync(flat, rectangular, Config(threshold: -1.0));

      result.Matches[0].Confidence.Should().Be(expected);
      result.Masked.Should().BeFalse();
    }

    [Fact(DisplayName = "An all-opaque alpha channel scores identically to the same image without one")]
    public async Task AllOpaqueAlphaChannelScoresIdenticallyToImageWithoutOne() {
      using var frame = MaskFixtures.CreateFrame(FrameWidth, FrameHeight, MaskFixtures.Backdrop.Bright, BadgeAt);
      using var threeChannel = MaskFixtures.CreateOpaqueTemplate(MaskFixtures.Backdrop.Bright);
      using var fourChannel = new Mat();
      Cv2.CvtColor(threeChannel, fourChannel, ColorConversionCodes.BGR2BGRA);

      var matcher = new TemplateMatcher();
      var withoutAlpha = await matcher.MatchAllAsync(frame, threeChannel, Config());
      var withOpaqueAlpha = await matcher.MatchAllAsync(frame, fourChannel, Config());

      withOpaqueAlpha.Matches[0].Confidence.Should().Be(withoutAlpha.Matches[0].Confidence);
      withOpaqueAlpha.Masked.Should().BeFalse();
    }

    // ---- Issue #196 (B-013): a featureless screen region is not a perfect match ----------------
    //
    // The reported symptom was a score of exactly 1.0000 against a dimmed modal. The cause is that
    // a flat region leaves the correlation with a zero denominator, and the division produced
    // ±Infinity rather than the zero the code claimed; Normalization.ClampConfidence then turned
    // every infinity into exactly 1.0 on the way out. These tests assert against the raw score, not
    // just against "below threshold", because a clamp downstream is what hid this for a release.

    [Fact(DisplayName = "Issue 196: a masked template reports no match on a perfectly flat frame")]
    public async Task FlatSceneRegionReportsNoMatch() {
      using var flat = MaskFixtures.CreateFlatFrame(FrameWidth, FrameHeight, 200);
      using var masked = MaskFixtures.CreateMaskedTemplate();
      var matcher = new TemplateMatcher();

      var result = await matcher.MatchAllAsync(flat, masked, Config(threshold: 0.0));

      foreach (var match in result.Matches) {
        double.IsFinite(match.Confidence).Should().BeTrue(
          "an infinite score is not a measurement, and the boundary clamp turns it into a convincing 1.0");
        match.Confidence.Should().Be(0d,
          "a region with nothing to correlate against carries no information, which is a score of zero (FR-015)");
      }

      var gated = await matcher.MatchAllAsync(flat, masked, Config(Gate));
      gated.Matches.Should().BeEmpty("a flat modal backdrop must never arm a tap");
      gated.NoInformationPositionCount.Should().BeGreaterThan(0,
        "the suppression must be visible to an operator diagnosing a detection that stopped matching");
    }

    [Fact(DisplayName = "Issue 196: a frame that is flat but for one pixel in 1024 is still no match")]
    public async Task NearFlatSceneRegionReportsNoMatch() {
      // One differing pixel in a retained region is the smallest non-zero variation 8-bit content
      // can carry — evidence of nothing.
      using var nearFlat = MaskFixtures.CreateGradedFrame(FrameWidth, FrameHeight, 200, amplitude: 1, everyNth: 1024);
      using var masked = MaskFixtures.CreateMaskedTemplate();
      var matcher = new TemplateMatcher();

      MaskFixtures.MeasureSceneStdUnderMask(nearFlat, masked, BadgeAt.X, BadgeAt.Y)
        .Should().BeLessThan(1.0, "the fixture must actually sit below the cutoff for this test to mean anything");

      var result = await matcher.MatchAllAsync(nearFlat, masked, Config(threshold: 0.0));

      foreach (var match in result.Matches) {
        double.IsFinite(match.Confidence).Should().BeTrue();
        match.Confidence.Should().Be(0d);
      }
      (await matcher.MatchAllAsync(nearFlat, masked, Config(Gate))).Matches.Should().BeEmpty();
    }

    [Theory(DisplayName = "Issue 196: no score ever leaves the measure's range, at any scene variance")]
    [InlineData(0, 1)]
    [InlineData(1, 1024)]
    [InlineData(1, 64)]
    [InlineData(1, 1)]
    [InlineData(2, 2)]
    [InlineData(2, 1)]
    [InlineData(4, 1)]
    public async Task ScoreNeverLeavesTheMeasureRange(int amplitude, int everyNth) {
      using var frame = MaskFixtures.CreateGradedFrame(FrameWidth, FrameHeight, 200, amplitude, everyNth);
      using var masked = MaskFixtures.CreateMaskedTemplate();
      var matcher = new TemplateMatcher();

      var result = await matcher.MatchAllAsync(frame, masked, Config(threshold: -1.0));

      foreach (var match in result.Matches) {
        double.IsNaN(match.Confidence).Should().BeFalse();
        double.IsInfinity(match.Confidence).Should().BeFalse();
        match.Confidence.Should().BeInRange(-1.0, 1.0,
          "the range has to hold where the score is produced — guaranteeing it only at the API boundary is a disguise, not a guarantee");
      }
    }

    [Fact(DisplayName = "Issue 196: a masked template with an almost-featureless retained region reports no match")]
    public async Task MaskedTemplateWithAlmostUniformRetainedRegionReportsNoMatch() {
      // FR-018: the mirror of the same degeneracy, on the reference-image side. The repository has
      // met it before as the `pns-never-matches` sentinel.
      using var almostUniform = CreateAlmostUniformMaskedTemplate();
      using var detailed = MaskFixtures.CreateFrame(FrameWidth, FrameHeight, MaskFixtures.Backdrop.Bright, BadgeAt);
      var matcher = new TemplateMatcher();

      var result = await matcher.MatchAllAsync(detailed, almostUniform, Config(threshold: 0.0));

      result.Matches.Should().BeEmpty(
        "a reference image carrying less than one shade level of detail correlates with almost anything");
      result.Masked.Should().BeTrue();
    }

    [Fact(DisplayName = "Issue 196: the reported degenerate region is scored normally, not suppressed and not 1.0")]
    public async Task ReportedDegenerateSceneIsScoredNormally() {
      // The issue's own case: a retained region with a standard deviation of about 2.4. It sits
      // above the cutoff, so it must still be scored — and scored correctly. Reported as 1.0000
      // before the fix; independently computed as 0.5171 on the live capture.
      using var frame = MaskFixtures.CreateGradedFrame(FrameWidth, FrameHeight, 200, amplitude: 4);
      using var masked = MaskFixtures.CreateMaskedTemplate();
      var matcher = new TemplateMatcher();

      var sceneStd = MaskFixtures.MeasureSceneStdUnderMask(frame, masked, BadgeAt.X, BadgeAt.Y);
      sceneStd.Should().BeGreaterThan(1.0, "this fixture must sit above the cutoff or it tests the wrong thing");
      sceneStd.Should().BeInRange(2.0, 3.0, "and it must reproduce the standard deviation the issue reported");

      var result = await matcher.MatchAllAsync(frame, masked, Config(threshold: -1.0));
      var atBadgePosition = result.Matches[0];

      atBadgePosition.Confidence.Should().BeLessThan(0.60,
        "there is no badge here; 1.0000 was the defect (SC-001)");
      atBadgePosition.Confidence.Should().BeApproximately(
        MaskFixtures.ReferenceMaskedScore(frame, masked, atBadgePosition.BBox.X, atBadgePosition.BBox.Y), 0.01,
        "the score must agree with the measure computed independently from its definition (SC-001, FR-002)");
      result.NoInformationPositionCount.Should().Be(0, "nothing here is below the cutoff");
    }

    [Fact(DisplayName = "Issue 196: masked accuracy on real content is unchanged by the degeneracy fix")]
    public async Task MaskedAccuracyOnRealContentIsUnchanged() {
      // SC-003. The whole point of the fix is that it costs nothing where the measure was working:
      // feature 089 exists because these numbers were achievable, and #190 stays fixed only if they
      // stay achievable.
      using var withBadge = MaskFixtures.CreateFrame(FrameWidth, FrameHeight, MaskFixtures.Backdrop.Bright, BadgeAt);
      using var withoutBadge = MaskFixtures.CreateFrame(FrameWidth, FrameHeight, MaskFixtures.Backdrop.Bright, badgeAt: null);
      using var masked = MaskFixtures.CreateMaskedTemplate();
      var matcher = new TemplateMatcher();

      var present = await matcher.MatchAllAsync(withBadge, masked, Config(threshold: -1.0));
      var absent = await matcher.MatchAllAsync(withoutBadge, masked, Config(threshold: -1.0));

      present.Matches[0].Confidence.Should().BeGreaterThanOrEqualTo(0.93,
        "a genuine badge must still score at the top of the range");
      present.Matches[0].BBox.X.Should().Be(BadgeAt.X);
      present.Matches[0].BBox.Y.Should().Be(BadgeAt.Y);

      // SC-003 quotes 0.50-0.60 for a real city screen with no badge. This fixture scores lower
      // (~0.11) and that is by design, not drift: MaskFixtures derives the backdrop and the badge
      // from different hashes precisely so nothing can correlate by accident, while a real city
      // screen shares structure with the badge drawn on it. The band asserted here is the one this
      // fixture can honestly support — comfortably under the gate, and far under the match score.
      absent.Matches[0].Confidence.Should().BeLessThan(0.60,
        "a detailed screen with no badge must stay well clear of any live gate");
      (present.Matches[0].Confidence - absent.Matches[0].Confidence).Should().BeGreaterThan(0.3,
        "present and absent must stay clearly separated, which is what makes a threshold calibratable");
      absent.NoInformationPositionCount.Should().Be(0, "detailed content is never suppressed");
    }

    [Theory(DisplayName = "Issue 196: the score is continuous across the no-information cutoff")]
    [InlineData(1, 4)]
    [InlineData(1, 2)]
    [InlineData(1, 1)]
    [InlineData(2, 2)]
    [InlineData(2, 1)]
    [InlineData(3, 1)]
    [InlineData(4, 1)]
    public async Task ScoreIsContinuousAcrossTheCutoff(int amplitude, int everyNth) {
      // FR-006: as the screen gains detail the score must rise smoothly out of the suppressed zero.
      // A jump straight from "no match" to a high score would just relocate the cliff.
      using var frame = MaskFixtures.CreateGradedFrame(FrameWidth, FrameHeight, 200, amplitude, everyNth);
      using var masked = MaskFixtures.CreateMaskedTemplate();
      var matcher = new TemplateMatcher();

      var result = await matcher.MatchAllAsync(frame, masked, Config(threshold: -1.0));

      foreach (var match in result.Matches) {
        match.Confidence.Should().BeLessThan(0.60,
          "no frame of undifferentiated noise should ever look like the badge, on either side of the cutoff");
      }
    }

    [Fact(DisplayName = "Issue 196: masked scores on real content agree with the measure computed independently")]
    public async Task MaskedScoreAgreesWithIndependentReferenceOnDetailedContent() {
      var matcher = new TemplateMatcher();
      using var masked = MaskFixtures.CreateMaskedTemplate();

      foreach (var backdrop in new[] { MaskFixtures.Backdrop.Bright, MaskFixtures.Backdrop.Dark }) {
        foreach (var badgeAt in new Point?[] { BadgeAt, null }) {
          using var frame = MaskFixtures.CreateFrame(FrameWidth, FrameHeight, backdrop, badgeAt);
          var result = await matcher.MatchAllAsync(frame, masked, Config(threshold: -1.0));
          var top = result.Matches[0];

          top.Confidence.Should().BeApproximately(
            MaskFixtures.ReferenceMaskedScore(frame, masked, top.BBox.X, top.BBox.Y), 0.001,
            "FR-002: within 0.001 of the same measure computed from its definition in double precision");
        }
      }
    }

    /// <summary>A masked template whose retained pixels vary by less than one shade level.</summary>
    private static Mat CreateAlmostUniformMaskedTemplate() {
      using var basis = MaskFixtures.CreateMaskedTemplate();
      var tpl = new Mat(new Size(MaskFixtures.BadgeWidth, MaskFixtures.BadgeHeight), MatType.CV_8UC4, new Scalar(0, 0, 0, 0));
      for (var y = 0; y < MaskFixtures.BadgeHeight; y++) {
        for (var x = 0; x < MaskFixtures.BadgeWidth; x++) {
          var alpha = basis.At<Vec4b>(y, x).Item3;
          if (alpha < TemplateMask.RetainedAlphaThreshold) {
            tpl.Set(y, x, new Vec4b(255, 0, 255, 0));
            continue;
          }
          // One pixel in 512 differs by a single shade: non-zero variance, far below the cutoff.
          var v = (byte)((((y * MaskFixtures.BadgeWidth) + x) % 512 == 0) ? 91 : 90);
          tpl.Set(y, x, new Vec4b(v, v, v, 255));
        }
      }
      return tpl;
    }
  }
}
