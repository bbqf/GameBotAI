using FluentAssertions;
using Xunit;

#pragma warning disable CA1707, CA1861

namespace GameBot.UnitTests.Ci;

/// <summary>
/// Feature 194: tests of scripts/ci/find-disabled-tests.ps1 (contract: specs/194-fix-disabled-tests/contracts/find-disabled-tests.md).
/// Each sample line is in a string that does not start a source line, so the guard does not report this file.
/// </summary>
public sealed class DisabledTestGuardTests : IDisposable {
  private const string Script = "find-disabled-tests.ps1";
  private const string NoFinding = "find-disabled-tests: no disabled test found.";
  private readonly string _root = Path.Combine(Path.GetTempPath(), "guard-" + Guid.NewGuid().ToString("N"));

  public DisabledTestGuardTests() {
    Directory.CreateDirectory(Path.Combine(_root, "tests"));
    Directory.CreateDirectory(Path.Combine(_root, "src", "web-ui", "src"));
    Directory.CreateDirectory(Path.Combine(_root, "src", "web-ui", "tests"));
  }

  public void Dispose() {
    if (Directory.Exists(_root)) {
      Directory.Delete(_root, recursive: true);
    }
  }

  public static TheoryData<string, string, string> Findings => new() {
    { "tests/unit/A.cs", "  [Fact(Skip = \"slow\")]", "skip" },
    { "tests/unit/A.cs", "  [Theory(Skip = \"slow\")]", "skip" },
    { "tests/unit/A.cs", "  // [Fact]", "commented" },
    { "tests/unit/A.cs", "  // [Theory(DisplayName = \"x\")]", "commented" },
    { "src/web-ui/src/a.test.ts", "  it.skip('opens', () => {", "skip" },
    { "src/web-ui/tests/e2e/a.spec.ts", "test.skip('opens', async () => {", "skip" },
    { "src/web-ui/src/a.test.tsx", "describe.skip('menu', () => {", "skip" },
    { "src/web-ui/tests/e2e/a.spec.ts", "test.describe.skip('menu', () => {", "skip" },
    { "src/web-ui/src/a.test.ts", "  xit('opens', () => {", "skip" },
    { "src/web-ui/tests/e2e/a.spec.ts", "  test.fixme('opens', async () => {", "skip" },
    { "src/web-ui/tests/e2e/a.spec.ts", "test.only('opens', async () => {", "focus" },
    { "src/web-ui/src/a.test.ts", "  fit('opens', () => {", "focus" },
    { "src/web-ui/src/a.test.ts", "  // it('opens', () => {", "commented" }
  };

  [Theory]
  [MemberData(nameof(Findings))]
  public void EachDisabledTestIsReportedWithThePathTheLineAndTheKind(string path, string line, string kind) {
    WriteFile(path, "first line", line, "last line");

    var (exitCode, lines, _) = CiScriptRunner.RunWithExitCode(Script, "-Root", _root);

    exitCode.Should().Be(1);
    lines.Should().Equal(
      $"{path}:2: {kind}: {line.Trim()}",
      "find-disabled-tests: 1 disabled test(s) found. Turn each test on, or delete it. A conditional skip needs a condition and a reason.");
  }

  public static TheoryData<string, string> Permitted => new() {
    { "src/web-ui/tests/e2e/a.spec.ts", "  test.skip(browserName !== 'chromium', 'Only Chromium has the API.');" },
    { "src/web-ui/src/a.test.ts", "  const text = 'it.skip(';" },
    { "tests/unit/A.cs", "  var line = \"[Fact(Skip = \\\"x\\\")]\";" },
    { "tests/unit/A.cs", "  // The old test used [Fact(Skip = ...)] here." },
    { "tests/unit/A.cs", "  [Fact]" },
    { "src/web-ui/src/a.test.ts", "  it('opens', () => {" }
  };

  [Theory]
  [MemberData(nameof(Permitted))]
  public void APermittedLineIsNotReported(string path, string line) {
    WriteFile(path, line);

    var (exitCode, lines, _) = CiScriptRunner.RunWithExitCode(Script, "-Root", _root);

    exitCode.Should().Be(0);
    lines.Should().Equal(NoFinding);
  }

  [Theory]
  [InlineData("tests/unit/bin/Debug/A.cs")]
  [InlineData("tests/unit/obj/A.cs")]
  [InlineData("src/web-ui/src/node_modules/lib/a.test.ts")]
  public void FilesInBuildAndPackageFoldersAreIgnored(string path) {
    WriteFile(path, path.EndsWith(".cs", StringComparison.Ordinal) ? "[Fact(Skip = \"x\")]" : "it.skip('x', () => {});");

    var (exitCode, lines, _) = CiScriptRunner.RunWithExitCode(Script, "-Root", _root);

    exitCode.Should().Be(0);
    lines.Should().Equal(NoFinding);
  }

  [Fact]
  public void FilesOutsideTheSearchedFoldersAreIgnored() {
    WriteFile("src/GameBot.Service/A.cs", "[Fact(Skip = \"x\")]");
    WriteFile("tests/unit/notes.txt", "[Fact(Skip = \"x\")]");

    var (exitCode, lines, _) = CiScriptRunner.RunWithExitCode(Script, "-Root", _root);

    exitCode.Should().Be(0);
    lines.Should().Equal(NoFinding);
  }

  [Fact]
  public void FindingsAreSortedByPathAndLine() {
    WriteFile("tests/unit/B.cs", "[Fact(Skip = \"b\")]");
    WriteFile("tests/unit/A.cs", "x", "[Fact(Skip = \"a2\")]", "[Theory(Skip = \"a3\")]");
    WriteFile("src/web-ui/src/a.test.ts", "it.only('x', () => {});");

    var (exitCode, lines, _) = CiScriptRunner.RunWithExitCode(Script, "-Root", _root);

    exitCode.Should().Be(1);
    lines.Should().Equal(
      "src/web-ui/src/a.test.ts:1: focus: it.only('x', () => {});",
      "tests/unit/A.cs:2: skip: [Fact(Skip = \"a2\")]",
      "tests/unit/A.cs:3: skip: [Theory(Skip = \"a3\")]",
      "tests/unit/B.cs:1: skip: [Fact(Skip = \"b\")]",
      "find-disabled-tests: 4 disabled test(s) found. Turn each test on, or delete it. A conditional skip needs a condition and a reason.");
  }

  [Fact]
  public void ARootThatDoesNotExistGivesExitCode2() {
    var (exitCode, _, _) = CiScriptRunner.RunWithExitCode(Script, "-Root", Path.Combine(_root, "missing"));

    exitCode.Should().Be(2);
  }

  private void WriteFile(string relativePath, params string[] lines) {
    var full = Path.Combine(_root, relativePath.Replace('/', Path.DirectorySeparatorChar));
    Directory.CreateDirectory(Path.GetDirectoryName(full)!);
    File.WriteAllLines(full, lines);
  }
}
