using FluentAssertions;
using GameBot.Domain.Queues;
using GameBot.Service.Contracts.Queues;
using GameBot.Service.Endpoints;
using GameBot.Service.Services.Notifications;
using Xunit;

namespace GameBot.UnitTests.Queues;

/// <summary>
/// Validation of a queue's failure policy at the API boundary (feature 087, issue #181).
/// <para>
/// Every message is asserted to name the offending value, per Constitution III — a policy rejected
/// with "invalid" tells an operator nothing about which of three fields to fix.
/// </para>
/// </summary>
public class QueueFailurePolicyValidationTests {
  private static FailureNotificationOptions WithDefaultUrl() =>
    new FailureNotificationOptions { DefaultUrl = "http://localhost:9099/alerts" };

  private static FailureNotificationOptions WithoutDefaultUrl() => new FailureNotificationOptions();

  [Fact]
  public void NullPolicy_IsValidAndMapsToNoPolicy() {
    var ok = QueueFailurePolicyMapping.TryMap(null, WithoutDefaultUrl(), out var policy, out var error);

    ok.Should().BeTrue();
    policy.Should().BeNull();
    error.Should().BeNull();
  }

  [Theory]
  [InlineData(0)]
  [InlineData(-1)]
  [InlineData(-100)]
  public void NonPositiveThreshold_IsRejectedNamingTheValue(int threshold) {
    var dto = new QueueFailurePolicyDto { ConsecutiveFailedCycles = threshold, Action = "notify" };

    var ok = QueueFailurePolicyMapping.TryMap(dto, WithDefaultUrl(), out _, out var error);

    ok.Should().BeFalse();
    error.Should().Contain("consecutiveFailedCycles");
    error.Should().Contain("at least 1");
    error.Should().Contain(threshold.ToString(System.Globalization.CultureInfo.InvariantCulture));
  }

  [Theory]
  [InlineData("halt")]
  [InlineData("")]
  [InlineData(null)]
  [InlineData("notify_and_stop")]
  public void UnknownAction_IsRejectedListingTheAllowedValues(string? action) {
    var dto = new QueueFailurePolicyDto { ConsecutiveFailedCycles = 3, Action = action };

    var ok = QueueFailurePolicyMapping.TryMap(dto, WithDefaultUrl(), out _, out var error);

    ok.Should().BeFalse();
    error.Should().Contain("notify, stop, pause, notifyAndStop");
  }

  [Theory]
  [InlineData("notify", QueueFailureAction.Notify)]
  [InlineData("Notify", QueueFailureAction.Notify)]
  [InlineData("stop", QueueFailureAction.Stop)]
  [InlineData("pause", QueueFailureAction.Pause)]
  [InlineData("notifyAndStop", QueueFailureAction.NotifyAndStop)]
  [InlineData("NOTIFYANDSTOP", QueueFailureAction.NotifyAndStop)]
  public void KnownActions_ParseCaseInsensitively(string wire, QueueFailureAction expected) {
    var dto = new QueueFailurePolicyDto { ConsecutiveFailedCycles = 3, Action = wire };

    var ok = QueueFailurePolicyMapping.TryMap(dto, WithDefaultUrl(), out var policy, out _);

    ok.Should().BeTrue();
    policy!.Action.Should().Be(expected);
  }

  [Theory]
  [InlineData("localhost:9099")]
  [InlineData("/relative/path")]
  [InlineData("ftp://example.com/hook")]
  [InlineData("file:///C:/alerts.txt")]
  public void NonAbsoluteHttpUrl_IsRejectedNamingTheValue(string candidate) {
    var dto = new QueueFailurePolicyDto {
      ConsecutiveFailedCycles = 3, Action = "notify", NotifyUrl = candidate
    };

    var ok = QueueFailurePolicyMapping.TryMap(dto, WithDefaultUrl(), out _, out var error);

    ok.Should().BeFalse();
    error.Should().Contain("notifyUrl");
    error.Should().Contain("absolute http or https");
    error.Should().Contain(candidate);
  }

  /// <summary>
  /// FR-006. A notifying policy with no destination anywhere would look configured in the UI and
  /// never fire — which is the silent-failure mode this whole feature exists to eliminate.
  /// </summary>
  [Theory]
  [InlineData("notify")]
  [InlineData("notifyAndStop")]
  public void NotifyingActionWithNoDestinationAnywhere_IsRejected(string action) {
    var dto = new QueueFailurePolicyDto { ConsecutiveFailedCycles = 3, Action = action };

    var ok = QueueFailurePolicyMapping.TryMap(dto, WithoutDefaultUrl(), out _, out var error);

    ok.Should().BeFalse();
    error.Should().Contain("requires a destination");
    error.Should().Contain("Service:Notifications:DefaultUrl");
  }

  [Theory]
  [InlineData("stop")]
  [InlineData("pause")]
  public void NonNotifyingActionNeedsNoDestination(string action) {
    var dto = new QueueFailurePolicyDto { ConsecutiveFailedCycles = 3, Action = action };

    var ok = QueueFailurePolicyMapping.TryMap(dto, WithoutDefaultUrl(), out var policy, out var error);

    ok.Should().BeTrue();
    error.Should().BeNull();
    policy!.Notifies.Should().BeFalse();
  }

  [Fact]
  public void PolicyUrl_SatisfiesTheDestinationRuleWithoutAServiceDefault() {
    var dto = new QueueFailurePolicyDto {
      ConsecutiveFailedCycles = 3, Action = "notify", NotifyUrl = "https://alerts.example/hook"
    };

    var ok = QueueFailurePolicyMapping.TryMap(dto, WithoutDefaultUrl(), out var policy, out _);

    ok.Should().BeTrue();
    policy!.NotifyUrl.Should().Be("https://alerts.example/hook");
  }

  [Fact]
  public void BlankUrl_IsTreatedAsAbsentRatherThanMalformed() {
    var dto = new QueueFailurePolicyDto {
      ConsecutiveFailedCycles = 3, Action = "notify", NotifyUrl = "   "
    };

    var ok = QueueFailurePolicyMapping.TryMap(dto, WithDefaultUrl(), out var policy, out _);

    ok.Should().BeTrue();
    policy!.NotifyUrl.Should().BeNull();
  }

  [Fact]
  public void Projection_RoundTripsTheWireSpelling() {
    var projected = QueueFailurePolicyMapping.Project(new QueueFailurePolicy {
      ConsecutiveFailedCycles = 7,
      Action = QueueFailureAction.NotifyAndStop,
      NotifyUrl = "https://alerts.example/hook"
    });

    projected!.ConsecutiveFailedCycles.Should().Be(7);
    projected.Action.Should().Be("notifyAndStop");
    projected.NotifyUrl.Should().Be("https://alerts.example/hook");
  }

  [Fact]
  public void Projection_OfNullIsNull() {
    QueueFailurePolicyMapping.Project(null).Should().BeNull();
  }

  [Fact]
  public void Clone_ProducesAnIndependentCopy() {
    var source = new QueueFailurePolicy {
      ConsecutiveFailedCycles = 4, Action = QueueFailureAction.Pause, NotifyUrl = null
    };

    var clone = QueueFailurePolicyMapping.Clone(source);
    clone!.ConsecutiveFailedCycles = 99;

    source.ConsecutiveFailedCycles.Should().Be(4, "a duplicated queue must not share its source's policy");
  }

  [Theory]
  [InlineData(QueueFailureAction.Notify, true, false)]
  [InlineData(QueueFailureAction.Stop, false, true)]
  [InlineData(QueueFailureAction.Pause, false, false)]
  [InlineData(QueueFailureAction.NotifyAndStop, true, true)]
  public void ActionPredicates_MatchTheDocumentedEffects(
    QueueFailureAction action, bool notifies, bool stops) {
    var policy = new QueueFailurePolicy { ConsecutiveFailedCycles = 1, Action = action };

    policy.Notifies.Should().Be(notifies);
    policy.Stops.Should().Be(stops);
  }
}
