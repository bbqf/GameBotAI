using GameBot.Emulator.Adb;
using FluentAssertions;
using Xunit;

namespace GameBot.UnitTests.Emulator;

public class AdbClientForegroundPackageTests {
  [Fact]
  public void ParseForegroundPackageReturnsPackageWhenMResumedActivityPresent() {
    var output = "  mResumedActivity: ActivityRecord{abc u0 com.example.game/.MainActivity t123}";
    AdbClient.ParseForegroundPackage(output).Should().Be("com.example.game");
  }

  [Fact]
  public void ParseForegroundPackageReturnsNullWhenLineAbsent() {
    var output = "some unrelated dumpsys output\nno activity info here";
    AdbClient.ParseForegroundPackage(output).Should().BeNull();
  }

  [Fact]
  public void ParseForegroundPackageReturnsNullWhenOutputEmpty() {
    AdbClient.ParseForegroundPackage(string.Empty).Should().BeNull();
  }

  [Fact]
  public void ParseForegroundPackageIgnoresNonResumedLinesAndReturnsFirstMatch() {
    var output =
      "  mLastPausedActivity: ActivityRecord{xyz u0 com.other.app/.OtherActivity t100}\n" +
      "  mResumedActivity: ActivityRecord{def u0 com.target.app/.MainActivity t200}\n" +
      "  mFocusedActivity: ActivityRecord{def u0 com.target.app/.MainActivity t200}";
    AdbClient.ParseForegroundPackage(output).Should().Be("com.target.app");
  }

  [Fact]
  public void ParseForegroundPackageIsCaseInsensitiveForKeyword() {
    var output = "  mresumedactivity: ActivityRecord{abc u0 com.example.game/.MainActivity t1}";
    AdbClient.ParseForegroundPackage(output).Should().Be("com.example.game");
  }
}
