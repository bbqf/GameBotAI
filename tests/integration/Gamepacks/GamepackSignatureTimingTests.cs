using System;
using System.Diagnostics;
using System.Text;
using FluentAssertions;
using GameBot.Domain.Gamepacks.Signing;
using GameBot.UnitTests.Gamepacks;
using GameBot.UnitTests.Gamepacks.Signing;
using Xunit;

#pragma warning disable CA2007, CA1861, CA1707, CA1416, CA2000

namespace GameBot.IntegrationTests.Gamepacks;

/// <summary>Feature 146 (T032, SC-007): the signature check on a 50 MB gamepack adds less than 2 seconds.</summary>
public sealed class GamepackSignatureTimingTests {
  [Fact]
  [Trait("Category", "Performance")]
  public void TheCheckOnA50MbGamepackTakesLessThanTwoSeconds() {
    // Five images of 10 MB base64 text each make about 50 MB of JSON.
    var data = new string('A', 10_000_000);
    var images = new object[5];
    for (var i = 0; i < images.Length; i++) {
      images[i] = GamepackTestKit.Img("big" + i, data);
    }

    var json = GamepackTestKit.Pack("big", "1", images: images);
    Encoding.UTF8.GetByteCount(json).Should().BeGreaterThan(49_000_000);
    using var signer = new TestSigner();
    var signature = signer.Sign(json);
    var keys = new[] { signer.AsTrusted() };
    var verifier = new GamepackSignatureVerifier();

    var watch = Stopwatch.StartNew();
    var status = verifier.Verify(json, signature, keys);
    watch.Stop();

    status.Should().Be(SignatureStatus.Valid);
    watch.Elapsed.Should().BeLessThan(TimeSpan.FromSeconds(2));
  }
}
