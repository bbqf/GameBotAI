using System.Diagnostics;
using System.Text;
using FluentAssertions;
using Xunit;

namespace GameBot.IntegrationTests.Installer;

/// <summary>
/// Runs the real installer script <c>PortResolver.jscript</c> with <c>cscript.exe</c> against a temporary
/// folder (feature 132, FR-019). Each test calls one core function that takes plain arguments.
/// </summary>
public sealed class PersistedNetworkScriptTests : IDisposable {
  private static readonly string CscriptPath = Path.Combine(
    Environment.GetFolderPath(Environment.SpecialFolder.Windows), "System32", "cscript.exe");

  private static readonly string[] LineBreaks = { "\r\n", "\n" };

  private readonly string _root =Path.Combine(Path.GetTempPath(), "gb-script-" + Guid.NewGuid().ToString("N"));
  private readonly string _dataPath;
  private readonly string _configDir;
  private readonly string _file;

  public PersistedNetworkScriptTests() {
    Directory.CreateDirectory(_root);
    _dataPath = Path.Combine(_root, "data") + "\\";
    _configDir = Path.Combine(_dataPath, "config");
    _file = Path.Combine(_configDir, "network.json");
  }

  public void Dispose() {
    try { Directory.Delete(_root, recursive: true); } catch (IOException) { }
    GC.SuppressFinalize(this);
  }

  private static bool CanRun => OperatingSystem.IsWindows() && File.Exists(CscriptPath);

  private static string JsString(string value) =>
    "\"" + value.Replace("\\", "\\\\", StringComparison.Ordinal).Replace("\"", "\\\"", StringComparison.Ordinal) + "\"";

  private static string ScriptPath() {
    var dir = new DirectoryInfo(AppContext.BaseDirectory);
    while (dir is not null) {
      if (File.Exists(Path.Combine(dir.FullName, "GameBot.sln"))) {
        return Path.Combine(dir.FullName, "installer", "wix", "Scripts", "PortResolver.jscript");
      }

      dir = dir.Parent;
    }

    throw new DirectoryNotFoundException("Unable to locate repository root containing GameBot.sln");
  }

  /// <summary>Loads the script, runs the snippet, and returns the output lines.</summary>
  private List<string> Run(string snippet) {
    var harness = Path.Combine(_root, "harness-" + Guid.NewGuid().ToString("N") + ".js");
    var text = new StringBuilder();
    text.AppendLine("var fso = new ActiveXObject(\"Scripting.FileSystemObject\");");
    text.AppendLine("var stream = fso.OpenTextFile(" + JsString(ScriptPath()) + ", 1);");
    text.AppendLine("var source = stream.ReadAll();");
    text.AppendLine("stream.Close();");
    text.AppendLine("eval(source);");
    text.AppendLine("function show(name, value) { WScript.Echo(name + \"=\" + (value === null ? \"<null>\" : value)); }");
    text.AppendLine("function showProblems(list) { show(\"problems\", list.length); for (var i = 0; i < list.length; i++) { show(\"problem\", list[i]); } }");
    text.AppendLine(snippet);
    File.WriteAllText(harness, text.ToString(), Encoding.ASCII);

    var info = new ProcessStartInfo(CscriptPath, "//nologo \"" + harness + "\"") {
      RedirectStandardOutput = true,
      RedirectStandardError = true,
      UseShellExecute = false,
      CreateNoWindow = true
    };
    using var process = Process.Start(info)!;
    var output = process.StandardOutput.ReadToEnd();
    var error = process.StandardError.ReadToEnd();
    process.WaitForExit();
    process.ExitCode.Should().Be(0, "cscript must run the harness. Output: " + output + error);
    return output.Split(LineBreaks, StringSplitOptions.RemoveEmptyEntries).ToList();
  }

  private static string Value(List<string> lines, string name) {
    var prefix = name + "=";
    var line = lines.FirstOrDefault(l => l.StartsWith(prefix, StringComparison.Ordinal));
    line.Should().NotBeNull("output must have a '" + name + "' line. Output: " + string.Join(" / ", lines));
    return line![prefix.Length..];
  }

  private void WriteNetworkFile(string content) {
    Directory.CreateDirectory(_configDir);
    File.WriteAllText(_file, content, new UTF8Encoding(false));
  }

  private List<string> Read() => Run(
    "var r = ReadNetworkFileCore(" + JsString(_dataPath) + "); show(\"host\", r.host); show(\"port\", r.port); showProblems(r.problems);");

  private List<string> Write(string host, string port) => Run(
    "var r = WriteNetworkFileCore(" + JsString(_dataPath) + ", " + JsString(host) + ", " + JsString(port) + "); show(\"status\", r.status); showProblems(r.problems);");

  private List<string> Rollback() => Run("RollbackNetworkFileCore(" + JsString(_dataPath) + "); show(\"done\", 1);");

  private List<string> Commit() => Run("CommitNetworkFileCore(" + JsString(_dataPath) + "); show(\"done\", 1);");

  // ---- Read ----

  [Fact]
  public void ReadWithNoFileGivesNoValueAndNoProblem() {
    if (!CanRun) { return; }

    var lines = Read();

    Value(lines, "host").Should().Be("<null>");
    Value(lines, "port").Should().Be("<null>");
    Value(lines, "problems").Should().Be("0");
  }

  [Fact]
  public void ReadWithBadJsonGivesNoValueAndOneProblem() {
    if (!CanRun) { return; }
    WriteNetworkFile("{ not json");

    var lines = Read();

    Value(lines, "host").Should().Be("<null>");
    Value(lines, "port").Should().Be("<null>");
    Value(lines, "problems").Should().Be("1");
  }

  [Fact]
  public void ReadWithBadPortKeepsTheGoodHost() {
    if (!CanRun) { return; }
    WriteNetworkFile("{\"bindHost\":\"0.0.0.0\",\"port\":\"abc\"}");

    var lines = Read();

    Value(lines, "host").Should().Be("0.0.0.0");
    Value(lines, "port").Should().Be("<null>");
    Value(lines, "problems").Should().Be("1");
  }

  [Fact]
  public void ReadAcceptsAPortWrittenAsANumber() {
    if (!CanRun) { return; }
    WriteNetworkFile("{\"bindHost\":\"10.0.0.5\",\"port\":9091}");

    var lines = Read();

    Value(lines, "host").Should().Be("10.0.0.5");
    Value(lines, "port").Should().Be("9091");
    Value(lines, "problems").Should().Be("0");
  }

  [Fact]
  public void ReadIgnoresUnknownFields() {
    if (!CanRun) { return; }
    WriteNetworkFile("{\"extra\":{\"a\":1},\"bindHost\":\"0.0.0.0\",\"port\":\"9090\",\"protocol\":\"http\"}");

    var lines = Read();

    Value(lines, "host").Should().Be("0.0.0.0");
    Value(lines, "port").Should().Be("9090");
    Value(lines, "problems").Should().Be("0");
  }

  [Theory]
  [InlineData("0")]
  [InlineData("65536")]
  [InlineData("-1")]
  [InlineData("80x")]
  public void ReadRejectsPortsOutOfRange(string port) {
    if (!CanRun) { return; }
    WriteNetworkFile("{\"port\":\"" + port + "\"}");

    var lines = Read();

    Value(lines, "port").Should().Be("<null>");
    Value(lines, "problems").Should().Be("1");
  }

  [Theory]
  [InlineData("a b")]
  [InlineData("a/b")]
  [InlineData("a:b")]
  public void ReadRejectsBadHosts(string host) {
    if (!CanRun) { return; }
    WriteNetworkFile("{\"bindHost\":\"" + host + "\"}");

    var lines = Read();

    Value(lines, "host").Should().Be("<null>");
    Value(lines, "problems").Should().Be("1");
  }

  // ---- Write ----

  [Fact]
  public void WriteCreatesTheMissingConfigFolder() {
    if (!CanRun) { return; }

    Write("0.0.0.0", "9090");

    File.Exists(_file).Should().BeTrue();
    var text = File.ReadAllText(_file);
    text.Should().Contain("\"bindHost\": \"0.0.0.0\"");
    text.Should().Contain("\"port\": \"9090\"");
    Read().Should().Contain("host=0.0.0.0").And.Contain("port=9090");
  }

  [Fact]
  public void WriteHasNoByteOrderMarkAndLeavesNoTemporaryFile() {
    if (!CanRun) { return; }

    Write("0.0.0.0", "9090");

    var bytes = File.ReadAllBytes(_file);
    bytes.Take(3).Should().NotEqual(new byte[] { 0xEF, 0xBB, 0xBF });
    bytes[0].Should().Be((byte)'{');
    File.Exists(_file + ".tmp").Should().BeFalse();
  }

  [Fact]
  public void WriteReplacesAnOldFile() {
    if (!CanRun) { return; }
    WriteNetworkFile("{\"bindHost\":\"127.0.0.1\",\"port\":\"8080\"}");

    Write("0.0.0.0", "9090");

    var lines = Read();
    Value(lines, "host").Should().Be("0.0.0.0");
    Value(lines, "port").Should().Be("9090");
  }

  [Fact]
  public void WriteWithOneInvalidFieldKeepsTheOldValueOfThatField() {
    if (!CanRun) { return; }
    WriteNetworkFile("{\"bindHost\":\"127.0.0.1\",\"port\":\"8080\"}");

    Write("0.0.0.0", "abc");

    var lines = Read();
    Value(lines, "host").Should().Be("0.0.0.0");
    Value(lines, "port").Should().Be("8080");
  }

  [Fact]
  public void WriteWithNoValidFieldAndNoOldFileWritesNothing() {
    if (!CanRun) { return; }

    var lines = Write("a b", "abc");

    File.Exists(_file).Should().BeFalse();
    Value(lines, "status").Should().Be("skipped");
  }

  [Fact]
  public void WriteRecoversABackupLeftByAHardStop() {
    if (!CanRun) { return; }
    Directory.CreateDirectory(_configDir);
    File.WriteAllText(_file + ".bak", "{\"bindHost\":\"10.1.1.1\",\"port\":\"7000\"}");
    File.WriteAllText(_file, "{\"bindHost\":\"broken\"");
    File.WriteAllText(_file + ".tmp", "partial");

    Write("0.0.0.0", "bad");

    var lines = Read();
    Value(lines, "host").Should().Be("0.0.0.0");
    Value(lines, "port").Should().Be("7000");
  }

  [Fact]
  public void WriteLeavesTheUndoMarkerForRollback() {
    if (!CanRun) { return; }
    WriteNetworkFile("{\"bindHost\":\"127.0.0.1\",\"port\":\"8080\"}");

    Write("0.0.0.0", "9090");

    File.Exists(_file + ".bak").Should().BeTrue();
    File.ReadAllText(_file + ".bak").Should().Contain("8080");
  }

  [Fact]
  public void WriteWithNoOldFileLeavesTheNoneMarker() {
    if (!CanRun) { return; }

    Write("0.0.0.0", "9090");

    File.Exists(_file + ".none").Should().BeTrue();
    File.Exists(_file + ".bak").Should().BeFalse();
  }

  // ---- Rollback ----

  [Fact]
  public void RollbackRestoresTheOldFile() {
    if (!CanRun) { return; }
    WriteNetworkFile("{\"bindHost\":\"127.0.0.1\",\"port\":\"8080\"}");
    Write("0.0.0.0", "9090");

    Rollback();

    var lines = Read();
    Value(lines, "host").Should().Be("127.0.0.1");
    Value(lines, "port").Should().Be("8080");
    AssertNoTemporaryFiles();
  }

  [Fact]
  public void RollbackAfterNoFileRemovesTheNewFile() {
    if (!CanRun) { return; }
    Write("0.0.0.0", "9090");
    File.Exists(_file).Should().BeTrue();

    Rollback();

    File.Exists(_file).Should().BeFalse();
    AssertNoTemporaryFiles();
  }

  [Fact]
  public void RollbackAfterAPartialWriteRestoresTheOldFile() {
    if (!CanRun) { return; }
    Directory.CreateDirectory(_configDir);
    File.WriteAllText(_file + ".bak", "{\"bindHost\":\"127.0.0.1\",\"port\":\"8080\"}");
    File.WriteAllText(_file + ".tmp", "{\"bindHost\":\"0.0.0.0\",\"port\":\"9090\"}");

    Rollback();

    var lines = Read();
    Value(lines, "port").Should().Be("8080");
    AssertNoTemporaryFiles();
  }

  [Fact]
  public void RollbackWithNoMarkerDoesNotChangeTheFile() {
    if (!CanRun) { return; }
    WriteNetworkFile("{\"bindHost\":\"0.0.0.0\",\"port\":\"9090\"}");
    File.WriteAllText(_file + ".tmp", "partial");

    Rollback();

    var lines = Read();
    Value(lines, "port").Should().Be("9090");
    AssertNoTemporaryFiles();
  }

  [Fact]
  public void RollbackTwiceIsSafe() {
    if (!CanRun) { return; }
    WriteNetworkFile("{\"bindHost\":\"127.0.0.1\",\"port\":\"8080\"}");
    Write("0.0.0.0", "9090");

    Rollback();
    Rollback();

    Value(Read(), "port").Should().Be("8080");
    AssertNoTemporaryFiles();
  }

  [Fact]
  public void RollbackWithAnEmptyDataPathDoesNothing() {
    if (!CanRun) { return; }

    var lines = Run("RollbackNetworkFileCore(\"\"); show(\"done\", 1);");

    Value(lines, "done").Should().Be("1");
  }

  // ---- Commit and data split ----

  [Fact]
  public void CommitRemovesTemporaryFilesAndKeepsTheFile() {
    if (!CanRun) { return; }
    WriteNetworkFile("{\"bindHost\":\"127.0.0.1\",\"port\":\"8080\"}");
    Write("0.0.0.0", "9090");
    File.WriteAllText(_file + ".none", "");
    File.WriteAllText(_file + ".tmp", "x");

    Commit();

    File.Exists(_file).Should().BeTrue();
    Value(Read(), "port").Should().Be("9090");
    AssertNoTemporaryFiles();
  }

  [Fact]
  public void CommitWithAnEmptyDataPathDoesNothing() {
    if (!CanRun) { return; }
    WriteNetworkFile("{\"bindHost\":\"0.0.0.0\",\"port\":\"9090\"}");

    var lines = Run("CommitNetworkFileCore(\"\"); show(\"done\", 1);");

    Value(lines, "done").Should().Be("1");
    File.Exists(_file).Should().BeTrue();
  }

  [Theory]
  [InlineData("")]
  [InlineData("only-one-part")]
  [InlineData("a|b")]
  [InlineData("a|b|c|d")]
  public void WriteDataWithAWrongPartCountDoesNothing(string data) {
    ArgumentNullException.ThrowIfNull(data);
    if (!CanRun) { return; }

    var lines = Run("show(\"result\", SplitWriteData(" + JsString(data) + ") === null ? \"none\" : \"parts\");");

    Value(lines, "result").Should().Be("none");
  }

  [Fact]
  public void WriteDataWithThreePartsIsSplit() {
    if (!CanRun) { return; }

    var lines = Run("var p = SplitWriteData(" + JsString(_dataPath + "|0.0.0.0|9090") + "); show(\"path\", p.dataPath); show(\"host\", p.host); show(\"port\", p.port);");

    Value(lines, "path").Should().Be(_dataPath);
    Value(lines, "host").Should().Be("0.0.0.0");
    Value(lines, "port").Should().Be("9090");
  }

  // ---- Later value rules (feature 132, scenario 2) ----

  [Fact]
  public void SecondWriteReplacesTheFirstValue() {
    if (!CanRun) { return; }

    Write("0.0.0.0", "9090");
    Commit();
    Write("0.0.0.0", "9191");
    Commit();

    Value(Read(), "port").Should().Be("9191");
    Write("10.0.0.7", "bad");
    Commit();
    var lines = Read();
    Value(lines, "port").Should().Be("9191");
    Value(lines, "host").Should().Be("10.0.0.7");
  }

  private void AssertNoTemporaryFiles() {
    File.Exists(_file + ".tmp").Should().BeFalse();
    File.Exists(_file + ".bak").Should().BeFalse();
    File.Exists(_file + ".none").Should().BeFalse();
  }
}
