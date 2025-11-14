using System.Text.Json;
using FluentAssertions;
using GameBot.Service.Models;
using GameBot.Service.Services;
using Xunit;

namespace GameBot.UnitTests;

public class ConfigMaskingAndMergeTests
{
    [Fact]
    public async Task SecretKeysAreMasked()
    {
        var tmp = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tmp);
        try
        {
            Environment.SetEnvironmentVariable("TEST_SECRET_KEY", "value123");
            using var svc = new ConfigSnapshotService(tmp);
            var snap = await svc.RefreshAsync();

            snap.Parameters.Should().ContainKey("TEST_SECRET_KEY");
            var p = snap.Parameters["TEST_SECRET_KEY"];
            p.IsSecret.Should().BeTrue();
            p.Value.Should().Be("***");
        }
        finally
        {
            Environment.SetEnvironmentVariable("TEST_SECRET_KEY", null);
            try { Directory.Delete(tmp, recursive: true); } catch { }
        }
    }

    [Fact]
    public async Task EnvOverridesSavedFileValues()
    {
        var tmp = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        var cfgDir = Path.Combine(tmp, "config");
        Directory.CreateDirectory(cfgDir);
        var cfgFile = Path.Combine(cfgDir, "config.json");
        await File.WriteAllTextAsync(cfgFile, "{\n  \"parameters\": { \n    \"FOO\": { \"value\": \"file\" } \n  }\n}");

        try
        {
            Environment.SetEnvironmentVariable("FOO", "env");
            using var svc = new ConfigSnapshotService(tmp);
            var snap = await svc.RefreshAsync();

            snap.Parameters.Should().ContainKey("FOO");
            var p = snap.Parameters["FOO"];
            p.Source.Should().Be("Environment");
            p.Value.Should().Be("env");
        }
        finally
        {
            Environment.SetEnvironmentVariable("FOO", null);
            try { Directory.Delete(tmp, recursive: true); } catch { }
        }
    }

    [Fact]
    public async Task InvalidSavedConfigIsIgnoredAndDoesNotCrash()
    {
        var tmp = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        var cfgDir = Path.Combine(tmp, "config");
        Directory.CreateDirectory(cfgDir);
        var cfgFile = Path.Combine(cfgDir, "config.json");
        await File.WriteAllTextAsync(cfgFile, "{ not json ");

        using var svc = new ConfigSnapshotService(tmp);
        var snap = await svc.RefreshAsync();
        snap.Should().NotBeNull();
        // No specific parameter assertion; just verify it handled gracefully
    }
}
