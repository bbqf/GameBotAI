using System.Diagnostics;
using System.Text;
using FluentAssertions;
using Microsoft.Win32;
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

  // ---- Resolver (feature 162, T004) ----

  private const string SampleNetstat =
    "Active Connections\r\n\r\n" +
    "  Proto  Local Address          Foreign Address        State           PID\r\n" +
    "  TCP    0.0.0.0:80             0.0.0.0:0              LISTENING       4\r\n" +
    "  TCP    127.0.0.1:8080         0.0.0.0:0              LISTENING       1234\r\n" +
    "  TCP    192.168.1.5:8088       0.0.0.0:0              LISTENING       555\r\n" +
    "  TCP    192.168.1.5:50000      10.0.0.1:8888          ESTABLISHED     999\r\n" +
    "  TCP    [::]:445               [::]:0                 LISTENING       4\r\n";

  private static string JsText(string value) =>
    JsString(value).Replace("\r", "\\r", StringComparison.Ordinal).Replace("\n", "\\n", StringComparison.Ordinal);

  private List<string> Resolve(string fields) => Run(
    "var r = ResolveBindingCore({ computerName: \"PC1\"" + (fields.Length == 0 ? "" : ", " + fields) + " });" +
    " show(\"host\", r.host); show(\"port\", r.port);" +
    " show(\"hostSource\", r.hostSource); show(\"portSource\", r.portSource);" +
    " show(\"prefillHost\", r.prefillHost); show(\"prefillPort\", r.prefillPort);" +
    " show(\"url\", r.shortcutUrl); show(\"logs\", r.log.length);" +
    " for (var i = 0; i < r.log.length; i++) { show(\"log\", r.log[i]); }");

  [Fact]
  public void ResolveWithNoSavedValueGivesTheFirstInstallDefaults() {
    if (!CanRun) { return; }

    var lines = Resolve("");

    Value(lines, "host").Should().Be("127.0.0.1");
    Value(lines, "port").Should().Be("8080");
    Value(lines, "hostSource").Should().Be("Default");
    Value(lines, "portSource").Should().Be("Default");
    Value(lines, "url").Should().Be("http://localhost:8080/");
  }

  [Theory]
  [InlineData("host: \"1.1.1.1\", fileHost: \"2.2.2.2\", legacyHost: \"3.3.3.3\"", "1.1.1.1", "Explicit")]
  [InlineData("fileHost: \"2.2.2.2\", legacyHost: \"3.3.3.3\"", "2.2.2.2", "NetworkFile")]
  [InlineData("legacyHost: \"3.3.3.3\"", "3.3.3.3", "LegacyRegistry")]
  [InlineData("", "127.0.0.1", "Default")]
  public void ResolveAppliesTheHostPriority(string fields, string expected, string source) {
    ArgumentNullException.ThrowIfNull(fields);
    if (!CanRun) { return; }

    var lines = Resolve(fields);

    Value(lines, "host").Should().Be(expected);
    Value(lines, "hostSource").Should().Be(source);
  }

  [Theory]
  [InlineData("port: \"9001\", filePort: 9002, legacyPort: 9003", "9001", "Explicit")]
  [InlineData("filePort: 9002, legacyPort: 9003", "9002", "NetworkFile")]
  [InlineData("legacyPort: 9003", "9003", "LegacyRegistry")]
  [InlineData("", "8080", "Default")]
  public void ResolveAppliesThePortPriority(string fields, string expected, string source) {
    ArgumentNullException.ThrowIfNull(fields);
    if (!CanRun) { return; }

    var lines = Resolve(fields);

    Value(lines, "port").Should().Be(expected);
    Value(lines, "portSource").Should().Be(source);
  }

  [Fact]
  public void ResolveGivesEachFieldItsOwnChain() {
    if (!CanRun) { return; }

    var lines = Resolve("fileHost: \"0.0.0.0\", legacyPort: 9003");

    Value(lines, "host").Should().Be("0.0.0.0");
    Value(lines, "hostSource").Should().Be("NetworkFile");
    Value(lines, "port").Should().Be("9003");
    Value(lines, "portSource").Should().Be("LegacyRegistry");
  }

  [Fact]
  public void ResolveIgnoresAnExplicitValueThatIsNotValid() {
    if (!CanRun) { return; }

    var lines = Resolve("host: \"a b\", port: \"abc\", fileHost: \"0.0.0.0\", filePort: 9090");

    Value(lines, "host").Should().Be("0.0.0.0");
    Value(lines, "port").Should().Be("9090");
    Value(lines, "hostSource").Should().Be("NetworkFile");
    Value(lines, "portSource").Should().Be("NetworkFile");
  }

  [Fact]
  public void ResolveTreatsAValueEqualToThePrefillAsNotExplicit() {
    if (!CanRun) { return; }

    var lines = Resolve("host: \"0.0.0.0\", prefillHost: \"0.0.0.0\", port: \"8080\", prefillPort: \"8080\", fileHost: \"10.0.0.5\", filePort: 9090");

    Value(lines, "host").Should().Be("10.0.0.5");
    Value(lines, "port").Should().Be("9090");
    Value(lines, "prefillHost").Should().Be("10.0.0.5");
    Value(lines, "prefillPort").Should().Be("9090");
  }

  [Fact]
  public void ResolveKeepsAValueThatTheUserTypedAfterThePrefill() {
    if (!CanRun) { return; }

    var first = Resolve("fileHost: \"127.0.0.1\", filePort: 9090");
    Value(first, "prefillPort").Should().Be("9090");

    var second = Resolve("host: \"0.0.0.0\", port: \"9191\", prefillHost: \"127.0.0.1\", prefillPort: \"9090\", fileHost: \"127.0.0.1\", filePort: 9090");

    Value(second, "host").Should().Be("0.0.0.0");
    Value(second, "port").Should().Be("9191");
    Value(second, "hostSource").Should().Be("Explicit");
    Value(second, "portSource").Should().Be("Explicit");
    Value(second, "prefillPort").Should().Be("9090");
  }

  [Fact]
  public void ResolveWritesALogLineForEachField() {
    if (!CanRun) { return; }

    var lines = Resolve("fileHost: \"0.0.0.0\", legacyPort: 9003");

    lines.Should().Contain("log=Binding: BIND_HOST=0.0.0.0 (source: network.json)");
    lines.Should().Contain("log=Binding: PORT=9003 (source: legacy registry, migrated)");
    lines.Should().Contain("log=Shortcut: http://PC1:9003/");
  }

  [Fact]
  public void ResolveWritesADefaultLogLine() {
    if (!CanRun) { return; }

    var lines = Resolve("");

    lines.Should().Contain("log=Binding: BIND_HOST=127.0.0.1 (source: default)");
    lines.Should().Contain("log=Binding: PORT=8080 (source: default)");
  }

  [Theory]
  [InlineData("127.0.0.1", "PC1", "localhost")]
  [InlineData("0.0.0.0", "PC1", "PC1")]
  [InlineData("192.168.1.5", "PC1", "PC1")]
  [InlineData("192.168.1.5", "", "192.168.1.5")]
  public void ShortcutHostFollowsOneRule(string bindHost, string computerName, string expected) {
    ArgumentNullException.ThrowIfNull(bindHost);
    ArgumentNullException.ThrowIfNull(computerName);
    if (!CanRun) { return; }

    var lines = Run("show(\"host\", ShortcutHostCore(" + JsString(bindHost) + ", " + JsString(computerName) + "));");

    Value(lines, "host").Should().Be(expected);
  }

  [Theory]
  [InlineData("http", "127.0.0.1", "8081", "http://localhost:8081/")]
  [InlineData("http", "0.0.0.0", "8081", "http://PC1:8081/")]
  [InlineData("https", "192.168.1.5", "8443", "https://PC1:8443/")]
  [InlineData("", "127.0.0.1", "8081", "http://localhost:8081/")]
  public void ShortcutUrlJoinsProtocolHostAndPort(string protocol, string bindHost, string port, string expected) {
    ArgumentNullException.ThrowIfNull(protocol);
    ArgumentNullException.ThrowIfNull(bindHost);
    ArgumentNullException.ThrowIfNull(port);
    if (!CanRun) { return; }

    var lines = Run("show(\"url\", BuildShortcutUrlCore(" + JsString(protocol) + ", " + JsString(bindHost) + ", " + JsString(port) + ", \"PC1\"));");

    Value(lines, "url").Should().Be(expected);
  }

  // The lookup function in these tests plays the role of WMI (Win32_Process).
  private const string LookupScript =
    "function lookup(pid) {" +
    " if (pid == 1234) { return { name: \"GameBot.Service.exe\", path: \"C:\\\\Apps\\\\GameBot\\\\GameBot.Service.exe\" }; }" +
    " if (pid == 555) { return { name: \"GameBot.Service.exe\", path: \"D:\\\\Other\\\\GameBot.Service.exe\" }; }" +
    " if (pid == 4) { return { name: \"System\", path: \"\" }; }" +
    " return null; }";

  private List<string> Validate(string host, string port) => Run(
    LookupScript +
    " var listeners = ParseNetstatListeners(" + JsText(SampleNetstat) + ");" +
    " show(\"error\", ValidateBindingCore(" + JsString(host) + ", " + JsString(port) + ", listeners, \"C:\\\\Apps\\\\GameBot\\\\\", lookup));");

  [Theory]
  [InlineData("127.0.0.1", "")]
  [InlineData("127.0.0.1", "abc")]
  [InlineData("127.0.0.1", "0")]
  [InlineData("127.0.0.1", "65536")]
  public void ValidateRejectsAPortThatIsNotAWholeNumberFrom1To65535(string host, string port) {
    ArgumentNullException.ThrowIfNull(host);
    ArgumentNullException.ThrowIfNull(port);
    if (!CanRun) { return; }

    Value(Validate(host, port), "error").Should().Be("Enter a port number from 1 to 65535.");
  }

  [Fact]
  public void ValidateAcceptsAFreePort() {
    if (!CanRun) { return; }

    Value(Validate("127.0.0.1", "18080"), "error").Should().BeEmpty();
  }

  [Fact]
  public void ValidateRejectsAPortThatAnotherProgramHolds() {
    if (!CanRun) { return; }

    Value(Validate("0.0.0.0", "80"), "error").Should().Be("Port 80 is in use by another program. Enter a different port.");
  }

  [Fact]
  public void ValidateAcceptsAPortThatTheInstalledGameBotHolds() {
    if (!CanRun) { return; }

    Value(Validate("127.0.0.1", "8080"), "error").Should().BeEmpty();
  }

  [Fact]
  public void ValidateRejectsAPortThatGameBotHoldsFromAnotherFolder() {
    if (!CanRun) { return; }

    Value(Validate("192.168.1.5", "8088"), "error").Should().Contain("in use by another program");
  }

  [Fact]
  public void ValidateRejectsAHostWithASeparator() {
    if (!CanRun) { return; }

    Value(Validate("a:b", "18080"), "error").Should().Contain("host");
  }

  // ---- New install defaults (feature 162, US2) ----

  // A listener on 127.0.0.1:8080 that the lookup cannot read, so another program holds the port.
  private const string HeldPortFields =
    "listeners: ParseNetstatListeners(" + "\"  TCP    127.0.0.1:8080         0.0.0.0:0              LISTENING       999\"" + "), lookup: function (pid) { return null; }";

  [Fact]
  public void FirstInstallChoosesTheNextPortWhenTheFirstOneIsInUse() {
    if (!CanRun) { return; }

    var lines = Resolve(HeldPortFields);

    Value(lines, "host").Should().Be("127.0.0.1");
    Value(lines, "port").Should().Be("8088");
    Value(lines, "prefillPort").Should().Be("8088");
    Value(lines, "portSource").Should().Be("Default");
  }

  [Fact]
  public void SilentFirstInstallWithOnlyAPortUsesTheDefaultHost() {
    if (!CanRun) { return; }

    var lines = Resolve("port: \"9100\", silent: true");

    Value(lines, "host").Should().Be("127.0.0.1");
    Value(lines, "hostSource").Should().Be("Default");
    Value(lines, "port").Should().Be("9100");
    Value(lines, "portSource").Should().Be("Explicit");
    Value(lines, "url").Should().Be("http://localhost:9100/");
  }

  [Fact]
  public void SilentFirstInstallWithOnlyAHostUsesTheFirstFreePort() {
    if (!CanRun) { return; }

    var lines = Resolve("host: \"0.0.0.0\", silent: true");

    Value(lines, "host").Should().Be("0.0.0.0");
    Value(lines, "hostSource").Should().Be("Explicit");
    Value(lines, "portSource").Should().Be("Default");
    Value(lines, "url").Should().StartWith("http://PC1:");
  }

  // ---- Upgrade keeps the saved binding (feature 162, US3 and US5) ----

  private List<string> ResolveSaved(string fields) => Run(
    "var r = ResolveWithSavedValuesCore(" + JsString(_dataPath) + ", \"\", { computerName: \"PC1\"" + (fields.Length == 0 ? "" : ", " + fields) + " });" +
    " show(\"host\", r.host); show(\"port\", r.port);" +
    " show(\"hostSource\", r.hostSource); show(\"portSource\", r.portSource);" +
    " show(\"url\", r.shortcutUrl);" +
    " for (var i = 0; i < r.log.length; i++) { show(\"log\", r.log[i]); }");

  [Fact]
  public void UpgradeWithNoArgumentKeepsTheSavedHostAndPort() {
    if (!CanRun) { return; }
    WriteNetworkFile("{\"bindHost\":\"0.0.0.0\",\"port\":\"9090\"}");

    var lines = ResolveSaved("");

    Value(lines, "host").Should().Be("0.0.0.0");
    Value(lines, "port").Should().Be("9090");
    Value(lines, "hostSource").Should().Be("NetworkFile");
    Value(lines, "portSource").Should().Be("NetworkFile");
    Value(lines, "url").Should().Be("http://PC1:9090/");
  }

  [Fact]
  public void UpgradeStartedWithOnlyTheFolderKeepsTheSavedBindingAndShortcutAddress() {
    if (!CanRun) { return; }
    // The web UI and the API start msiexec with APPLICATIONFOLDER only. No host and no port are given.
    WriteNetworkFile("{\"bindHost\":\"127.0.0.1\",\"port\":\"8123\"}");

    var lines = ResolveSaved("appFolder: \"C:\\\\Apps\\\\GameBot\\\\\", silent: true");

    Value(lines, "host").Should().Be("127.0.0.1");
    Value(lines, "port").Should().Be("8123");
    Value(lines, "url").Should().Be("http://localhost:8123/");
  }

  [Fact]
  public void AnExplicitPortReplacesOnlyThePort() {
    if (!CanRun) { return; }
    WriteNetworkFile("{\"bindHost\":\"0.0.0.0\",\"port\":\"9090\"}");

    var lines = ResolveSaved("port: \"9191\"");

    Value(lines, "host").Should().Be("0.0.0.0");
    Value(lines, "port").Should().Be("9191");
    Value(lines, "portSource").Should().Be("Explicit");
  }

  [Fact]
  public void ABadSavedPortFallsBackToTheDefaultAndWritesALogLine() {
    if (!CanRun) { return; }
    WriteNetworkFile("{\"bindHost\":\"0.0.0.0\",\"port\":\"abc\"}");

    var lines = ResolveSaved("");

    Value(lines, "host").Should().Be("0.0.0.0");
    Value(lines, "port").Should().Be("8080");
    Value(lines, "portSource").Should().Be("Default");
    lines.Should().Contain(l => l.StartsWith("log=WARNING: The field port in ", StringComparison.Ordinal));
  }

  [Fact]
  public void ASameBuildReinstallKeepsTheBindingUnlessTheUserChangesIt() {
    if (!CanRun) { return; }
    WriteNetworkFile("{\"bindHost\":\"0.0.0.0\",\"port\":\"9090\"}");

    var kept = ResolveSaved("host: \"0.0.0.0\", port: \"9090\", prefillHost: \"0.0.0.0\", prefillPort: \"9090\"");
    var changed = ResolveSaved("host: \"0.0.0.0\", port: \"9191\", prefillHost: \"0.0.0.0\", prefillPort: \"9090\"");

    Value(kept, "port").Should().Be("9090");
    Value(changed, "port").Should().Be("9191");
  }

  // ---- Silent run with the saved port in use (feature 162, FR-022) ----

  private const string SavedPortHeldByOther =
    "silent: true, listeners: ParseNetstatListeners(\"  TCP    127.0.0.1:9090         0.0.0.0:0              LISTENING       999\")" +
    ", lookup: function (pid) { return null; }";

  private const string SavedPortHeldByTheBot =
    "silent: true, appFolder: \"C:\\\\Apps\\\\GameBot\\\\\", listeners: ParseNetstatListeners(\"  TCP    127.0.0.1:9090         0.0.0.0:0              LISTENING       1234\")" +
    ", lookup: function (pid) { return { name: \"GameBot.Service.exe\", path: \"C:\\\\Apps\\\\GameBot\\\\GameBot.Service.exe\" }; }";

  [Fact]
  public void SilentRunKeepsASavedPortThatAnotherProgramHoldsAndWarns() {
    if (!CanRun) { return; }
    WriteNetworkFile("{\"bindHost\":\"127.0.0.1\",\"port\":\"9090\"}");

    var lines = ResolveSaved(SavedPortHeldByOther);

    Value(lines, "port").Should().Be("9090");
    Value(lines, "portSource").Should().Be("NetworkFile");
    lines.Should().Contain("log=WARNING: port 9090 is in use. The saved port is kept.");
  }

  [Fact]
  public void SilentRunWritesNoWarningWhenTheInstalledBotHoldsTheSavedPort() {
    if (!CanRun) { return; }
    WriteNetworkFile("{\"bindHost\":\"127.0.0.1\",\"port\":\"9090\"}");

    var lines = ResolveSaved(SavedPortHeldByTheBot);

    Value(lines, "port").Should().Be("9090");
    lines.Should().NotContain(l => l.Contains("WARNING: port", StringComparison.Ordinal));
  }

  [Fact]
  public void AnInteractiveRunWritesNoPortWarning() {
    if (!CanRun) { return; }
    WriteNetworkFile("{\"bindHost\":\"127.0.0.1\",\"port\":\"9090\"}");

    var lines = ResolveSaved(SavedPortHeldByOther.Replace("silent: true", "silent: false", StringComparison.Ordinal));

    lines.Should().NotContain(l => l.Contains("WARNING: port", StringComparison.Ordinal));
  }

  // ---- Older registry values: migrated once, deleted at commit (feature 162, US6) ----

  private static void WriteLegacyRegistry(string root, string host, string port) {
    using var key = Registry.CurrentUser.CreateSubKey(root);
    key.SetValue("BindHost", host);
    key.SetValue("Port", port);
    key.SetValue("Protocol", "http");
  }

  private static (string? Host, string? Port, string? Protocol) ReadLegacyRegistry(string root) {
    using var key = Registry.CurrentUser.OpenSubKey(root);
    return (key?.GetValue("BindHost") as string, key?.GetValue("Port") as string, key?.GetValue("Protocol") as string);
  }

  // The root has the form Software\GameBotTest\<guid>\Network. Delete only the key of this test (<guid>), so a test
  // install of a developer (for example HKCU\Software\GameBotTest\T162) is never touched.
  private static void DeleteTestRegistry(string root) =>
    Registry.CurrentUser.DeleteSubKeyTree(root[..root.LastIndexOf('\\')], throwOnMissingSubKey: false);

  [Fact]
  public void CommitDeletesTheOlderHostAndPortValuesAndKeepsTheProtocol() {
    if (!CanRun) { return; }
    var root = NewTestRegistryRoot();
    try {
      WriteLegacyRegistry(root, "0.0.0.0", "9003");
      Write("0.0.0.0", "9003");

      Run("CommitNetworkFileCore(" + JsString(_dataPath) + ", " + JsString(root) + "); show(\"done\", 1);");

      var legacy = ReadLegacyRegistry(root);
      legacy.Host.Should().BeNull();
      legacy.Port.Should().BeNull();
      legacy.Protocol.Should().Be("http");
      Value(Read(), "port").Should().Be("9003");
    }
    finally {
      DeleteTestRegistry(root);
    }
  }

  [Fact]
  public void CommitWithNoRegistryRootDoesNotTouchTheRegistry() {
    if (!CanRun) { return; }
    var root = NewTestRegistryRoot();
    try {
      WriteLegacyRegistry(root, "0.0.0.0", "9003");

      Run("CommitNetworkFileCore(" + JsString(_dataPath) + ", \"\"); show(\"done\", 1);");

      ReadLegacyRegistry(root).Host.Should().Be("0.0.0.0");
    }
    finally {
      DeleteTestRegistry(root);
    }
  }

  [Fact]
  public void RollbackKeepsTheOlderRegistryValues() {
    if (!CanRun) { return; }
    var root = NewTestRegistryRoot();
    try {
      WriteLegacyRegistry(root, "0.0.0.0", "9003");
      Write("0.0.0.0", "9003");

      Rollback();

      var legacy = ReadLegacyRegistry(root);
      legacy.Host.Should().Be("0.0.0.0");
      legacy.Port.Should().Be("9003");
    }
    finally {
      DeleteTestRegistry(root);
    }
  }

  [Fact]
  public void AnInstallFromBefore132MigratesTheRegistryValuesIntoTheFile() {
    if (!CanRun) { return; }
    var root = NewTestRegistryRoot();
    try {
      WriteLegacyRegistry(root, "0.0.0.0", "9003");

      // Resolve (no file yet), write, then commit. This is what the installer does in one run.
      var lines = Run(
        "var r = ResolveWithSavedValuesCore(" + JsString(_dataPath) + ", " + JsString(root) + ", { computerName: \"PC1\" });" +
        " show(\"host\", r.host); show(\"port\", r.port); show(\"portSource\", r.portSource);" +
        " WriteNetworkFileCore(" + JsString(_dataPath) + ", r.host, r.port);" +
        " CommitNetworkFileCore(" + JsString(_dataPath) + ", " + JsString(root) + ");");

      Value(lines, "host").Should().Be("0.0.0.0");
      Value(lines, "port").Should().Be("9003");
      Value(lines, "portSource").Should().Be("LegacyRegistry");
      var fileValues = Read();
      Value(fileValues, "host").Should().Be("0.0.0.0");
      Value(fileValues, "port").Should().Be("9003");
      ReadLegacyRegistry(root).Host.Should().BeNull();
      ReadLegacyRegistry(root).Port.Should().BeNull();
    }
    finally {
      DeleteTestRegistry(root);
    }
  }

  [Fact]
  public void TheCommitDataSplitsIntoTheDataPathAndTheRegistryRoot() {
    if (!CanRun) { return; }

    var lines = Run("var p = SplitCommitData(" + JsString(_dataPath + "|Software\\X\\Network") + "); show(\"path\", p.dataPath); show(\"root\", p.registryRoot);" +
                    " var q = SplitCommitData(" + JsString(_dataPath) + "); show(\"root2\", q.registryRoot);");

    Value(lines, "path").Should().Be(_dataPath);
    Value(lines, "root").Should().Be("Software\\X\\Network");
    Value(lines, "root2").Should().BeEmpty();
  }

  // ---- Interface list (feature 162, T015) ----

  private List<string> Rows(string discovered, string currentHost) => Run(
    "var rows = InterfaceRowsCore(" + discovered + ", " + JsString(currentHost) + ");" +
    " show(\"count\", rows.length);" +
    " for (var i = 0; i < rows.length; i++) { show(\"row\", rows[i].value + \"|\" + rows[i].text); }");

  [Fact]
  public void InterfaceListHasTheDiscoveredInterfaces() {
    if (!CanRun) { return; }

    var lines = Rows("[{ name: \"Ethernet\", ip: \"192.168.1.5\" }]", "127.0.0.1");

    Value(lines, "count").Should().Be("1");
    lines.Should().Contain("row=192.168.1.5|Ethernet (192.168.1.5)");
  }

  [Fact]
  public void InterfaceListIncludesASavedValueThatIsNotDiscovered() {
    if (!CanRun) { return; }

    var lines = Rows("[{ name: \"Ethernet\", ip: \"192.168.1.5\" }]", "10.9.9.9");

    Value(lines, "count").Should().Be("2");
    lines.Should().Contain("row=10.9.9.9|10.9.9.9");
  }

  [Theory]
  [InlineData("127.0.0.1")]
  [InlineData("0.0.0.0")]
  [InlineData("192.168.1.5")]
  [InlineData("")]
  public void InterfaceListAddsNoRowForAValueThatIsAlreadyInTheList(string currentHost) {
    ArgumentNullException.ThrowIfNull(currentHost);
    if (!CanRun) { return; }

    var lines = Rows("[{ name: \"Ethernet\", ip: \"192.168.1.5\" }]", currentHost);

    Value(lines, "count").Should().Be("1");
  }

  // ---- Legacy registry (feature 162): only under the root that the caller gives ----

  private static string NewTestRegistryRoot() => @"Software\GameBotTest\" + Guid.NewGuid().ToString("N") + @"\Network";

  [Fact]
  public void LegacyRegistryReadGivesNothingForAnEmptyRoot() {
    if (!CanRun) { return; }

    var lines = Run("var r = ReadLegacyRegistryCore(\"\"); show(\"host\", r.host); show(\"port\", r.port);");

    Value(lines, "host").Should().Be("<null>");
    Value(lines, "port").Should().Be("<null>");
  }

  [Fact]
  public void LegacyRegistryReadGivesNothingWhenTheKeyIsMissing() {
    if (!CanRun) { return; }

    var lines = Run("var r = ReadLegacyRegistryCore(" + JsString(NewTestRegistryRoot()) + "); show(\"host\", r.host); show(\"port\", r.port);");

    Value(lines, "host").Should().Be("<null>");
    Value(lines, "port").Should().Be("<null>");
  }

  [Fact]
  public void LegacyRegistryReadGivesTheValuesUnderTheGivenRoot() {
    if (!CanRun) { return; }
    var root = NewTestRegistryRoot();
    try {
      using (var key = Registry.CurrentUser.CreateSubKey(root)) {
        key.SetValue("BindHost", "0.0.0.0");
        key.SetValue("Port", "9003");
      }

      var lines = Run("var r = ReadLegacyRegistryCore(" + JsString(root) + "); show(\"host\", r.host); show(\"port\", r.port);");

      Value(lines, "host").Should().Be("0.0.0.0");
      Value(lines, "port").Should().Be("9003");
    }
    finally {
      DeleteTestRegistry(root);
    }
  }

  [Fact]
  public void LegacyRegistryReadIgnoresValuesThatAreNotValid() {
    if (!CanRun) { return; }
    var root = NewTestRegistryRoot();
    try {
      using (var key = Registry.CurrentUser.CreateSubKey(root)) {
        key.SetValue("BindHost", "a b");
        key.SetValue("Port", "99999");
      }

      var lines = Run("var r = ReadLegacyRegistryCore(" + JsString(root) + "); show(\"host\", r.host); show(\"port\", r.port);");

      Value(lines, "host").Should().Be("<null>");
      Value(lines, "port").Should().Be("<null>");
    }
    finally {
      DeleteTestRegistry(root);
    }
  }

  private void AssertNoTemporaryFiles() {
    File.Exists(_file + ".tmp").Should().BeFalse();
    File.Exists(_file + ".bak").Should().BeFalse();
    File.Exists(_file + ".none").Should().BeFalse();
  }
}
