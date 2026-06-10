using FluentAssertions;
using GameBot.Domain.Config;
using GameBot.Service.Hosted;
using GameBot.Service.Models;
using GameBot.Service.Services;
using Microsoft.Extensions.Options;
using Xunit;

namespace GameBot.UnitTests.Config;

public sealed class ConfigApplierJitterTests {
  private static ConfigurationSnapshot BuildSnapshot(object? jitterValue) {
    var parameters = new Dictionary<string, ConfigurationParameter>();
    if (jitterValue is not null) {
      parameters["GAMEBOT_TAP_JITTER_RADIUS_PX"] = new ConfigurationParameter {
        Name = "GAMEBOT_TAP_JITTER_RADIUS_PX",
        Source = "File",
        Value = jitterValue
      };
    }
    return new ConfigurationSnapshot {
      GeneratedAtUtc = DateTimeOffset.UtcNow,
      Parameters = parameters
    };
  }

  private static (ConfigApplier Applier, AppConfig Config) BuildApplier() {
    var config = new AppConfig();
    var applier = new ConfigApplier(new OptionsCache<TriggerWorkerOptions>(), config);
    return (applier, config);
  }

  [Fact]
  public void MissingJitterValueAppliesDefaultFive() {
    var (applier, config) = BuildApplier();
    applier.Apply(BuildSnapshot(null));
    config.TapJitterRadiusPx.Should().Be(5);
  }

  [Fact]
  public void ValidJitterValueIsApplied() {
    var (applier, config) = BuildApplier();
    applier.Apply(BuildSnapshot(12));
    config.TapJitterRadiusPx.Should().Be(12);
  }

  [Fact]
  public void ZeroJitterValueIsAppliedAsDisabled() {
    var (applier, config) = BuildApplier();
    applier.Apply(BuildSnapshot(0));
    config.TapJitterRadiusPx.Should().Be(0);
  }

  [Fact]
  public void NegativeJitterValueFallsBackToDefaultFive() {
    var (applier, config) = BuildApplier();
    applier.Apply(BuildSnapshot(-7));
    config.TapJitterRadiusPx.Should().Be(5);
  }

  [Fact]
  public void NonNumericJitterValueFallsBackToDefaultFive() {
    var (applier, config) = BuildApplier();
    applier.Apply(BuildSnapshot("not-a-number"));
    config.TapJitterRadiusPx.Should().Be(5);
  }
}
