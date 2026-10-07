using System.Collections.Generic;
using System.Text.Json;
using FluentAssertions;
using GameBot.Domain.Commands;
using GameBot.Domain.Commands.EnsureGameRunning;
using Xunit;

// Test-code analyzer relaxations permitted by the constitution:
#pragma warning disable CA2007, CA1861, CA1859

namespace GameBot.UnitTests.Sequences;

/// <summary>Feature 129: the strict reader of the forceRestart option.</summary>
public sealed class EnsureGameRunningPayloadTests {
  private static SequenceActionPayload Payload(object? value, bool hasKey = true) {
    var payload = new SequenceActionPayload { Type = "ensure-game-running" };
    if (hasKey) payload.Parameters["forceRestart"] = value;
    return payload;
  }

  private static JsonElement Json(string text) => JsonDocument.Parse(text).RootElement.Clone();

  [Fact]
  public void TrueIsAccepted() {
    EnsureGameRunningPayload.TryRead(Payload(true), out var value, out var error).Should().BeTrue();
    value.Should().BeTrue();
    error.Should().BeNull();
  }

  [Fact]
  public void FalseIsAccepted() {
    EnsureGameRunningPayload.TryRead(Payload(false), out var value, out var error).Should().BeTrue();
    value.Should().BeFalse();
    error.Should().BeNull();
  }

  [Fact]
  public void AnAbsentKeyGivesFalse() {
    EnsureGameRunningPayload.TryRead(Payload(null, hasKey: false), out var value, out var error).Should().BeTrue();
    value.Should().BeFalse();
    error.Should().BeNull();
  }

  [Fact]
  public void ANullPayloadGivesFalse() {
    EnsureGameRunningPayload.TryRead(null, out var value, out _).Should().BeTrue();
    value.Should().BeFalse();
  }

  [Fact]
  public void AJsonElementTrueIsAccepted() {
    EnsureGameRunningPayload.TryRead(Payload(Json("true")), out var value, out _).Should().BeTrue();
    value.Should().BeTrue();
  }

  [Fact]
  public void AJsonElementFalseIsAccepted() {
    EnsureGameRunningPayload.TryRead(Payload(Json("false")), out var value, out _).Should().BeTrue();
    value.Should().BeFalse();
  }

  public static IEnumerable<object?[]> BadValues() {
    yield return new object?[] { "true" };
    yield return new object?[] { "{{x}}" };
    yield return new object?[] { 1 };
    yield return new object?[] { null };
    yield return new object?[] { new Dictionary<string, object>() };
    yield return new object?[] { System.Array.Empty<object>() };
    yield return new object?[] { Json("\"true\"") };
    yield return new object?[] { Json("1") };
    yield return new object?[] { Json("null") };
    yield return new object?[] { Json("{}") };
    yield return new object?[] { Json("[]") };
  }

  [Theory]
  [MemberData(nameof(BadValues))]
  public void ABadValueIsRejectedWithAMessageThatNamesTheOption(object? bad) {
    EnsureGameRunningPayload.TryRead(Payload(bad), out var value, out var error).Should().BeFalse();
    value.Should().BeFalse();
    error.Should().Contain("forceRestart");
  }
}
