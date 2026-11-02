using System.Linq;
using System.Text.Json;
using FluentAssertions;
using GameBot.Domain.Actions;
using GameBot.Domain.Commands;
using GameBot.Domain.WaitForStill;
using Xunit;

namespace GameBot.UnitTests.WaitForStill;

/// <summary>Feature 174: payload reading and save-time validation.</summary>
public sealed class WaitForStillPayloadTests {
  private static SequenceActionPayload FromJson(string json) {
    var action = new SequenceActionPayload { Type = ActionTypes.WaitForStill };
    foreach (var property in JsonDocument.Parse(json).RootElement.EnumerateObject()) {
      action.Parameters[property.Name] = property.Value.Clone();
    }

    return action;
  }

  [Fact]
  public void AnEmptyPayloadUsesTheDefaults() {
    WaitForStillPayload.TryRead(FromJson("{}"), out var payload, out var error).Should().BeTrue();

    error.Should().BeNull();
    payload!.Region.Should().BeNull();
    payload.StableMs.Should().Be(300);
    payload.MaxDiff.Should().Be(0.01);
    payload.TimeoutMs.Should().Be(5000);
  }

  [Fact]
  public void AFullPayloadIsRead() {
    var action = FromJson("""{"region":{"x":1,"y":2,"width":30,"height":40},"stableMs":"200","maxDiff":0.5,"timeoutMs":900}""");

    WaitForStillPayload.TryRead(action, out var payload, out _).Should().BeTrue();

    payload!.Region!.Describe().Should().Be("1,2,30,40");
    payload.StableMs.Should().Be(200);
    payload.MaxDiff.Should().Be(0.5);
    payload.TimeoutMs.Should().Be(900);
  }

  [Theory]
  [InlineData("""{"stableMs":0}""", "stableMs must be an integer of 1 or more")]
  [InlineData("""{"stableMs":1.5}""", "stableMs must be an integer of 1 or more")]
  [InlineData("""{"stableMs":"abc"}""", "stableMs must be an integer of 1 or more")]
  [InlineData("""{"timeoutMs":0}""", "timeoutMs must be an integer of 1 or more")]
  [InlineData("""{"timeoutMs":-5}""", "timeoutMs must be an integer of 1 or more")]
  [InlineData("""{"maxDiff":-0.1}""", "maxDiff must be a number from 0 to 1")]
  [InlineData("""{"maxDiff":1.1}""", "maxDiff must be a number from 0 to 1")]
  [InlineData("""{"maxDiff":"x"}""", "maxDiff must be a number from 0 to 1")]
  [InlineData("""{"stableMs":6000}""", "stableMs must not be greater than timeoutMs, so that the step can end with still")]
  [InlineData("""{"stableMs":500,"timeoutMs":400}""", "stableMs must not be greater than timeoutMs, so that the step can end with still")]
  [InlineData("""{"extra":1}""", "payload field 'extra' is not supported")]
  public void ABadValueIsRejected(string json, string message) {
    var action = FromJson(json);

    WaitForStillPayload.Validate(action).Should().Contain(message);
    WaitForStillPayload.TryRead(action, out var payload, out var error).Should().BeFalse();
    payload.Should().BeNull();
    error.Should().NotBeNullOrEmpty();
  }

  [Fact]
  public void StableMsEqualToTimeoutMsIsAllowed() {
    WaitForStillPayload.Validate(FromJson("""{"stableMs":500,"timeoutMs":500}""")).Should().BeEmpty();
  }

  [Theory]
  [InlineData("""{"region":{"y":1,"width":2,"height":3}}""", "region.x is required")]
  [InlineData("""{"region":{"x":1,"width":2,"height":3}}""", "region.y is required")]
  [InlineData("""{"region":{"x":-1,"y":1,"width":2,"height":3}}""", "region.x must be 0 or more")]
  [InlineData("""{"region":{"x":1,"y":-1,"width":2,"height":3}}""", "region.y must be 0 or more")]
  [InlineData("""{"region":{"x":1,"y":1,"width":0,"height":3}}""", "region.width must be greater than 0")]
  [InlineData("""{"region":{"x":1,"y":1,"width":2,"height":-3}}""", "region.height must be greater than 0")]
  [InlineData("""{"region":{"x":1,"y":1,"width":"w","height":3}}""", "region.width must be a whole number")]
  [InlineData("""{"region":5}""", "region must be an object with x, y, width and height")]
  public void ABadRegionIsRejected(string json, string message) {
    var errors = WaitForStillPayload.Validate(FromJson(json));

    errors.Should().Contain(message);
    errors.Distinct().Should().HaveCount(errors.Count);
  }

  [Fact]
  public void AParameterReferenceIsNotCheckedAtSaveTime() {
    var action = FromJson("""{"stableMs":"{{wait}}","maxDiff":"{{diff}}","timeoutMs":"{{limit}}","region":{"x":"{{x}}","y":0,"width":10,"height":10}}""");

    WaitForStillPayload.Validate(action).Should().BeEmpty();
    // At run time the reference is resolved before the handler reads it. An unresolved one is an error.
    WaitForStillPayload.TryRead(action, out _, out _).Should().BeFalse();
  }

  [Fact]
  public void ANullActionIsRejected() {
    WaitForStillPayload.Validate(null).Should().ContainSingle();
    WaitForStillPayload.TryRead(null, out _, out var error).Should().BeFalse();
    error.Should().Contain("missing");
  }

  [Fact]
  public void ThePayloadTypeIsInTheSequenceActionTypes() {
    SequenceActionTypes.All.Should().Contain(ActionTypes.WaitForStill);
  }
}
