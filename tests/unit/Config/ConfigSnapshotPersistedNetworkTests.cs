using FluentAssertions;
using GameBot.Service.Services;
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

  private static Func<string, string?> Registry(string? host, string? port) =>
    name => name == "BindHost" ? host : name == "Port" ? port : null;

  [Fact]
  public async Task DefaultDisplayValuesComeFromTheNetworkFile() {
    WriteNetworkFile("{\"bindHost\":\"0.0.0.0\",\"port\":\"9090\"}");

    using var svc = new ConfigSnapshotService(_root, new NoopConfigApplier(), Registry("10.0.0.5", "7000"));
    var snap = await svc.RefreshAsync().ConfigureAwait(false);

    snap.Parameters["GAMEBOT_BIND_HOST"].Value.Should().Be("0.0.0.0");
    snap.Parameters["GAMEBOT_PORT"].Value.Should().Be("9090");
    snap.Parameters["Service__Network__BindHost"].Value.Should().Be("0.0.0.0");
    snap.Parameters["Service__Network__Port"].Value.Should().Be("9090");
  }

  [Fact]
  public async Task DefaultDisplayValuesComeFromTheRegistryWhenThereIsNoFile() {
    using var svc = new ConfigSnapshotService(_root, new NoopConfigApplier(), Registry("10.0.0.5", "7000"));
    var snap = await svc.RefreshAsync().ConfigureAwait(false);

    snap.Parameters["GAMEBOT_BIND_HOST"].Value.Should().Be("10.0.0.5");
    snap.Parameters["GAMEBOT_PORT"].Value.Should().Be("7000");
  }

  [Fact]
  public async Task DefaultDisplayValuesAreTheBuiltInDefaultsWithNoSavedValue() {
    using var svc = new ConfigSnapshotService(_root, new NoopConfigApplier(), Registry(null, null));
    var snap = await svc.RefreshAsync().ConfigureAwait(false);

    snap.Parameters["GAMEBOT_BIND_HOST"].Value.Should().Be("127.0.0.1");
    snap.Parameters["GAMEBOT_PORT"].Value.Should().Be("8080");
  }
}
