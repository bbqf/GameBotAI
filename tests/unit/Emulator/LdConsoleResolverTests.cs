using System.Runtime.Versioning;
using GameBot.Emulator.Adb;
using FluentAssertions;
using Xunit;

namespace GameBot.UnitTests.Emulator;

[SupportedOSPlatform("windows")]
public class LdConsoleResolverTests {
  [Fact]
  public void UsesEnvOverrideWhenSet() {
    var tmp = Path.Combine(Path.GetTempPath(), $"ldconsole-{Guid.NewGuid():N}.exe");
    File.WriteAllText(tmp, string.Empty);
    try {
      Environment.SetEnvironmentVariable("GAMEBOT_LDCONSOLE_PATH", tmp);
      LdConsoleResolver.ResolveLdConsolePath().Should().Be(tmp);
    }
    finally {
      Environment.SetEnvironmentVariable("GAMEBOT_LDCONSOLE_PATH", null);
      if (File.Exists(tmp)) File.Delete(tmp);
    }
  }

  [Fact]
  public void UsesLdplayerHomeWhenPresent() {
    var dir = Path.Combine(Path.GetTempPath(), $"ldp-{Guid.NewGuid():N}");
    Directory.CreateDirectory(dir);
    var ldconsole = Path.Combine(dir, "ldconsole.exe");
    File.WriteAllText(ldconsole, string.Empty);
    try {
      Environment.SetEnvironmentVariable("GAMEBOT_LDCONSOLE_PATH", null);
      Environment.SetEnvironmentVariable("LDPLAYER_HOME", dir);
      LdConsoleResolver.ResolveLdConsolePath().Should().Be(ldconsole);
    }
    finally {
      Environment.SetEnvironmentVariable("LDPLAYER_HOME", null);
      if (File.Exists(ldconsole)) File.Delete(ldconsole);
      if (Directory.Exists(dir)) Directory.Delete(dir);
    }
  }

  [Fact]
  public void FallsBackToLegacyDnconsoleName() {
    var dir = Path.Combine(Path.GetTempPath(), $"ldp-{Guid.NewGuid():N}");
    Directory.CreateDirectory(dir);
    var dnconsole = Path.Combine(dir, "dnconsole.exe");
    File.WriteAllText(dnconsole, string.Empty);
    try {
      Environment.SetEnvironmentVariable("GAMEBOT_LDCONSOLE_PATH", null);
      Environment.SetEnvironmentVariable("LDPLAYER_HOME", dir);
      LdConsoleResolver.ResolveLdConsolePath().Should().Be(dnconsole);
    }
    finally {
      Environment.SetEnvironmentVariable("LDPLAYER_HOME", null);
      if (File.Exists(dnconsole)) File.Delete(dnconsole);
      if (Directory.Exists(dir)) Directory.Delete(dir);
    }
  }
}
