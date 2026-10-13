using FluentAssertions;
using GameBot.Emulator.Adb;
using Xunit;

namespace GameBot.UnitTests;

public sealed class LdInstanceMapperTests {
  [Theory]
  [InlineData("emulator-5554", 0)]
  [InlineData("emulator-5556", 1)]
  [InlineData("emulator-5558", 2)]
  [InlineData("EMULATOR-5560", 3)]
  [InlineData(" emulator-5558 ", 2)]
  public void ValidSerialGivesTheInstanceIndex(string serial, int expected) {
    LdInstanceMapper.TryGetIndex(serial, out var index, out var reason).Should().BeTrue();
    index.Should().Be(expected);
    reason.Should().BeNull();
  }

  [Theory]
  [InlineData(null)]
  [InlineData("")]
  [InlineData("   ")]
  public void NoSerialIsReportedAsNoDeviceSerial(string? serial) {
    LdInstanceMapper.TryGetIndex(serial, out var index, out var reason).Should().BeFalse();
    index.Should().Be(-1);
    reason.Should().Be(LdInstanceMapper.NoDeviceSerial);
  }

  [Theory]
  [InlineData("192.168.1.5:5555")]
  [InlineData("R58M123ABC")]
  [InlineData("emulator-5555")]
  [InlineData("emulator-5552")]
  [InlineData("emulator-abc")]
  [InlineData("emulator--5558")]
  [InlineData("emulator-")]
  public void UnsupportedSerialIsReportedAsUnsupported(string serial) {
    LdInstanceMapper.TryGetIndex(serial, out _, out var reason).Should().BeFalse();
    reason.Should().Be(LdInstanceMapper.UnsupportedSerial);
  }

  [Fact]
  public void TwoSerialsGiveTwoIndexesAndNeverShareState() {
    LdInstanceMapper.TryGetIndex("emulator-5558", out var a, out _).Should().BeTrue();
    LdInstanceMapper.TryGetIndex("emulator-5560", out var b, out _).Should().BeTrue();
    a.Should().NotBe(b);
  }
}
