using FluentAssertions;
using GameBot.Service.Services;
using Xunit;

namespace GameBot.UnitTests.Config;

public sealed class PersistedNetworkSettingsTests : IDisposable {
  private readonly string _root = Path.Combine(Path.GetTempPath(), "gb-net-" + Guid.NewGuid().ToString("N"));

  public PersistedNetworkSettingsTests() {
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

  private static Func<string, string?> Registry(string? host, string? port) =>
    name => name == "BindHost" ? host : name == "Port" ? port : null;

  private static readonly Func<string, string?> NoRegistry = _ => null;

  [Fact]
  public void MissingFileGivesNoValueAndNoProblem() {
    var result = PersistedNetworkSettings.Read(_root, NoRegistry);

    result.BindHost.Should().BeNull();
    result.Port.Should().BeNull();
    result.Problems.Should().BeEmpty();
  }

  [Fact]
  public void FileThatIsNotJsonGivesNoValueAndOneProblem() {
    WriteFile("{ not json");

    var result = PersistedNetworkSettings.Read(_root, NoRegistry);

    result.BindHost.Should().BeNull();
    result.Port.Should().BeNull();
    result.Problems.Should().HaveCount(1);
    result.Problems[0].Should().Contain("network.json");
  }

  [Fact]
  public void PortAsStringIsAccepted() {
    WriteFile("{\"bindHost\":\"0.0.0.0\",\"port\":\"9090\"}");

    var result = PersistedNetworkSettings.Read(_root, NoRegistry);

    result.BindHost.Should().Be("0.0.0.0");
    result.Port.Should().Be(9090);
    result.Problems.Should().BeEmpty();
  }

  [Fact]
  public void PortAsNumberIsAccepted() {
    WriteFile("{\"port\":9091}");

    var result = PersistedNetworkSettings.Read(_root, NoRegistry);

    result.Port.Should().Be(9091);
    result.BindHost.Should().BeNull();
  }

  [Theory]
  [InlineData("\"abc\"")]
  [InlineData("\"0\"")]
  [InlineData("\"65536\"")]
  [InlineData("-5")]
  [InlineData("true")]
  [InlineData("\"\"")]
  public void InvalidPortIsIgnoredWithAProblem(string portJson) {
    WriteFile("{\"port\":" + portJson + "}");

    var result = PersistedNetworkSettings.Read(_root, NoRegistry);

    result.Port.Should().BeNull();
    result.Problems.Should().HaveCount(1);
    result.Problems[0].Should().Contain("port");
  }

  [Theory]
  [InlineData("")]
  [InlineData("my host")]
  [InlineData("a/b")]
  [InlineData("a:b")]
  public void InvalidHostIsIgnoredWithAProblem(string host) {
    WriteFile("{\"bindHost\":\"" + host + "\"}");

    var result = PersistedNetworkSettings.Read(_root, NoRegistry);

    result.BindHost.Should().BeNull();
    result.Problems.Should().HaveCount(1);
    result.Problems[0].Should().Contain("bindHost");
  }

  [Fact]
  public void InvalidPortKeepsTheValidHost() {
    WriteFile("{\"bindHost\":\"0.0.0.0\",\"port\":\"abc\"}");

    var result = PersistedNetworkSettings.Read(_root, NoRegistry);

    result.BindHost.Should().Be("0.0.0.0");
    result.Port.Should().BeNull();
    result.Problems.Should().HaveCount(1);
  }

  [Fact]
  public void FileValueWinsOverRegistry() {
    WriteFile("{\"bindHost\":\"0.0.0.0\",\"port\":\"9090\"}");

    var result = PersistedNetworkSettings.Read(_root, Registry("10.0.0.5", "7000"));

    result.BindHost.Should().Be("0.0.0.0");
    result.Port.Should().Be(9090);
  }

  [Fact]
  public void RegistryIsUsedWhenTheFileHasNoValue() {
    WriteFile("{\"bindHost\":\"0.0.0.0\"}");

    var result = PersistedNetworkSettings.Read(_root, Registry("10.0.0.5", "7000"));

    result.BindHost.Should().Be("0.0.0.0");
    result.Port.Should().Be(7000);
  }

  [Fact]
  public void RegistryIsUsedWhenTheFileIsMissing() {
    var result = PersistedNetworkSettings.Read(_root, Registry("10.0.0.5", "7000"));

    result.BindHost.Should().Be("10.0.0.5");
    result.Port.Should().Be(7000);
    result.Problems.Should().BeEmpty();
  }

  [Fact]
  public void RegistryIsUsedWhenTheFilePortIsInvalid() {
    WriteFile("{\"port\":\"abc\"}");

    var result = PersistedNetworkSettings.Read(_root, Registry(null, "7000"));

    result.Port.Should().Be(7000);
    result.Problems.Should().HaveCount(1);
  }

  [Fact]
  public void InvalidRegistryValueIsIgnored() {
    var result = PersistedNetworkSettings.Read(_root, Registry("a b", "99999"));

    result.BindHost.Should().BeNull();
    result.Port.Should().BeNull();
  }

  [Fact]
  public void UnknownFieldsAreIgnored() {
    WriteFile("{\"bindHost\":\"0.0.0.0\",\"port\":\"9090\",\"extra\":{\"a\":1},\"protocol\":\"http\"}");

    var result = PersistedNetworkSettings.Read(_root, NoRegistry);

    result.BindHost.Should().Be("0.0.0.0");
    result.Port.Should().Be(9090);
    result.Problems.Should().BeEmpty();
  }

  [Fact]
  public void FileThatIsNotAnObjectGivesAProblemAndNoException() {
    WriteFile("[1,2,3]");

    var act = () => PersistedNetworkSettings.Read(_root, NoRegistry);

    act.Should().NotThrow();
    act().Problems.Should().HaveCount(1);
  }

  [Fact]
  public void ThrowingRegistryReaderDoesNotThrow() {
    var act = () => PersistedNetworkSettings.Read(_root, _ => throw new InvalidOperationException("boom"));

    act.Should().NotThrow();
  }

  [Fact]
  public void NullOrMissingRootDoesNotThrow() {
    var act = () => PersistedNetworkSettings.Read(Path.Combine(_root, "does-not-exist"), NoRegistry);

    act.Should().NotThrow();
  }
}
