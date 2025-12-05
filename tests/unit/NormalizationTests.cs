using FluentAssertions;
using GameBot.Domain.Vision;
using Xunit;

namespace GameBot.Tests.Unit;

public sealed class NormalizationTests {
  [Fact(DisplayName = "NormalizeRect computes expected fractions across sizes")]
  public void NormalizeRectComputesExpectedFractions() {
    Normalization.NormalizeRect(50, 100, 200, 400, 1000, 2000, out var x, out var y, out var w, out var h);
    x.Should().BeApproximately(0.05, 1e-6);
    y.Should().BeApproximately(0.05, 1e-6);
    w.Should().BeApproximately(0.2, 1e-6);
    h.Should().BeApproximately(0.2, 1e-6);
  }

  [Fact(DisplayName = "ClampConfidence keeps values within [0,1]")]
  public void ClampConfidenceKeepsRange() {
    Normalization.ClampConfidence(-0.3).Should().Be(0.0);
    Normalization.ClampConfidence(0.0).Should().Be(0.0);
    Normalization.ClampConfidence(0.5).Should().Be(0.5);
    Normalization.ClampConfidence(1.0).Should().Be(1.0);
    Normalization.ClampConfidence(1.7).Should().Be(1.0);
  }
}
