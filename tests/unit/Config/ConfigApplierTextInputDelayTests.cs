using FluentAssertions;
using GameBot.Domain.Config;
using GameBot.Service.Hosted;
using GameBot.Service.Models;
using GameBot.Service.Services;
using Microsoft.Extensions.Options;
using Xunit;

namespace GameBot.UnitTests.Config;

/// <summary>Feature 195: the run-time apply of <c>GAMEBOT_TEXT_INPUT_CHAR_DELAY_MS</c>.</summary>
public sealed class ConfigApplierTextInputDelayTests {
  private const string Key = "GAMEBOT_TEXT_INPUT_CHAR_DELAY_MS";

  private static ConfigurationSnapshot BuildSnapshot(object? value) {
    var parameters = new Dictionary<string, ConfigurationParameter>();
    if (value is not null) {
      parameters[Key] = new ConfigurationParameter { Name = Key, Source = "File", Value = value };
    }
    return new ConfigurationSnapshot { GeneratedAtUtc = DateTimeOffset.UtcNow, Parameters = parameters };
  }

  private static int Apply(object? value) {
    var config = new AppConfig { TextInputCharDelayMs = 777 };
    new ConfigApplier(new OptionsCache<TriggerWorkerOptions>(), config).Apply(BuildSnapshot(value));
    return config.TextInputCharDelayMs;
  }

  [Fact]
  public void AnAbsentKeyGivesTheDefault() {
    Apply(null).Should().Be(100);
  }

  [Theory]
  [InlineData("abc", 100)]
  [InlineData("-1", 100)]
  [InlineData("0", 0)]
  [InlineData("250", 250)]
  [InlineData("9999", 2000)]
  public void TheValueIsAppliedInTheRange(string value, int expected) {
    Apply(value).Should().Be(expected);
  }
}
