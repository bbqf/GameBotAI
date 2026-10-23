using System.Diagnostics;
using System.Text;
using FluentAssertions;
using Xunit;

namespace GameBot.UnitTests.Installer;

/// <summary>
/// Checks the port rules of <c>PortResolver.jscript</c> (feature 162, FR-006). The tests run the real script
/// with <c>cscript.exe</c> on sample <c>netstat -ano -p tcp</c> text. A lookup function plays the role of WMI.
/// </summary>
public class PortSelectionRulesTests {
  private static readonly string CscriptPath = Path.Combine(
    Environment.GetFolderPath(Environment.SpecialFolder.Windows), "System32", "cscript.exe");

  private static readonly string[] LineBreaks = { "\r\n", "\n" };

  private const string AppFolder = "C:\\Apps\\GameBot\\";

  private const string Netstat =
    "Active Connections\r\n\r\n" +
    "  Proto  Local Address          Foreign Address        State           PID\r\n" +
    "  TCP    0.0.0.0:80             0.0.0.0:0              LISTENING       4\r\n" +
    "  TCP    127.0.0.1:8080         0.0.0.0:0              LISTENING       1234\r\n" +
    "  TCP    192.168.1.5:8088       0.0.0.0:0              LISTENING       555\r\n" +
    "  TCP    192.168.1.5:50000      10.0.0.1:8888          ESTABLISHED     999\r\n" +
    "  TCP    127.0.0.1:50001        127.0.0.1:8081         TIME_WAIT       0\r\n" +
    "  TCP    [::]:445               [::]:0                 LISTENING       4\r\n" +
    "  TCP    [::1]:9000             [::]:0                 LISTENING       77\r\n";

  // PID 1234: the installed bot. PID 555: a bot from another folder. PID 4: the system. Others: unreadable.
  private const string Lookup =
    "function lookup(pid) {" +
    " if (pid == 1234) { return { name: \"GameBot.Service.exe\", path: \"C:\\\\Apps\\\\GameBot\\\\GameBot.Service.exe\" }; }" +
    " if (pid == 555) { return { name: \"GameBot.Service.exe\", path: \"D:\\\\Other\\\\GameBot.Service.exe\" }; }" +
    " if (pid == 4) { return { name: \"System\", path: \"\" }; }" +
    " return null; }";

  private static bool CanRun => OperatingSystem.IsWindows() && File.Exists(CscriptPath);

  private static string JsText(string value) =>
    "\"" + value.Replace("\\", "\\\\", StringComparison.Ordinal).Replace("\"", "\\\"", StringComparison.Ordinal)
      .Replace("\r", "\\r", StringComparison.Ordinal).Replace("\n", "\\n", StringComparison.Ordinal) + "\"";

  private static string FindRepoRoot() {
    var dir = new DirectoryInfo(AppContext.BaseDirectory);
    while (dir is not null) {
      if (File.Exists(Path.Combine(dir.FullName, "GameBot.sln"))) {
        return dir.FullName;
      }

      dir = dir.Parent;
    }

    throw new DirectoryNotFoundException("Unable to locate repository root containing GameBot.sln");
  }

  private static List<string> Run(string snippet) {
    var scriptPath = Path.Combine(FindRepoRoot(), "installer", "wix", "Scripts", "PortResolver.jscript");
    var harness = Path.Combine(Path.GetTempPath(), "gb-port-" + Guid.NewGuid().ToString("N") + ".js");
    var text = new StringBuilder();
    text.AppendLine("var fso = new ActiveXObject(\"Scripting.FileSystemObject\");");
    text.AppendLine("var stream = fso.OpenTextFile(" + JsText(scriptPath) + ", 1);");
    text.AppendLine("var source = stream.ReadAll();");
    text.AppendLine("stream.Close();");
    text.AppendLine("eval(source);");
    text.AppendLine("function show(name, value) { WScript.Echo(name + \"=\" + value); }");
    text.AppendLine(Lookup);
    text.AppendLine("var listeners = ParseNetstatListeners(" + JsText(Netstat) + ");");
    text.AppendLine(snippet);
    File.WriteAllText(harness, text.ToString(), Encoding.ASCII);
    try {
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
    finally {
      File.Delete(harness);
    }
  }

  private static string Value(List<string> lines, string name) {
    var prefix = name + "=";
    var line = lines.FirstOrDefault(l => l.StartsWith(prefix, StringComparison.Ordinal));
    line.Should().NotBeNull("output must have a '" + name + "' line. Output: " + string.Join(" / ", lines));
    return line![prefix.Length..];
  }

  private static string State(string host, int port) => Value(
    Run("var c = ClassifyPortCore(listeners, " + port + ", " + JsText(host) + ", " + JsText(AppFolder) + ", lookup); show(\"state\", c.state);"),
    "state");

  [Fact]
  public void PortDetectionAndPropertiesContainDeterministicPortOrder() {
    var repoRoot = FindRepoRoot();
    var propertiesPath = Path.Combine(repoRoot, "installer", "wix", "Fragments", "InstallerProperties.wxs");
    var detectionPath = Path.Combine(repoRoot, "installer", "wix", "Fragments", "PortDetection.wxs");
    var resolverScriptPath = Path.Combine(repoRoot, "installer", "wix", "Scripts", "PortResolver.jscript");

    File.Exists(propertiesPath).Should().BeTrue();
    File.Exists(detectionPath).Should().BeTrue();
    File.Exists(resolverScriptPath).Should().BeTrue();

    var propertiesContent = File.ReadAllText(propertiesPath);
    var detectionContent = File.ReadAllText(detectionPath);
    var resolverScriptContent = File.ReadAllText(resolverScriptPath);

    propertiesContent.Should().Contain("8080,8088,8888,80");
    detectionContent.Should().Contain("ResolveNetworkBinding");
    detectionContent.Should().Contain("DiscoverBindInterfaces");
    resolverScriptContent.Should().Contain("PREFERRED_PORT_ORDER");
    resolverScriptContent.Should().Contain("discoverAndPopulateInterfaces");
  }

  [Fact]
  public void ParserReadsOnlyListeningLinesAndTheLocalAddress() {
    if (!CanRun) { return; }

    var lines = Run(
      "show(\"count\", listeners.length);" +
      " for (var i = 0; i < listeners.length; i++) { show(\"l\", listeners[i].address + \"|\" + listeners[i].port + \"|\" + listeners[i].pid); }");

    Value(lines, "count").Should().Be("5");
    lines.Should().Contain("l=0.0.0.0|80|4");
    lines.Should().Contain("l=127.0.0.1|8080|1234");
    lines.Should().Contain("l=[::]|445|4");
    lines.Should().Contain("l=[::1]|9000|77");
  }

  [Fact]
  public void AListeningLineOnThePortCounts() {
    if (!CanRun) { return; }

    State("0.0.0.0", 9000).Should().Be("inuse");
    State("127.0.0.1", 445).Should().Be("inuse");
  }

  [Fact]
  public void ThePortInAForeignAddressDoesNotCount() {
    if (!CanRun) { return; }

    State("192.168.1.5", 8888).Should().Be("free");
  }

  [Theory]
  [InlineData(50000)]
  [InlineData(50001)]
  [InlineData(8081)]
  public void AnEstablishedOrWaitingLineDoesNotCount(int port) {
    if (!CanRun) { return; }

    State("127.0.0.1", port).Should().Be("free");
  }

  [Theory]
  [InlineData("127.0.0.1")]
  [InlineData("192.168.1.5")]
  [InlineData("0.0.0.0")]
  public void AListenerOnAllInterfacesCountsForEveryInterface(string host) {
    if (!CanRun) { return; }

    State(host, 80).Should().Be("inuse");
  }

  [Fact]
  public void AListenerOnASpecificAddressCountsOnlyForThatAddress() {
    if (!CanRun) { return; }

    State("192.168.1.5", 9000).Should().Be("free");
    State("127.0.0.1", 8088).Should().Be("free");
    State("0.0.0.0", 8088).Should().Be("inuse");
  }

  [Fact]
  public void TheInstalledGameBotServiceDoesNotCount() {
    if (!CanRun) { return; }

    State("127.0.0.1", 8080).Should().Be("gamebot");
  }

  [Fact]
  public void TheSameProcessNameInAnotherFolderCounts() {
    if (!CanRun) { return; }

    State("0.0.0.0", 8088).Should().Be("inuse");
  }

  [Fact]
  public void AProcessThatCannotBeReadCounts() {
    if (!CanRun) { return; }

    // PID 77 is not known to the lookup function, so the owner is unreadable.
    State("0.0.0.0", 9000).Should().Be("inuse");
  }

  [Fact]
  public void FirstInstallChoosesTheFirstFreePortOfThePreferredOrder() {
    if (!CanRun) { return; }

    // 8080 is held by another program in this sample, so the first free port is 8088.
    var lines = Run(
      "var other = ParseNetstatListeners(" + JsText(Netstat.Replace("1234", "999", StringComparison.Ordinal)) + ");" +
      " show(\"port\", FirstFreePortCore([8080, 8088, 8888, 80], other, \"127.0.0.1\", " + JsText(AppFolder) + ", lookup));");

    Value(lines, "port").Should().Be("8088");
  }

  [Fact]
  public void FirstInstallChoosesTheFirstPortWhenItIsFree() {
    if (!CanRun) { return; }

    var lines = Run("show(\"port\", FirstFreePortCore([18080, 8088], [], \"127.0.0.1\", " + JsText(AppFolder) + ", lookup));");

    Value(lines, "port").Should().Be("18080");
  }

  [Fact]
  public void FirstInstallUsesTheFirstPreferredPortWhenAllAreInUse() {
    if (!CanRun) { return; }

    var lines = Run("show(\"port\", FirstFreePortCore([80, 445], listeners, \"127.0.0.1\", " + JsText(AppFolder) + ", lookup));");

    Value(lines, "port").Should().Be("80");
  }
}
