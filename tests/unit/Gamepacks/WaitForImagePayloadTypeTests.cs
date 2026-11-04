using System.Linq;
using System.Text.Json;
using FluentAssertions;
using GameBot.Domain.Commands;
using GameBot.Domain.Gamepacks;
using Xunit;
using W = GameBot.UnitTests.Gamepacks.WaitForImagePackFixtures;

#pragma warning disable CA1707, CA1861, CA1305, CA1307, CA1310

namespace GameBot.UnitTests.Gamepacks;

/// <summary>Feature 177: the runtime type of a payload value after a pack file read. The mapper depends on this type.</summary>
public sealed class WaitForImagePayloadTypeTests {
  private static SequenceStep ReadOnlyStep(string stepJson) {
    var result = GamepackFileSerializer.Parse(W.PackWith("1.0.0", stepJson));
    result.File.Should().NotBeNull();
    return result.File!.Sequences.Single().Steps.Single();
  }

  [Theory]
  [InlineData(false)]
  [InlineData(true)]
  public void PayloadValuesAreJsonElements(bool storedForm) {
    var payload = W.AbsentPayload;
    var step = ReadOnlyStep(storedForm ? W.StoredStep("s", payload) : W.Step("s", payload));

    step.Action.Should().NotBeNull();
    step.WaitForImage.Should().BeNull("the pack read does not build the configuration");
    var parameters = step.Action!.Parameters;
    parameters["detectionTarget"].Should().BeOfType<JsonElement>();
    parameters["timeoutMs"].Should().BeOfType<JsonElement>();
    parameters["until"].Should().BeOfType<JsonElement>();
    parameters["absentCaptures"].Should().BeOfType<JsonElement>();
  }
}
