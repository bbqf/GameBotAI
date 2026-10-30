using System.Globalization;
using FluentAssertions;
using Xunit;

namespace GameBot.UnitTests.Ci;

public sealed class ShardBalanceTests : IDisposable {
  private const string ManifestJson = """
    { "sample": { "shards": [ { "name": "s-1", "include": [ "A." ] }, { "name": "s-2", "include": [ "B." ] }, { "name": "s-3", "complement": true } ] },
      "single": { "shards": [ { "name": "only", "complement": true } ] } }
    """;

  private readonly string folder = Path.Combine(Path.GetTempPath(), "shard-balance-" + Guid.NewGuid().ToString("N"));

  public ShardBalanceTests() {
    Directory.CreateDirectory(folder);
    File.WriteAllText(Path.Combine(folder, "manifest.json"), ManifestJson);
  }

  public void Dispose() {
    Directory.Delete(folder, recursive: true);
  }

  private (int ExitCode, IReadOnlyList<string> Lines, string Error) Measure(string project, string timesJson) {
    var times = Path.Combine(folder, "times.json");
    File.WriteAllText(times, timesJson);
    return CiScriptRunner.RunWithExitCode("measure-shards.ps1", "-Project", project, "-TimesFile", times, "-Manifest", Path.Combine(folder, "manifest.json"));
  }

  [Fact]
  public void ShardsWithinTheLimitGiveStatusZeroAndNoSplitLine() {
    var result = Measure("sample", """{ "s-1": 100, "s-2": 110, "s-3": 120 }""");

    result.ExitCode.Should().Be(0, result.Error);
    result.Lines.Should().NotContain(line => line.StartsWith("Split ", StringComparison.Ordinal));
  }

  [Fact]
  public void ShardAboveTheLimitGivesStatusOneAndASplitLine() {
    var result = Measure("sample", """{ "s-1": 100, "s-2": 100, "s-3": 400 }""");

    result.ExitCode.Should().Be(1, result.Error);
    result.Lines.Should().ContainSingle(line => line.StartsWith("Split s-3:", StringComparison.Ordinal));
  }

  [Fact]
  public void ShardExactlyAtTheLimitIsNotSplit() {
    // Average 100, limit 120. A shard of 120 s is at the limit, not above it.
    var result = Measure("sample", """{ "s-1": 90, "s-2": 90, "s-3": 120 }""");

    result.ExitCode.Should().Be(0, result.Error);
  }

  [Fact]
  public void LimitParameterChangesTheAllowedExcess() {
    var times = Path.Combine(folder, "times.json");
    File.WriteAllText(times, """{ "s-1": 100, "s-2": 100, "s-3": 130 }""");

    var result = CiScriptRunner.RunWithExitCode("measure-shards.ps1", "-Project", "sample", "-TimesFile", times,
      "-Manifest", Path.Combine(folder, "manifest.json"), "-Limit", 0.1.ToString(CultureInfo.InvariantCulture));

    result.ExitCode.Should().Be(1, result.Error);
  }

  [Fact]
  public void ProjectWithOneShardGivesStatusZero() {
    var result = Measure("single", """{ "only": 500 }""");

    result.ExitCode.Should().Be(0, result.Error);
    result.Lines.Should().NotContain(line => line.StartsWith("Split ", StringComparison.Ordinal));
  }

  [Fact]
  public void UnknownProjectGivesStatusTwo() {
    var result = Measure("missing", """{ "s-1": 1 }""");

    result.ExitCode.Should().Be(2);
  }

  [Fact]
  public void ShardWithNoTimeGivesStatusTwo() {
    var result = Measure("sample", """{ "s-1": 100, "s-2": 100 }""");

    result.ExitCode.Should().Be(2);
  }
}
