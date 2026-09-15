using System;
using FluentAssertions;
using GameBot.Domain.Actions;
using GameBot.Domain.Commands;
using GameBot.Domain.Commands.Notify;
using Xunit;

namespace GameBot.UnitTests.Sequences;

/// <summary>
/// Save-time validation of a <c>notify</c> action step (feature 087, User Story 3).
/// <para>
/// Validating at save time rather than run time is the whole point: a malformed alert step that
/// only reveals itself during an outage fails at exactly the moment it was supposed to help.
/// </para>
/// </summary>
public class NotifyStepValidationTests {
  private static SequenceActionPayload Payload(object? message, object? url = null) {
    var action = new SequenceActionPayload { Type = ActionTypes.Notify };
    if (message is not null) action.Parameters["message"] = message;
    if (url is not null) action.Parameters["url"] = url;
    return action;
  }

  [Fact]
  public void ValidPayload_IsAccepted() {
    var ok = NotifyPayload.TryRead(
      Payload("Unrecognised screen; BACK did not dismiss it."), out var payload, out var error);

    ok.Should().BeTrue();
    error.Should().BeNull();
    payload!.Message.Should().Be("Unrecognised screen; BACK did not dismiss it.");
    payload.Url.Should().BeNull();
  }

  [Fact]
  public void ValidPayloadWithUrl_IsAccepted() {
    var ok = NotifyPayload.TryRead(
      Payload("escalating", "https://alerts.example/hook"), out var payload, out _);

    ok.Should().BeTrue();
    payload!.Url.Should().Be("https://alerts.example/hook");
  }

  [Fact]
  public void MissingMessage_IsRejected() {
    var ok = NotifyPayload.TryRead(Payload(null), out _, out var error);

    ok.Should().BeFalse();
    error.Should().Contain("message");
  }

  [Theory]
  [InlineData("")]
  [InlineData("   ")]
  [InlineData("\t\n")]
  public void BlankMessage_IsRejected(string message) {
    var ok = NotifyPayload.TryRead(Payload(message), out _, out var error);

    ok.Should().BeFalse();
    error.Should().Contain("non-empty");
  }

  [Fact]
  public void OversizedMessage_IsRejectedNamingTheLength() {
    var tooLong = new string('x', NotifyPayload.MaxMessageLength + 240);

    var ok = NotifyPayload.TryRead(Payload(tooLong), out _, out var error);

    ok.Should().BeFalse();
    error.Should().Contain("1000");
    error.Should().Contain(tooLong.Length.ToString(System.Globalization.CultureInfo.InvariantCulture));
  }

  [Fact]
  public void MessageAtExactlyTheLimit_IsAccepted() {
    var atLimit = new string('x', NotifyPayload.MaxMessageLength);

    var ok = NotifyPayload.TryRead(Payload(atLimit), out var payload, out _);

    ok.Should().BeTrue();
    payload!.Message.Should().HaveLength(NotifyPayload.MaxMessageLength);
  }

  [Theory]
  [InlineData("localhost:9099")]
  [InlineData("/relative")]
  [InlineData("ftp://example.com")]
  public void MalformedUrl_IsRejectedNamingTheValue(string candidate) {
    var ok = NotifyPayload.TryRead(Payload("escalating", candidate), out _, out var error);

    ok.Should().BeFalse();
    error.Should().Contain("absolute http or https");
    error.Should().Contain(candidate);
  }

  [Fact]
  public void NullAction_IsRejectedRatherThanThrowing() {
    var ok = NotifyPayload.TryRead(null, out _, out var error);

    ok.Should().BeFalse();
    error.Should().NotBeNullOrWhiteSpace();
  }

  [Fact]
  public void BlankUrl_IsTreatedAsAbsent() {
    var ok = NotifyPayload.TryRead(Payload("escalating", "  "), out var payload, out _);

    ok.Should().BeTrue();
    payload!.Url.Should().BeNull();
  }

  /// <summary>
  /// Parameters deserialized from stored JSON arrive as <see cref="System.Text.Json.JsonElement"/>
  /// rather than <see cref="string"/>, so the reader must not depend on a direct string cast.
  /// </summary>
  [Fact]
  public void NonStringMessage_IsReadViaToString() {
    var element = System.Text.Json.JsonDocument.Parse("\"escalating\"").RootElement;

    var ok = NotifyPayload.TryRead(Payload(element), out var payload, out _);

    ok.Should().BeTrue();
    payload!.Message.Should().Be("escalating");
  }
}
