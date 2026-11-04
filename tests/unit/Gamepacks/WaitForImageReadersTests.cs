using System.Text.Json;
using FluentAssertions;
using GameBot.Domain.Commands;
using Xunit;

#pragma warning disable CA1707, CA1861, CA1305, CA1307, CA1310

namespace GameBot.UnitTests.Gamepacks;

/// <summary>Feature 177: the readers that moved from the save endpoint keep their results and error texts.</summary>
public sealed class WaitForImageReadersTests {
  private static JsonElement Json(string text) => JsonSerializer.Deserialize<JsonElement>(text);

  [Fact]
  public void NoRegionGivesTrueAndNull() {
    WaitForImageConfigMapper.TryReadRegion(Json("{}"), out var region, out var errors).Should().BeTrue();
    region.Should().BeNull();
    errors.Should().BeEmpty();
  }

  [Fact]
  public void NullRegionGivesTrueAndNull() {
    WaitForImageConfigMapper.TryReadRegion(Json("{\"region\":null}"), out var region, out _).Should().BeTrue();
    region.Should().BeNull();
  }

  [Fact]
  public void ARegionThatIsNotAnObjectGivesTheOldText() {
    WaitForImageConfigMapper.TryReadRegion(Json("{\"region\":5}"), out _, out var errors).Should().BeFalse();
    errors.Should().Equal("region must be an object");
  }

  [Fact]
  public void AFieldThatIsNotAWholeNumberGivesTheOldTextWithPrefix() {
    WaitForImageConfigMapper.TryReadRegion(Json("{\"region\":{\"x\":1.5,\"y\":2,\"width\":3,\"height\":4}}"), out var region, out var errors, "Step 's' region").Should().BeFalse();
    errors.Should().Equal("Step 's' region.x must be a whole number");
    region!.Y.Should().Be(2);
  }

  [Fact]
  public void AMissingFieldStaysNull() {
    WaitForImageConfigMapper.TryReadRegion(Json("{\"region\":{\"x\":1,\"y\":2,\"width\":3}}"), out var region, out var errors).Should().BeTrue();
    errors.Should().BeEmpty();
    region!.Height.Should().BeNull();
    region.X.Should().Be(1);
  }

  [Fact]
  public void ANonObjectTargetGivesTrueAndNull() {
    WaitForImageConfigMapper.TryReadRegion(Json("[]"), out var region, out _).Should().BeTrue();
    region.Should().BeNull();
  }

  [Fact]
  public void TryReadInt32AcceptsTheOldTypes() {
    WaitForImageConfigMapper.TryReadInt32(5, out var a).Should().BeTrue();
    a.Should().Be(5);
    WaitForImageConfigMapper.TryReadInt32(6L, out var b).Should().BeTrue();
    b.Should().Be(6);
    WaitForImageConfigMapper.TryReadInt32(Json("7"), out var c).Should().BeTrue();
    c.Should().Be(7);
    WaitForImageConfigMapper.TryReadInt32("8", out var d).Should().BeTrue();
    d.Should().Be(8);
  }

  [Fact]
  public void TryReadInt32RejectsOtherValues() {
    WaitForImageConfigMapper.TryReadInt32(long.MaxValue, out _).Should().BeFalse();
    WaitForImageConfigMapper.TryReadInt32(Json("1.5"), out _).Should().BeFalse();
    WaitForImageConfigMapper.TryReadInt32(Json("\"x\""), out _).Should().BeFalse();
    WaitForImageConfigMapper.TryReadInt32("{{n}}", out _).Should().BeFalse();
    WaitForImageConfigMapper.TryReadInt32(null, out _).Should().BeFalse();
  }
}
