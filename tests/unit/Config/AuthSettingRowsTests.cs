using FluentAssertions;
using GameBot.Service.Services;
using Microsoft.Extensions.Configuration;
using Xunit;

namespace GameBot.UnitTests.Config;

public sealed class AuthSettingRowsTests : IDisposable {
  private readonly string _root = Path.Combine(Path.GetTempPath(), "gamebot-authrows-" + Guid.NewGuid().ToString("N"));

  public void Dispose() {
    if (Directory.Exists(_root)) {
      Directory.Delete(_root, recursive: true);
    }
  }

  private ConfigSnapshotService Service(Dictionary<string, string?> config, Dictionary<string, string?>? env = null) {
    var configuration = new ConfigurationBuilder().AddInMemoryCollection(config).Build();
    return new ConfigSnapshotService(_root, new NoopConfigApplier(), configuration, name => env is not null && env.TryGetValue(name, out var v) ? v : null);
  }

  [Fact]
  public async Task RowsShowTheEffectiveValuesAndSources() {
    using var service = Service(
      new Dictionary<string, string?> { ["Service:Auth:Enabled"] = "true" },
      new Dictionary<string, string?> { ["GAMEBOT_AUTH_PUBLIC_ORIGIN"] = "https://bot.example.com" });

    var snapshot = await service.RefreshAsync().ConfigureAwait(false);

    snapshot.Parameters["GAMEBOT_AUTH_ENABLED"].Value.Should().Be("true");
    snapshot.Parameters["GAMEBOT_AUTH_ENABLED"].Source.Should().Be("File");
    snapshot.Parameters["GAMEBOT_AUTH_PUBLIC_ORIGIN"].Value.Should().Be("https://bot.example.com");
    snapshot.Parameters["GAMEBOT_AUTH_PUBLIC_ORIGIN"].Source.Should().Be("Environment");
    snapshot.Parameters["GAMEBOT_AUTH_TRUSTED_PROXIES"].Source.Should().Be("Default");
  }

  [Fact]
  public async Task TokenValueStaysHidden() {
    using var service = Service(
      new Dictionary<string, string?>(),
      new Dictionary<string, string?> { ["GAMEBOT_AUTH_TOKEN"] = "super-secret-token" });

    var snapshot = await service.RefreshAsync().ConfigureAwait(false);

    snapshot.Parameters["GAMEBOT_AUTH_TOKEN"].IsSecret.Should().BeTrue();
    snapshot.Parameters["GAMEBOT_AUTH_TOKEN"].Value?.ToString().Should().NotContain("super-secret-token");
  }

  [Theory]
  [InlineData("GAMEBOT_AUTH_ENABLED")]
  [InlineData("GAMEBOT_AUTH_PUBLIC_ORIGIN")]
  [InlineData("GAMEBOT_AUTH_TRUSTED_PROXIES")]
  public async Task RowsCannotBeChangedThroughTheUpdateCall(string key) {
    using var service = Service(new Dictionary<string, string?>());

    var act = () => service.UpdateParametersAsync(new Dictionary<string, string?> { [key] = "x" });

    await act.Should().ThrowAsync<InvalidOperationException>().ConfigureAwait(false);
  }
}
