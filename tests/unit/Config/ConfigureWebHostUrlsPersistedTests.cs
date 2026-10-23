using System.Reflection;
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

  [Fact]
  public void DefaultsApplyWhenNothingIsSet() {
    var result = GameBotServiceSetup.ResolveListenAddress(Config(), Env(), _root);

    result.Host.Should().Be("127.0.0.1");
    result.Port.Should().Be(8080);
    result.HostSource.Should().Be("Default");
    result.PortSource.Should().Be("Default");
    result.Problems.Should().BeEmpty();
  }

  [Fact]
  public void FileBeatsTheDefault() {
    WriteFile("{\"bindHost\":\"0.0.0.0\",\"port\":\"9090\"}");

    var result = GameBotServiceSetup.ResolveListenAddress(Config(), Env(), _root);

    result.Host.Should().Be("0.0.0.0");
    result.Port.Should().Be(9090);
    result.HostSource.Should().Be("NetworkFile");
    result.PortSource.Should().Be("NetworkFile");
  }

  [Fact]
  public void EnvironmentBeatsFile() {
    WriteFile("{\"bindHost\":\"0.0.0.0\",\"port\":\"9090\"}");

    var result = GameBotServiceSetup.ResolveListenAddress(
      Config(), Env(("GAMEBOT_BIND_HOST", "192.168.1.2"), ("GAMEBOT_PORT", "7001")), _root);

    result.Host.Should().Be("192.168.1.2");
    result.Port.Should().Be(7001);
    result.HostSource.Should().Be("Environment");
    result.PortSource.Should().Be("Environment");
  }

  [Fact]
  public void ConfigurationBeatsEnvironment() {
    var result = GameBotServiceSetup.ResolveListenAddress(
      Config(("Service:Network:BindHost", "10.1.1.1"), ("Service:Network:Port", "6000")),
      Env(("GAMEBOT_BIND_HOST", "192.168.1.2"), ("GAMEBOT_PORT", "7001")),
      _root);

    result.Host.Should().Be("10.1.1.1");
    result.Port.Should().Be(6000);
    result.HostSource.Should().Be("Configuration");
    result.PortSource.Should().Be("Configuration");
  }

  [Fact]
  public void HostAndPortResolveOnTheirOwn() {
    WriteFile("{\"bindHost\":\"0.0.0.0\"}");

    var result = GameBotServiceSetup.ResolveListenAddress(Config(), Env(("GAMEBOT_PORT", "7001")), _root);

    result.Host.Should().Be("0.0.0.0");
    result.Port.Should().Be(7001);
    result.HostSource.Should().Be("NetworkFile");
    result.PortSource.Should().Be("Environment");
  }

  [Fact]
  public void ABadFilePortFallsBackToTheDefaultAndKeepsTheGoodHost() {
    WriteFile("{\"bindHost\":\"0.0.0.0\",\"port\":\"abc\"}");

    var result = GameBotServiceSetup.ResolveListenAddress(Config(), Env(), _root);

    result.Host.Should().Be("0.0.0.0");
    result.Port.Should().Be(8080);
    result.HostSource.Should().Be("NetworkFile");
    result.PortSource.Should().Be("Default");
    result.Problems.Should().HaveCount(1);
  }

  [Fact]
  public void ProblemsOfTheFileAreReturnedForTheLog() {
    WriteFile("{\"port\":\"abc\"}");

    var result = GameBotServiceSetup.ResolveListenAddress(Config(), Env(), _root);

    result.Port.Should().Be(8080);
    result.Problems.Should().HaveCount(1);
  }

  [Fact]
  public void InvalidPortFromEnvironmentFallsBackToTheDefaultWithAProblem() {
    var result = GameBotServiceSetup.ResolveListenAddress(Config(), Env(("GAMEBOT_PORT", "abc")), _root);

    result.Port.Should().Be(8080);
    result.PortSource.Should().Be("Default");
    result.Problems.Should().ContainSingle().Which.Should().Contain("GAMEBOT_PORT");
  }

  [Fact]
  public void TheBotDoesNotReadTheRegistry() {
    // The installer migrates the older registry values into network.json. The bot has no registry parameter.
    var method = typeof(GameBotServiceSetup).GetMethod(
      "ResolveListenAddress", BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Static)!;

    method.GetParameters().Select(p => p.ParameterType).Should().Equal(
      typeof(IConfiguration), typeof(Func<string, string?>), typeof(string));
  }
}
