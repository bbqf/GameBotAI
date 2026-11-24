using System.Text.Json;
using FluentAssertions;
using GameBot.Service.Contracts.Queues;
using Xunit;

namespace GameBot.UnitTests.Queues;

/// <summary>
/// Feature 199 (FR-005a, research R-001): the update request tells an absent <c>deviceRecovery</c> member
/// from an explicit null.
/// </summary>
public sealed class UpdateQueueRequestDeviceRecoveryTests {
  private static readonly JsonSerializerOptions WebOptions = new(JsonSerializerDefaults.Web);

  private static UpdateQueueRequest Parse(string json) =>
    JsonSerializer.Deserialize<UpdateQueueRequest>(json, WebOptions)!;

  [Fact]
  public void AnAbsentMemberIsNotSpecified() {
    var req = Parse("{\"name\":\"Q\"}");

    req.DeviceRecoverySpecified.Should().BeFalse();
    req.DeviceRecovery.Should().BeNull();
  }

  [Fact]
  public void AnExplicitNullIsSpecifiedWithANullValue() {
    var req = Parse("{\"name\":\"Q\",\"deviceRecovery\":null}");

    req.DeviceRecoverySpecified.Should().BeTrue();
    req.DeviceRecovery.Should().BeNull();
  }

  [Fact]
  public void AnObjectIsSpecifiedWithTheObject() {
    var req = Parse("{\"name\":\"Q\",\"deviceRecovery\":{\"action\":\"reboot-instance\",\"afterMs\":120000}}");

    req.DeviceRecoverySpecified.Should().BeTrue();
    req.DeviceRecovery!.Action.Should().Be("reboot-instance");
    req.DeviceRecovery.AfterMs.Should().Be(120000);
  }
}
