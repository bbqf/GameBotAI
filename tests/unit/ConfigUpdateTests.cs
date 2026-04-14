using System.Text.Json;
using FluentAssertions;
using GameBot.Service.Services;
using Xunit;

namespace GameBot.UnitTests;

public sealed class ConfigUpdateTests {
  private static readonly JsonSerializerOptions s_jsonOptions = new() {
    WriteIndented = true,
    PropertyNamingPolicy = JsonNamingPolicy.CamelCase
  };

  private static readonly string[] s_reorderKeys = ["GAMEBOT_DEBUG_DUMP_IMAGES", "GAMEBOT_TESSERACT_LANG"];
  private static readonly string[] s_singleKey = ["GAMEBOT_DEBUG_DUMP_IMAGES"];
  private static readonly string[] s_duplicateKeys = ["GAMEBOT_TESSERACT_LANG", "GAMEBOT_TESSERACT_LANG"];

  private static (string tmp, string cfgFile) CreateTempDir() {
    var tmp = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
    var cfgDir = Path.Combine(tmp, "config");
    Directory.CreateDirectory(cfgDir);
    return (tmp, Path.Combine(cfgDir, "config.json"));
  }

  private static async Task SeedConfig(string cfgFile, Dictionary<string, object?> parameters) {
    var wrapper = new Dictionary<string, object?> { ["parameters"] = parameters };
    var json = JsonSerializer.Serialize(wrapper, s_jsonOptions);
    await File.WriteAllTextAsync(cfgFile, json).ConfigureAwait(false);
  }

  [Fact]
  public async Task UpdateParametersMergesValues() {
    var (tmp, cfgFile) = CreateTempDir();
    var prevUseAdb = Environment.GetEnvironmentVariable("GAMEBOT_USE_ADB");
    Environment.SetEnvironmentVariable("GAMEBOT_USE_ADB", "false");
    try {
      await SeedConfig(cfgFile, new Dictionary<string, object?> { ["GAMEBOT_TESSERACT_LANG"] = "eng" }).ConfigureAwait(false);
      using var svc = new ConfigSnapshotService(tmp);
      var snap = await svc.UpdateParametersAsync(
        new Dictionary<string, string?> { ["GAMEBOT_TESSERACT_LANG"] = "deu" }).ConfigureAwait(false);
      snap.Parameters["GAMEBOT_TESSERACT_LANG"].Value.Should().Be("deu");
      snap.Parameters["GAMEBOT_TESSERACT_LANG"].Source.Should().Be("File");
    }
    finally {
      Environment.SetEnvironmentVariable("GAMEBOT_USE_ADB", prevUseAdb);
      try { Directory.Delete(tmp, recursive: true); } catch { }
    }
  }

  [Fact]
  public async Task UpdateParametersPromotesDefaultToFile() {
    var (tmp, _) = CreateTempDir();
    var prevUseAdb = Environment.GetEnvironmentVariable("GAMEBOT_USE_ADB");
    Environment.SetEnvironmentVariable("GAMEBOT_USE_ADB", "false");
    try {
      using var svc = new ConfigSnapshotService(tmp);
      await svc.RefreshAsync().ConfigureAwait(false);
      var snap = await svc.UpdateParametersAsync(
        new Dictionary<string, string?> { ["GAMEBOT_TESSERACT_LANG"] = "fra" }).ConfigureAwait(false);
      snap.Parameters["GAMEBOT_TESSERACT_LANG"].Source.Should().Be("File");
      snap.Parameters["GAMEBOT_TESSERACT_LANG"].Value.Should().Be("fra");
    }
    finally {
      Environment.SetEnvironmentVariable("GAMEBOT_USE_ADB", prevUseAdb);
      try { Directory.Delete(tmp, recursive: true); } catch { }
    }
  }

  [Fact]
  public async Task UpdateParametersRejectsEnvironmentSourcedKey() {
    var (tmp, _) = CreateTempDir();
    var prevKey = "GAMEBOT_UPDATE_TEST_ENV";
    var prevVal = Environment.GetEnvironmentVariable(prevKey);
    var prevUseAdb = Environment.GetEnvironmentVariable("GAMEBOT_USE_ADB");
    Environment.SetEnvironmentVariable(prevKey, "envvalue");
    Environment.SetEnvironmentVariable("GAMEBOT_USE_ADB", "false");
    try {
      using var svc = new ConfigSnapshotService(tmp);
      var act = () => svc.UpdateParametersAsync(
        new Dictionary<string, string?> { [prevKey] = "newval" });
      await act.Should().ThrowAsync<InvalidOperationException>()
        .WithMessage($"*'{prevKey}'*").ConfigureAwait(false);
    }
    finally {
      Environment.SetEnvironmentVariable(prevKey, prevVal);
      Environment.SetEnvironmentVariable("GAMEBOT_USE_ADB", prevUseAdb);
      try { Directory.Delete(tmp, recursive: true); } catch { }
    }
  }

  [Fact]
  public async Task ReorderParametersReordersKeys() {
    var (tmp, cfgFile) = CreateTempDir();
    var prevUseAdb = Environment.GetEnvironmentVariable("GAMEBOT_USE_ADB");
    Environment.SetEnvironmentVariable("GAMEBOT_USE_ADB", "false");
    try {
      await SeedConfig(cfgFile, new Dictionary<string, object?> {
        ["GAMEBOT_TESSERACT_LANG"] = "eng",
        ["GAMEBOT_DEBUG_DUMP_IMAGES"] = "false"
      }).ConfigureAwait(false);
      using var svc = new ConfigSnapshotService(tmp);
      await svc.RefreshAsync().ConfigureAwait(false);

      var snap = await svc.ReorderParametersAsync(s_reorderKeys).ConfigureAwait(false);

      var keys = snap.Parameters.Keys.ToList();
      keys.IndexOf("GAMEBOT_DEBUG_DUMP_IMAGES").Should().BeLessThan(keys.IndexOf("GAMEBOT_TESSERACT_LANG"));
    }
    finally {
      Environment.SetEnvironmentVariable("GAMEBOT_USE_ADB", prevUseAdb);
      try { Directory.Delete(tmp, recursive: true); } catch { }
    }
  }

  [Fact]
  public async Task ReorderParametersAppendsMissingKeysAtEnd() {
    var (tmp, cfgFile) = CreateTempDir();
    var prevUseAdb = Environment.GetEnvironmentVariable("GAMEBOT_USE_ADB");
    Environment.SetEnvironmentVariable("GAMEBOT_USE_ADB", "false");
    try {
      await SeedConfig(cfgFile, new Dictionary<string, object?> {
        ["GAMEBOT_TESSERACT_LANG"] = "eng",
        ["GAMEBOT_DEBUG_DUMP_IMAGES"] = "false"
      }).ConfigureAwait(false);
      using var svc = new ConfigSnapshotService(tmp);
      await svc.RefreshAsync().ConfigureAwait(false);

      var snap = await svc.ReorderParametersAsync(s_singleKey).ConfigureAwait(false);

      var keys = snap.Parameters.Keys.ToList();
      // The single requested key should come before the other seeded key
      keys.IndexOf("GAMEBOT_DEBUG_DUMP_IMAGES").Should().BeLessThan(keys.IndexOf("GAMEBOT_TESSERACT_LANG"));
      keys.Should().Contain("GAMEBOT_TESSERACT_LANG");
    }
    finally {
      Environment.SetEnvironmentVariable("GAMEBOT_USE_ADB", prevUseAdb);
      try { Directory.Delete(tmp, recursive: true); } catch { }
    }
  }

  [Fact]
  public async Task ReorderParametersDeduplicatesKeys() {
    var (tmp, cfgFile) = CreateTempDir();
    var prevUseAdb = Environment.GetEnvironmentVariable("GAMEBOT_USE_ADB");
    Environment.SetEnvironmentVariable("GAMEBOT_USE_ADB", "false");
    try {
      await SeedConfig(cfgFile, new Dictionary<string, object?> {
        ["GAMEBOT_TESSERACT_LANG"] = "eng"
      }).ConfigureAwait(false);
      using var svc = new ConfigSnapshotService(tmp);
      await svc.RefreshAsync().ConfigureAwait(false);

      var snap = await svc.ReorderParametersAsync(s_duplicateKeys).ConfigureAwait(false);

      snap.Parameters.Keys.Count(k => k == "GAMEBOT_TESSERACT_LANG").Should().Be(1);
    }
    finally {
      Environment.SetEnvironmentVariable("GAMEBOT_USE_ADB", prevUseAdb);
      try { Directory.Delete(tmp, recursive: true); } catch { }
    }
  }
}
