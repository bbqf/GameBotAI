using FluentAssertions;
using GameBot.Domain.Triggers;
using Xunit;

namespace GameBot.UnitTests.Domain;

public sealed class TriggerDefaultsTests {
  [Fact]
  public void NewTriggerDefaultsToZeroCooldown() {
    var trigger = new Trigger { Id = "t", Type = TriggerType.Delay, Enabled = true, Params = new DelayParams { Seconds = 0 } };
    trigger.CooldownSeconds.Should().Be(0);
  }
}
