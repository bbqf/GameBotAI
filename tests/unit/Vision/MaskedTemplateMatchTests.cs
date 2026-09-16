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

      after.Matches[0].Confidence.Should().BeApproximately(before.Matches[0].Confidence, 1e-9,
        "masked-out pixels must contribute to neither the correlation nor its normalisation");
      after.Matches[0].BBox.X.Should().Be(before.Matches[0].BBox.X);
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
  }
}
