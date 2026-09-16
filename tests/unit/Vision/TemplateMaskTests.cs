using FluentAssertions;
using GameBot.Domain.Vision;
using OpenCvSharp;
using Xunit;

namespace GameBot.UnitTests.Vision {
  public class TemplateMaskTests {
    private static Mat CreateBgra(int width, int height, byte alpha) {
      return new Mat(new Size(width, height), MatType.CV_8UC4, new Scalar(10, 20, 30, alpha));
    }

    [Fact(DisplayName = "TryCreate retains alpha at the threshold and drops it just below")]
    public void TryCreateRetainsAlphaAtThresholdAndDropsJustBelow() {
      using var tpl = CreateBgra(4, 1, 0);
      tpl.Set(0, 0, new Vec4b(1, 2, 3, (byte)TemplateMask.RetainedAlphaThreshold));
      tpl.Set(0, 1, new Vec4b(1, 2, 3, (byte)(TemplateMask.RetainedAlphaThreshold - 1)));
      tpl.Set(0, 2, new Vec4b(1, 2, 3, 255));
      tpl.Set(0, 3, new Vec4b(1, 2, 3, 0));

      var created = TemplateMask.TryCreate(tpl, out var mask, out var retained);

      using (mask) {
        created.Should().BeTrue();
        retained.Should().Be(2, "alpha 128 and 255 are retained, 127 and 0 are not");
        mask.At<byte>(0, 0).Should().Be(255);
        mask.At<byte>(0, 1).Should().Be(0);
        mask.At<byte>(0, 2).Should().Be(255);
        mask.At<byte>(0, 3).Should().Be(0);
      }
    }

    [Fact(DisplayName = "TryCreate refuses a template with no alpha channel")]
    public void TryCreateRefusesTemplateWithNoAlphaChannel() {
      using var tpl = new Mat(new Size(8, 8), MatType.CV_8UC3, new Scalar(10, 20, 30));

      var created = TemplateMask.TryCreate(tpl, out _, out var retained);

      created.Should().BeFalse();
      retained.Should().Be(0);
    }

    [Fact(DisplayName = "TryCreate refuses an all-opaque alpha channel so existing images never shift")]
    public void TryCreateRefusesAllOpaqueAlphaChannel() {
      using var tpl = CreateBgra(8, 8, 255);

      var created = TemplateMask.TryCreate(tpl, out _, out _);

      created.Should().BeFalse("an all-opaque alpha is not a mask; treating it as one would move scores that live thresholds are calibrated against");
    }

    [Fact(DisplayName = "TryCreate accepts a fully transparent template with a zero retained count")]
    public void TryCreateAcceptsFullyTransparentTemplateWithZeroRetainedCount() {
      using var tpl = CreateBgra(8, 8, 0);

      var created = TemplateMask.TryCreate(tpl, out var mask, out var retained);

      using (mask) {
        created.Should().BeTrue();
        retained.Should().Be(0);
      }
    }

    [Fact(DisplayName = "TryCreate reports the badge's opaque pixel count")]
    public void TryCreateReportsBadgeOpaquePixelCount() {
      using var tpl = MaskFixtures.CreateMaskedTemplate();

      var created = TemplateMask.TryCreate(tpl, out var mask, out var retained);

      using (mask) {
        created.Should().BeTrue();
        retained.Should().Be(MaskFixtures.CountBadgePixels());
        retained.Should().BeGreaterThan(TemplateMask.MinimumRetainedPixels);
      }
    }

    [Fact(DisplayName = "TryCreate counts partially transparent pixels by the half-opaque rule")]
    public void TryCreateCountsPartiallyTransparentPixelsByHalfOpaqueRule() {
      using var image = CreateBgra(4, 4, 0);
      image.Set(0, 0, new Vec4b(1, 2, 3, 255));
      image.Set(1, 1, new Vec4b(1, 2, 3, 200));
      image.Set(2, 2, new Vec4b(1, 2, 3, 60));

      var created = TemplateMask.TryCreate(image, out var mask, out var retained);

      using (mask) {
        created.Should().BeTrue();
        retained.Should().Be(2, "alpha 60 is more transparent than opaque, so it is background");
      }
    }
  }
}
