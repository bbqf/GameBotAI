using FluentAssertions;
using GameBot.Service;
using Microsoft.Extensions.Configuration;
using Xunit;

namespace GameBot.UnitTests.Config;

public sealed class ConfigureWebHostUrlsPersistedTests : IDisposable {
  private readonly string _root = Path.Combine(Path.GetTempPath(), "gb-url-" + Guid.NewGuid().ToString("N"));

  public ConfigureWebHostUrlsPersistedTests() {
    Directory.CreateDirectory(_root);
  }

  public void Dispose() {
    try { Directory.Delete(_root, recursive: true); } catch (IOException) { }
    GC.SuppressFinalize(this);
  }

  private void WriteFile(string content) {
    var dir = Path.Combine(_root, "config");
    Directory.CreateDirectory(dir);
    File.WriteAllText(Path.Combine(dir, "network.json"), content);
  }

  private static IConfiguration Config(params (string Key, string Value)[] items) =>
    new ConfigurationBuilder()
      .AddInMemoryCollection(items.ToDictionary(i => i.Key, i => (string?)i.Value))
      .Build();

  private static Func<string, string?> Env(params (string Key, string Value)[] items) {
    var map = items.ToDictionary(i => i.Key, i => i.Value);
    return name => map.TryGetValue(name, out var value) ? value : null;
  }

  private static Func<string, string?> Registry(string? host, string? port) =>
    name => name == "BindHost" ? host : name == "Port" ? port : null;

  [Fact]
  public void DefaultsApplyWhenNothingIsSet() {
    var result = GameBotServiceSetup.ResolveListenAddress(Config(), Env(), _root, Registry(null, null));

    result.Host.Should().Be("127.0.0.1");
    result.Port.Should().Be(8080);
    result.Problems.Should().BeEmpty();
  }

  [Fact]
  public void RegistryBeatsTheDefault() {
    var result = GameBotServiceSetup.ResolveListenAddress(Config(), Env(), _root, Registry("10.0.0.5", "7000"));

    result.Host.Should().Be("10.0.0.5");
    result.Port.Should().Be(7000);
  }

  [Fact]
  public void FileBeatsRegistry() {
    WriteFile("{\"bindHost\":\"0.0.0.0\",\"port\":\"9090\"}");

    var result = GameBotServiceSetup.ResolveListenAddress(Config(), Env(), _root, Registry("10.0.0.5", "7000"));

    result.Host.Should().Be("0.0.0.0");
    result.Port.Should().Be(9090);
  }

  [Fact]
  public void EnvironmentBeatsFile() {
    WriteFile("{\"bindHost\":\"0.0.0.0\",\"port\":\"9090\"}");

    var result = GameBotServiceSetup.ResolveListenAddress(
      Config(), Env(("GAMEBOT_BIND_HOST", "192.168.1.2"), ("GAMEBOT_PORT", "7001")), _root, Registry(null, null));

    result.Host.Should().Be("192.168.1.2");
    result.Port.Should().Be(7001);
  }

  [Fact]
  public void ConfigurationBeatsEnvironment() {
    var result = GameBotServiceSetup.ResolveListenAddress(
      Config(("Service:Network:BindHost", "10.1.1.1"), ("Service:Network:Port", "6000")),
      Env(("GAMEBOT_BIND_HOST", "192.168.1.2"), ("GAMEBOT_PORT", "7001")),
      _root,
      Registry(null, null));

    result.Host.Should().Be("10.1.1.1");
    result.Port.Should().Be(6000);
  }

  [Fact]
  public void HostAndPortResolveOnTheirOwn() {
    WriteFile("{\"bindHost\":\"0.0.0.0\"}");

    var result = GameBotServiceSetup.ResolveListenAddress(
      Config(), Env(("GAMEBOT_PORT", "7001")), _root, Registry(null, "7000"));

    result.Host.Should().Be("0.0.0.0");
    result.Port.Should().Be(7001);
  }

  [Fact]
  public void HostFromFileAndPortFromRegistry() {
    WriteFile("{\"bindHost\":\"0.0.0.0\",\"port\":\"abc\"}");

    var result = GameBotServiceSetup.ResolveListenAddress(Config(), Env(), _root, Registry(null, "7000"));

    result.Host.Should().Be("0.0.0.0");
    result.Port.Should().Be(7000);
  }

  [Fact]
  public void ProblemsOfTheFileAreReturnedForTheLog() {
    WriteFile("{\"port\":\"abc\"}");

    var result = GameBotServiceSetup.ResolveListenAddress(Config(), Env(), _root, Registry(null, null));

    result.Port.Should().Be(8080);
    result.Problems.Should().HaveCount(1);
  }

  [Fact]
  public void InvalidPortFromEnvironmentFallsBackToTheDefault() {
    var result = GameBotServiceSetup.ResolveListenAddress(
      Config(), Env(("GAMEBOT_PORT", "abc")), _root, Registry(null, null));

    result.Port.Should().Be(8080);
  }
}
