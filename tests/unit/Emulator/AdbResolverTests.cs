using System.Runtime.Versioning;
using GameBot.Emulator.Adb;
using FluentAssertions;
using Xunit;

namespace GameBot.UnitTests.Emulator;

[SupportedOSPlatform("windows")]
internal class AdbResolverTests {
  [Fact]
  public void UsesEnvOverrideWhenSet() {
    var tmp = Path.Combine(Path.GetTempPath(), $"adb-{Guid.NewGuid():N}.exe");
    File.WriteAllText(tmp, string.Empty);
    try {
      Environment.SetEnvironmentVariable("GAMEBOT_ADB_PATH", tmp);
      var resolved = AdbResolver.ResolveAdbPath();
      resolved.Should().Be(tmp);
    }
    finally {
      Environment.SetEnvironmentVariable("GAMEBOT_ADB_PATH", null);
      if (File.Exists(tmp)) File.Delete(tmp);
    }
  }

  [Fact]
  public void UsesLdplayerHomeWhenPresent() {
    var dir = Path.Combine(Path.GetTempPath(), $"ldp-{Guid.NewGuid():N}");
    Directory.CreateDirectory(dir);
    var adb = Path.Combine(dir, "adb.exe");
    File.WriteAllText(adb, string.Empty);
    try {
      Environment.SetEnvironmentVariable("LDPLAYER_HOME", dir);
      var resolved = AdbResolver.ResolveAdbPath();
      resolved.Should().Be(adb);
    }
    finally {
      Environment.SetEnvironmentVariable("LDPLAYER_HOME", null);
      if (File.Exists(adb)) File.Delete(adb);
      if (Directory.Exists(dir)) Directory.Delete(dir);
    }
  }
}
