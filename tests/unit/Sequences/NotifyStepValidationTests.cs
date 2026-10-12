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

  // ---- Feature 137: dedupe fields ----

  private static SequenceActionPayload Dedupe(params (string Key, object Value)[] fields) {
    var action = Payload("escalating");
    foreach (var (key, value) in fields) action.Parameters[key] = value;
    return action;
  }

  [Fact]
  public void KeyWithSince_IsAccepted() {
    var ok = NotifyPayload.TryRead(Dedupe(("dedupeKey", "  daily  "), ("since", "02:05")), out var payload, out _);

    ok.Should().BeTrue();
    payload!.DedupeKey.Should().Be("daily");
    payload.Since.Should().Be(new TimeOnly(2, 5));
    payload.Within.Should().BeNull();
    payload.HasDedupe.Should().BeTrue();
  }

  [Theory]
  [InlineData("00:00:01")]
  [InlineData("00:30:00")]
  [InlineData("24:00:00")]
  public void KeyWithWithin_IsAccepted(string within) {
    var ok = NotifyPayload.TryRead(Dedupe(("dedupeKey", "k"), ("within", within)), out var payload, out _);

    ok.Should().BeTrue();
    payload!.Within.Should().NotBeNull();
  }

  [Fact]
  public void KeyAtLimit_IsAccepted() {
    var key = new string('k', NotifyPayload.MaxDedupeKeyLength);

    NotifyPayload.TryRead(Dedupe(("dedupeKey", key), ("since", "02:05")), out _, out _).Should().BeTrue();
  }

  [Fact]
  public void NoDedupeFields_GiveNoDedupe() {
    NotifyPayload.TryRead(Payload("escalating"), out var payload, out _).Should().BeTrue();
    payload!.HasDedupe.Should().BeFalse();
  }

  [Theory]
  [InlineData("dedupeKey", "k", null, null, "'dedupeKey' requires 'since' or 'within'")]
  [InlineData("since", "02:05", null, null, "'since' and 'within' require 'dedupeKey'")]
  [InlineData("within", "00:30:00", null, null, "'since' and 'within' require 'dedupeKey'")]
  [InlineData("dedupeKey", "k", "since", "02:05", null)]
  public void MissingPartner_IsRejected(string field, string value, string? other, string? otherValue, string? expectedOrNull) {
    var fields = other is null ? new[] { (field, (object)value) } : new[] { (field, (object)value), (other, (object)otherValue!) };
    var ok = NotifyPayload.TryRead(Dedupe(fields), out _, out var error);

    if (expectedOrNull is null) {
      ok.Should().BeTrue();
      return;
    }

    ok.Should().BeFalse();
    error.Should().Contain(expectedOrNull);
  }

  [Fact]
  public void SinceAndWithinTogether_AreRejected() {
    var ok = NotifyPayload.TryRead(Dedupe(("dedupeKey", "k"), ("since", "02:05"), ("within", "00:30:00")), out _, out var error);

    ok.Should().BeFalse();
    error.Should().Contain("use either 'since' or 'within', not both");
  }

  [Fact]
  public void BlankKey_IsRejected() {
    var ok = NotifyPayload.TryRead(Dedupe(("dedupeKey", "   "), ("since", "02:05")), out _, out var error);

    ok.Should().BeFalse();
    error.Should().Contain("'dedupeKey' must not be blank");
  }

  [Fact]
  public void LongKey_IsRejected() {
    var key = new string('k', NotifyPayload.MaxDedupeKeyLength + 1);
    var ok = NotifyPayload.TryRead(Dedupe(("dedupeKey", key), ("since", "02:05")), out _, out var error);

    ok.Should().BeFalse();
    error.Should().Contain("'dedupeKey' must be at most 100 characters");
  }

  [Theory]
  [InlineData("2:05")]
  [InlineData("25:00")]
  [InlineData("02:60")]
  [InlineData("02:05:00")]
  [InlineData("noon")]
  public void BadSince_IsRejected(string since) {
    var ok = NotifyPayload.TryRead(Dedupe(("dedupeKey", "k"), ("since", since)), out _, out var error);

    ok.Should().BeFalse();
    error.Should().Contain("'since' must be a time of day in HH:mm format");
  }

  [Theory]
  [InlineData("00:30")]
  [InlineData("0:30:00")]
  [InlineData("00:61:00")]
  [InlineData("nope")]
  public void BadWithinFormat_IsRejected(string within) {
    var ok = NotifyPayload.TryRead(Dedupe(("dedupeKey", "k"), ("within", within)), out _, out var error);

    ok.Should().BeFalse();
    error.Should().Contain("'within' must be a duration in HH:mm:ss format");
  }

  [Theory]
  [InlineData("00:00:00")]
  [InlineData("24:00:01")]
  [InlineData("25:00:00")]
  public void WithinOutOfRange_IsRejected(string within) {
    var ok = NotifyPayload.TryRead(Dedupe(("dedupeKey", "k"), ("within", within)), out _, out var error);

    ok.Should().BeFalse();
    error.Should().Contain("'within' must be from 00:00:01 to 24:00:00");
  }
}
