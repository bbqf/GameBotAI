using System.Text.Json;
using FluentAssertions;
using GameBot.Service.Models;
using GameBot.Service.Services;
using Xunit;

namespace GameBot.UnitTests;

public class ConfigMaskingAndMergeTests {
  [Fact]
  public async Task SecretKeysAreMasked() {
    var tmp = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
    Directory.CreateDirectory(tmp);
    try {
      Environment.SetEnvironmentVariable("GAMEBOT_TEST_SECRET_KEY", "value123");
      using var svc = new ConfigSnapshotService(tmp);
      var snap = await svc.RefreshAsync().ConfigureAwait(false);

      snap.Parameters.Should().ContainKey("GAMEBOT_TEST_SECRET_KEY");
      var p = snap.Parameters["GAMEBOT_TEST_SECRET_KEY"];
      p.IsSecret.Should().BeTrue();
      p.Value.Should().Be("***");
    }
    finally {
      Environment.SetEnvironmentVariable("GAMEBOT_TEST_SECRET_KEY", null);
      try { Directory.Delete(tmp, recursive: true); } catch { }
    }
  }

  [Fact]
  public async Task EnvOverridesSavedFileValues() {
    var tmp = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
    var cfgDir = Path.Combine(tmp, "config");
    Directory.CreateDirectory(cfgDir);
    var cfgFile = Path.Combine(cfgDir, "config.json");
    await File.WriteAllTextAsync(cfgFile, "{\n  \"parameters\": { \n    \"GAMEBOT_FOO\": { \"value\": \"file\" } \n  }\n}").ConfigureAwait(false);

    try {
      Environment.SetEnvironmentVariable("GAMEBOT_FOO", "env");
      using var svc = new ConfigSnapshotService(tmp);
      var snap = await svc.RefreshAsync().ConfigureAwait(false);

      snap.Parameters.Should().ContainKey("GAMEBOT_FOO");
      var p = snap.Parameters["GAMEBOT_FOO"];
      p.Source.Should().Be("Environment");
      p.Value.Should().Be("env");
    }
    finally {
      Environment.SetEnvironmentVariable("GAMEBOT_FOO", null);
      try { Directory.Delete(tmp, recursive: true); } catch { }
    }
  }

  [Fact]
  public async Task InvalidSavedConfigIsIgnoredAndDoesNotCrash() {
    var tmp = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
    var cfgDir = Path.Combine(tmp, "config");
    Directory.CreateDirectory(cfgDir);
    var cfgFile = Path.Combine(cfgDir, "config.json");
    await File.WriteAllTextAsync(cfgFile, "{ not json ").ConfigureAwait(false);

    using var svc = new ConfigSnapshotService(tmp);
    var snap = await svc.RefreshAsync().ConfigureAwait(false);
    snap.Should().NotBeNull();
    // No specific parameter assertion; just verify it handled gracefully
  }
}
