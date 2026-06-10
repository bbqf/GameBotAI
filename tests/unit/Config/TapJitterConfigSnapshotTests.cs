using FluentAssertions;
using GameBot.Service.Services;
using Xunit;

namespace GameBot.UnitTests.Config;

public sealed class TapJitterConfigSnapshotTests {
  [Fact]
  public async Task SnapshotContainsTapJitterRadiusWithDefaultFiveWhenUnset() {
    var tmp = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
    Directory.CreateDirectory(tmp);
    try {
      using var svc = new ConfigSnapshotService(tmp);
      var snap = await svc.RefreshAsync().ConfigureAwait(false);

      snap.Parameters.Should().ContainKey("GAMEBOT_TAP_JITTER_RADIUS_PX");
      var p = snap.Parameters["GAMEBOT_TAP_JITTER_RADIUS_PX"];
      p.Source.Should().Be("Default");
      p.Value.Should().Be(5);
    }
    finally {
      try { Directory.Delete(tmp, recursive: true); } catch { }
    }
  }

  [Fact]
  public async Task SavedFileValueForTapJitterRadiusIsReflectedWithFileSource() {
    var tmp = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
    var cfgDir = Path.Combine(tmp, "config");
    Directory.CreateDirectory(cfgDir);
    var cfgFile = Path.Combine(cfgDir, "config.json");
    await File.WriteAllTextAsync(cfgFile, "{\n  \"parameters\": { \n    \"GAMEBOT_TAP_JITTER_RADIUS_PX\": { \"value\": \"12\" } \n  }\n}").ConfigureAwait(false);

    try {
      using var svc = new ConfigSnapshotService(tmp);
      var snap = await svc.RefreshAsync().ConfigureAwait(false);

      snap.Parameters.Should().ContainKey("GAMEBOT_TAP_JITTER_RADIUS_PX");
      var p = snap.Parameters["GAMEBOT_TAP_JITTER_RADIUS_PX"];
      p.Source.Should().Be("File");
      p.Value?.ToString().Should().Be("12");
    }
    finally {
      try { Directory.Delete(tmp, recursive: true); } catch { }
    }
  }
}
