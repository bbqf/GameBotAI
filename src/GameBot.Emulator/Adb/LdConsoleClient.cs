using System.Diagnostics;
using System.Globalization;
using System.Runtime.Versioning;
using System.Text;
using Microsoft.Extensions.Logging;

namespace GameBot.Emulator.Adb;

/// <summary>Run state of an LDPlayer instance as reported by <c>ldconsole isrunning</c>.</summary>
public enum LdConsoleRunState {
  /// <summary>Instance reported "running".</summary>
  Running,
  /// <summary>Instance reported "stop"/stopped.</summary>
  Stopped,
  /// <summary>The identifier matched no instance (or the tool gave an unrecognized answer).</summary>
  NotFound
}

/// <summary>
/// Windows-only wrapper over LDPlayer's <c>ldconsole.exe</c> CLI for the ensure-emulator-running
/// action (feature 070). Mirrors <see cref="AdbClient"/>: process-exec + <see cref="LoggerMessage"/>
/// logging. Instances are addressed by name (<c>--name</c>) or index (<c>--index</c>).
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class LdConsoleClient {
  private readonly string _ldconsole;
  private readonly ILogger? _logger;

  public LdConsoleClient(string ldconsolePath, ILogger? logger = null) {
    _ldconsole = ldconsolePath;
    _logger = logger;
  }

  /// <summary>Returns the run state of the addressed instance, or <see cref="LdConsoleRunState.NotFound"/>.</summary>
  public async Task<LdConsoleRunState> IsRunningAsync(string? instanceName, int? instanceIndex, CancellationToken ct = default) {
    var (code, stdout, stderr) = await ExecAsync($"isrunning {InstanceArg(instanceName, instanceIndex)}", ct).ConfigureAwait(false);
    return ParseRunState(code, stdout, stderr);
  }

  /// <summary>Launches (starts) the addressed instance.</summary>
  public Task<(int ExitCode, string StdOut, string StdErr)> LaunchAsync(string? instanceName, int? instanceIndex, CancellationToken ct = default) =>
    ExecAsync($"launch {InstanceArg(instanceName, instanceIndex)}", ct);

  /// <summary>Restarts the addressed instance in place (single <c>reboot</c> command).</summary>
  public Task<(int ExitCode, string StdOut, string StdErr)> RebootAsync(string? instanceName, int? instanceIndex, CancellationToken ct = default) =>
    ExecAsync($"reboot {InstanceArg(instanceName, instanceIndex)}", ct);

  /// <summary>Builds the <c>--name</c>/<c>--index</c> selector; name wins when both are supplied.</summary>
  public static string InstanceArg(string? instanceName, int? instanceIndex) {
    if (!string.IsNullOrWhiteSpace(instanceName)) return $"--name \"{instanceName}\"";
    if (instanceIndex is not null) return $"--index {instanceIndex.Value.ToString(CultureInfo.InvariantCulture)}";
    return string.Empty;
  }

  /// <summary>
  /// Maps <c>isrunning</c> output to a run state. Recognizes only the documented "running" and "stop"
  /// answers; a non-zero exit, empty output, or any other text is treated as <see cref="LdConsoleRunState.NotFound"/>
  /// (a nonexistent instance identifier), which the handler surfaces as a step failure (FR-014).
  /// </summary>
  public static LdConsoleRunState ParseRunState(int exitCode, string? stdout, string? stderr) {
    var text = (stdout ?? string.Empty).Trim();
    if (exitCode == 0) {
      if (text.Equals("running", StringComparison.OrdinalIgnoreCase)) return LdConsoleRunState.Running;
      if (text.StartsWith("stop", StringComparison.OrdinalIgnoreCase)) return LdConsoleRunState.Stopped;
    }
    return LdConsoleRunState.NotFound;
  }

  public async Task<(int ExitCode, string StdOut, string StdErr)> ExecAsync(string arguments, CancellationToken ct = default) {
    Log.ExecStart(_logger, _ldconsole, arguments);
    var psi = new ProcessStartInfo {
      FileName = _ldconsole,
      Arguments = arguments,
      RedirectStandardOutput = true,
      RedirectStandardError = true,
      UseShellExecute = false,
      CreateNoWindow = true,
    };
    using var proc = new Process { StartInfo = psi, EnableRaisingEvents = true };
    var stdout = new StringBuilder();
    var stderr = new StringBuilder();
    proc.OutputDataReceived += (_, e) => { if (e.Data is not null) stdout.AppendLine(e.Data); };
    proc.ErrorDataReceived += (_, e) => { if (e.Data is not null) stderr.AppendLine(e.Data); };

    if (!proc.Start()) throw new InvalidOperationException("Failed to start ldconsole process");
    proc.BeginOutputReadLine();
    proc.BeginErrorReadLine();

    await proc.WaitForExitAsync(ct).ConfigureAwait(false);
    var so = stdout.ToString().TrimEnd();
    var se = stderr.ToString().TrimEnd();
    Log.ExecEnd(_logger, proc.ExitCode, arguments, Trunc(so));
    return (proc.ExitCode, so, se);
  }

  private static string Trunc(string s) => string.IsNullOrEmpty(s) || s.Length <= 200 ? s : string.Concat(s.AsSpan(0, 200), "...");

  private static class Log {
    private static readonly Action<ILogger, string, string, Exception?> _execStart =
        LoggerMessage.Define<string, string>(LogLevel.Debug, new EventId(1101, nameof(ExecStart)),
            "ldconsole exec start: {Tool} {Args}");

    private static readonly Action<ILogger, int, string, string, Exception?> _execEnd =
        LoggerMessage.Define<int, string, string>(LogLevel.Debug, new EventId(1102, nameof(ExecEnd)),
            "ldconsole exec end ({ExitCode}): cmd={Args} out={Stdout}");

    public static void ExecStart(ILogger? l, string tool, string args) { if (l != null) _execStart(l, tool, args, null); }
    public static void ExecEnd(ILogger? l, int exit, string args, string stdout) { if (l != null) _execEnd(l, exit, args, stdout, null); }
  }
}
