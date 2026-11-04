using System.Collections.Generic;
using System.Text.Json;
using FluentAssertions;
using GameBot.Domain.Commands;
using Xunit;

#pragma warning disable CA1707, CA1861, CA1305, CA1307, CA1310

namespace GameBot.UnitTests.Gamepacks;

/// <summary>Feature 177: the shared mapper for the WaitForImage payload.</summary>
public sealed class WaitForImageConfigMapperTests {
  private const string Target = "{\"referenceImageId\":\"img\"}";

  private static object Json(string text) => JsonSerializer.Deserialize<JsonElement>(text);

  private static Dictionary<string, object?> Payload(string target, object? timeout = null, string? until = null, object? absent = null) {
    var payload = new Dictionary<string, object?> { ["detectionTarget"] = Json(target) };
    if (timeout is not null) payload["timeoutMs"] = timeout;
    if (until is not null) payload["until"] = Json("\"" + until + "\"");
    if (absent is not null) payload["absentCaptures"] = absent;
    return payload;
  }

  [Fact]
  public void DefaultsAreApplied() {
    var config = WaitForImageConfigMapper.Map("WaitForImage", Payload(Target));

    config!.TimeoutMs.Should().Be(1000);
    config.Until.Should().BeNull();
    config.AbsentCaptures.Should().BeNull();
    config.DetectionTarget!.ReferenceImageId.Should().Be("img");
    config.DetectionTarget.Confidence.Should().Be(0.8);
    config.DetectionTarget.OffsetX.Should().Be(0);
    config.DetectionTarget.OffsetY.Should().Be(0);
    config.DetectionTarget.SelectionStrategy.Should().Be(DetectionSelectionStrategy.HighestConfidence);
    config.DetectionTarget.Region.Should().BeNull();
  }

  [Fact]
  public void AllFieldsAreRead() {
    var target = "{\"referenceImageId\":\"img\",\"confidence\":0.9,\"offsetX\":3,\"offsetY\":-4,\"selectionStrategy\":\"FirstMatch\",\"region\":{\"x\":1,\"y\":2,\"width\":30,\"height\":40}}";

    var config = WaitForImageConfigMapper.Map("waitforimage", Payload(target, Json("2500"), "absent", Json("3")));

    config!.TimeoutMs.Should().Be(2500);
    config.Until.Should().Be("absent");
    config.AbsentCaptures.Should().Be(3);
    config.DetectionTarget!.Confidence.Should().Be(0.9);
    config.DetectionTarget.OffsetX.Should().Be(3);
    config.DetectionTarget.OffsetY.Should().Be(-4);
    config.DetectionTarget.SelectionStrategy.Should().Be(DetectionSelectionStrategy.FirstMatch);
    config.DetectionTarget.Region!.Width.Should().Be(30);
  }

  [Fact]
  public void AnInvalidRegionGivesNoRegionAndNoThrow() {
    var target = "{\"referenceImageId\":\"img\",\"region\":{\"x\":-1,\"y\":2,\"width\":30,\"height\":40}}";

    var config = WaitForImageConfigMapper.Map("WaitForImage", Payload(target));

    config!.DetectionTarget.Should().NotBeNull();
    config.DetectionTarget!.Region.Should().BeNull();
  }

  [Fact]
  public void AnIncompleteOrBadRegionGivesNoRegion() {
    WaitForImageConfigMapper.Map("WaitForImage", Payload("{\"referenceImageId\":\"img\",\"region\":{\"x\":1}}"))!.DetectionTarget!.Region.Should().BeNull();
    WaitForImageConfigMapper.Map("WaitForImage", Payload("{\"referenceImageId\":\"img\",\"region\":3}"))!.DetectionTarget!.Region.Should().BeNull();
  }

  [Fact]
  public void AMissingImageIdGivesNoDetectionTarget() {
    WaitForImageConfigMapper.Map("WaitForImage", Payload("{\"confidence\":0.9}"))!.DetectionTarget.Should().BeNull();
    WaitForImageConfigMapper.Map("WaitForImage", Payload("{\"referenceImageId\":\"  \"}"))!.DetectionTarget.Should().BeNull();
    WaitForImageConfigMapper.Map("WaitForImage", new Dictionary<string, object?>())!.DetectionTarget.Should().BeNull();
  }

  [Fact]
  public void AWrongActionTypeOrNullPayloadGivesNull() {
    WaitForImageConfigMapper.Map("WaitForStill", Payload(Target)).Should().BeNull();
    WaitForImageConfigMapper.Map(null, Payload(Target)).Should().BeNull();
    WaitForImageConfigMapper.Map("WaitForImage", null).Should().BeNull();
  }

  [Theory]
  [InlineData(1500)]
  [InlineData(1500L)]
  [InlineData("1500")]
  public void TimeoutAcceptsTheValueTypes(object value) =>
    WaitForImageConfigMapper.Map("WaitForImage", Payload(Target, value))!.TimeoutMs.Should().Be(1500);

  [Fact]
  public void TimeoutAcceptsAJsonElement() =>
    WaitForImageConfigMapper.Map("WaitForImage", Payload(Target, Json("1500")))!.TimeoutMs.Should().Be(1500);

  [Fact]
  public void AnUnreadableTimeoutKeepsTheDefault() =>
    WaitForImageConfigMapper.Map("WaitForImage", Payload(Target, "{{t}}"))!.TimeoutMs.Should().Be(1000);

  [Fact]
  public void UntilAcceptsAPlainString() {
    var payload = Payload(Target);
    payload["until"] = "present";
    WaitForImageConfigMapper.Map("WaitForImage", payload)!.Until.Should().Be("present");
  }
}
