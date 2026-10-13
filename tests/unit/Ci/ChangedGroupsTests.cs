using FluentAssertions;
using Xunit;

namespace GameBot.UnitTests.Ci;

public class ChangedGroupsTests {
  private static (bool Dotnet, bool Webui, string Reason) Classify(params string[] files) {
    var lines = CiScriptRunner.Run("get-changed-groups.ps1", ["-Files", string.Join(",", files)]);
    string Value(string key) => lines.Single(l => l.StartsWith(key + "=", StringComparison.Ordinal))[(key.Length + 1)..];
    return (bool.Parse(Value("dotnet")), bool.Parse(Value("webui")), Value("reason"));
  }

  [Fact]
  public void DocsOnlyPushRunsNoJob() {
    var result = Classify("docs/ci-time.md", "README.md");
    result.Dotnet.Should().BeFalse();
    result.Webui.Should().BeFalse();
  }

  [Fact]
  public void SpecOnlyPushRunsNoJob() {
    var result = Classify("specs/134-ci-duration-reduction/tasks.md", "specs/STATUS.md");
    result.Dotnet.Should().BeFalse();
    result.Webui.Should().BeFalse();
  }

  [Fact]
  public void WebUiOnlyPushRunsOnlyTheWebUiJob() {
    var result = Classify("src/web-ui/src/App.tsx");
    result.Dotnet.Should().BeFalse();
    result.Webui.Should().BeTrue();
  }

  [Fact]
  public void BackendOnlyPushRunsOnlyTheDotnetJobs() {
    var result = Classify("src/GameBot.Service/Program.cs", "tests/unit/NmsTests.cs");
    result.Dotnet.Should().BeTrue();
    result.Webui.Should().BeFalse();
  }

  [Fact]
  public void WebUiPlusBackendPushRunsBoth() {
    var result = Classify("src/web-ui/src/App.tsx", "src/GameBot.Domain/Foo.cs");
    result.Dotnet.Should().BeTrue();
    result.Webui.Should().BeTrue();
  }

  [Fact]
  public void DocsFileDoesNotHideACodeFile() {
    var result = Classify("docs/ci-time.md", "src/GameBot.Domain/Foo.cs");
    result.Dotnet.Should().BeTrue();
    result.Webui.Should().BeFalse();
  }

  [Theory]
  [InlineData(".github/workflows/dotnet.yml")]
  [InlineData("scripts/check-coverage-thresholds.ps1")]
  [InlineData("scripts/ci/get-changed-groups.ps1")]
  [InlineData("Directory.Build.props")]
  [InlineData("GameBot.sln")]
  [InlineData("global.json")]
  [InlineData("src/GameBot.Domain/GameBot.Domain.csproj")]
  [InlineData("src/web-ui/package-lock.json")]
  [InlineData("some/unknown/file.bin")]
  [InlineData("Dockerfile")]
  public void SharedAndUnknownPathsRunEveryJob(string path) {
    var result = Classify(path);
    result.Dotnet.Should().BeTrue();
    result.Webui.Should().BeTrue();
  }

  [Fact]
  public void EmptyFileListRunsEveryJob() {
    var lines = CiScriptRunner.Run("get-changed-groups.ps1");
    lines.Should().Contain("dotnet=true");
    lines.Should().Contain("webui=true");
  }

  [Fact]
  public void UnknownBaseCommitRunsEveryJobAndExitsWithZero() {
    // After a force push, the old commit does not exist, so "git diff" fails. The script must not fail.
    var (exitCode, lines, error) = CiScriptRunner.RunWithExitCode(
      "get-changed-groups.ps1", "-Base", "1111111111111111111111111111111111111111", "-Head", "HEAD");

    exitCode.Should().Be(0, because: error);
    lines.Should().Contain("dotnet=true");
    lines.Should().Contain("webui=true");
  }

  [Fact]
  public void ReasonNamesTheGroupsThatWereFound() {
    var result = Classify("docs/ci-time.md", "src/web-ui/src/App.tsx");
    result.Reason.Should().Contain("docs").And.Contain("webui");
  }
}
