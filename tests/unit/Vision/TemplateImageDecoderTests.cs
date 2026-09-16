using FluentAssertions;
using GameBot.Domain.Vision;
using OpenCvSharp;
using Xunit;

namespace GameBot.UnitTests.Vision {
  public class TemplateImageDecoderTests {
    private static byte[] EncodePng(Mat image) {
      Cv2.ImEncode(".png", image, out var bytes);
      return bytes;
    }

    [Fact(DisplayName = "Decode keeps the alpha channel of a transparent PNG")]
    public void DecodeKeepsAlphaChannelOfTransparentPng() {
      using var source = MaskFixtures.CreateMaskedTemplate();
      var bytes = EncodePng(source);

      using var decoded = TemplateImageDecoder.Decode(bytes);

      decoded.Channels().Should().Be(4, "the alpha channel is the mask the operator authored");
      decoded.At<Vec4b>(0, 0).Item3.Should().Be(0, "the corner of the badge crop is transparent");
    }

    [Fact(DisplayName = "Decode returns three channels for an opaque PNG")]
    public void DecodeReturnsThreeChannelsForOpaquePng() {
      using var source = MaskFixtures.CreateOpaqueTemplate(MaskFixtures.Backdrop.Dark);
      var bytes = EncodePng(source);

      using var decoded = TemplateImageDecoder.Decode(bytes);

      decoded.Channels().Should().Be(3);
    }

    [Fact(DisplayName = "Decode returns one channel for a grayscale PNG")]
    public void DecodeReturnsOneChannelForGrayscalePng() {
      using var source = new Mat(new Size(8, 8), MatType.CV_8UC1, new Scalar(120));
      var bytes = EncodePng(source);

      using var decoded = TemplateImageDecoder.Decode(bytes);

      decoded.Channels().Should().Be(1);
    }

    [Fact(DisplayName = "Decode falls back to the colour decode for a 16-bit PNG")]
    public void DecodeFallsBackToColourDecodeForSixteenBitPng() {
      using var source = new Mat(new Size(8, 8), MatType.CV_16UC3, new Scalar(4000, 5000, 6000));
      var bytes = EncodePng(source);

      using var decoded = TemplateImageDecoder.Decode(bytes);

      decoded.Depth().Should().Be((int)MatType.CV_8U, "a depth the matcher has never handled falls back to today's behaviour");
      decoded.Channels().Should().Be(3);
    }

    [Fact(DisplayName = "A decoded alpha PNG round-trips to a masked match")]
    public void DecodedAlphaPngRoundTripsToMaskedMatch() {
      using var source = MaskFixtures.CreateMaskedTemplate();
      var bytes = EncodePng(source);

      using var decoded = TemplateImageDecoder.Decode(bytes);
      var created = TemplateMask.TryCreate(decoded, out var mask, out var retained);

      using (mask) {
        created.Should().BeTrue();
        retained.Should().Be(MaskFixtures.CountBadgePixels(), "PNG encoding must not flatten the mask");
      }
    }
  }
}
