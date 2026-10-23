using FluentAssertions;
using GameBot.Service.Services;
using Microsoft.Extensions.Configuration;
using Xunit;

namespace GameBot.UnitTests.Config;

public sealed class ConfigSnapshotPersistedNetworkTests : IDisposable {
  private readonly string _root = Path.Combine(Path.GetTempPath(), "gb-snap-" + Guid.NewGuid().ToString("N"));

  public ConfigSnapshotPersistedNetworkTests() {
    Directory.CreateDirectory(_root);
  }

  public void Dispose() {
    try { Directory.Delete(_root, recursive: true); } catch (IOException) { }
    GC.SuppressFinalize(this);
  }

  private void WriteNetworkFile(string content) {
    var dir = Path.Combine(_root, "config");
    Directory.CreateDirectory(dir);
    File.WriteAllText(Path.Combine(dir, "network.json"), content);
  }

  private ConfigSnapshotService Service(IConfiguration? configuration = null, Func<string, string?>? env = null) =>
    new(_root, new NoopConfigApplier(), configuration, env ?? (_ => null));

  [Fact]
  public async Task TheTwoRowsShowTheValuesOfTheNetworkFileWithTheNetworkFileSource() {
    WriteNetworkFile("{\"bindHost\":\"0.0.0.0\",\"port\":\"9090\"}");

    using var svc = Service();
    var snap = await svc.RefreshAsync().ConfigureAwait(false);

    snap.Parameters["GAMEBOT_BIND_HOST"].Value.Should().Be("0.0.0.0");
    snap.Parameters["GAMEBOT_BIND_HOST"].Source.Should().Be("NetworkFile");
    snap.Parameters["GAMEBOT_PORT"].Value.Should().Be("9090");
    snap.Parameters["GAMEBOT_PORT"].Source.Should().Be("NetworkFile");
  }

  [Fact]
  public async Task TheTwoRowsShowTheBuiltInDefaultsWithNoSavedValue() {
    using var svc = Service();
    var snap = await svc.RefreshAsync().ConfigureAwait(false);

    snap.Parameters["GAMEBOT_BIND_HOST"].Value.Should().Be("127.0.0.1");
    snap.Parameters["GAMEBOT_BIND_HOST"].Source.Should().Be("Default");
    snap.Parameters["GAMEBOT_PORT"].Value.Should().Be("8080");
    snap.Parameters["GAMEBOT_PORT"].Source.Should().Be("Default");
  }

  [Fact]
  public async Task TheTwoRowsShowTheActiveValueOfAnEnvironmentOverride() {
    WriteNetworkFile("{\"bindHost\":\"0.0.0.0\",\"port\":\"9090\"}");
    Func<string, string?> env = name => name == "GAMEBOT_PORT" ? "7001" : null;

    using var svc = Service(env: env);
    var snap = await svc.RefreshAsync().ConfigureAwait(false);

    snap.Parameters["GAMEBOT_PORT"].Value.Should().Be("7001");
    snap.Parameters["GAMEBOT_PORT"].Source.Should().Be("Environment");
    snap.Parameters["GAMEBOT_BIND_HOST"].Source.Should().Be("NetworkFile");
  }

  [Fact]
  public async Task TheTwoRowsShowTheActiveValueOfTheConfigurationAlias() {
    var configuration = new ConfigurationBuilder()
      .AddInMemoryCollection(new Dictionary<string, string?> { ["Service:Network:Port"] = "6000" })
      .Build();

    using var svc = Service(configuration);
    var snap = await svc.RefreshAsync().ConfigureAwait(false);

    snap.Parameters["GAMEBOT_PORT"].Value.Should().Be("6000");
    snap.Parameters["GAMEBOT_PORT"].Source.Should().Be("Configuration");
  }

  [Fact]
  public async Task NoRowRepeatsTheHostOrPortUnderTheAliasName() {
    using var svc = Service();
    var snap = await svc.RefreshAsync().ConfigureAwait(false);

    snap.Parameters.Keys.Should().NotContain("Service__Network__BindHost");
    snap.Parameters.Keys.Should().NotContain("Service__Network__Port");
  }

  [Theory]
  [InlineData("GAMEBOT_BIND_HOST")]
  [InlineData("GAMEBOT_PORT")]
  public async Task AnUpdateOfTheHostOrPortIsRejectedWithAMessageAboutTheInstaller(string key) {
    ArgumentNullException.ThrowIfNull(key);
    using var svc = Service();

    var act = async () => await svc.UpdateParametersAsync(new Dictionary<string, string?> { [key] = "9999" }).ConfigureAwait(false);

    (await act.Should().ThrowAsync<InvalidOperationException>().ConfigureAwait(false)).Which.Message
      .Should().Be("The host and port come from the installer. Change them with the installer or in network.json.");
  }

  [Fact]
  public async Task AStaleHostOrPortInConfigJsonHasNoEffectAndIsNotKept() {
    var dir = Path.Combine(_root, "config");
    Directory.CreateDirectory(dir);
    await File.WriteAllTextAsync(
      Path.Combine(dir, "config.json"),
      "{\"parameters\":{\"GAMEBOT_PORT\":\"9999\",\"GAMEBOT_BIND_HOST\":\"1.2.3.4\",\"Service__Network__Port\":\"9998\"}}").ConfigureAwait(false);
    WriteNetworkFile("{\"bindHost\":\"0.0.0.0\",\"port\":\"9090\"}");

    using var svc = Service();
    var snap = await svc.RefreshAsync().ConfigureAwait(false);

    snap.Parameters["GAMEBOT_PORT"].Value.Should().Be("9090");
    snap.Parameters["GAMEBOT_BIND_HOST"].Value.Should().Be("0.0.0.0");
    snap.Parameters.Keys.Should().NotContain("Service__Network__Port");
    (await File.ReadAllTextAsync(Path.Combine(dir, "config.json")).ConfigureAwait(false)).Should().NotContain("9999").And.NotContain("1.2.3.4");
  }
}
