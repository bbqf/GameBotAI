using System.Diagnostics;
using System.Globalization;
using System.Runtime.Versioning;
using System.Text;
using Microsoft.Extensions.Logging;

namespace GameBot.Emulator.Adb;

[SupportedOSPlatform("windows")]
public sealed class AdbClient {
  private readonly string _adb;
  private readonly string? _serial;
  private readonly ILogger? _logger;

  public AdbClient() : this(AdbResolver.ResolveAdbPath() ?? "adb", null, null) { }

  public AdbClient(ILogger? logger) : this(AdbResolver.ResolveAdbPath() ?? "adb", null, logger) { }

  public AdbClient(string adbPath) : this(adbPath, null, null) { }

  private AdbClient(string adbPath, string? serial, ILogger? logger) {
    _adb = adbPath;
    _serial = serial;
    _logger = logger;
  }

  public AdbClient WithSerial(string? serial) => new(_adb, serial, _logger);

  public async Task<(int ExitCode, string StdOut, string StdErr)> ExecAsync(string arguments, CancellationToken ct = default) {
    var args = string.IsNullOrWhiteSpace(_serial) ? arguments : $"-s {_serial} {arguments}";
    Log.ExecStart(_logger, _adb, args);
    var psi = new ProcessStartInfo {
      FileName = _adb,
      Arguments = args,
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

    if (!proc.Start()) throw new InvalidOperationException("Failed to start adb process");
    proc.BeginOutputReadLine();
    proc.BeginErrorReadLine();

    await proc.WaitForExitAsync(ct).ConfigureAwait(false);
    var so = stdout.ToString().TrimEnd();
    var se = stderr.ToString().TrimEnd();
    Log.ExecEnd(_logger, proc.ExitCode, args, Trunc(so), Trunc(se));
    return (proc.ExitCode, so, se);
  }

  public Task<(int ExitCode, string StdOut, string StdErr)> KeyEventAsync(int keyCode, CancellationToken ct = default) {
    Log.KeyEvent(_logger, keyCode);
    return ExecAsync($"shell input keyevent {keyCode}", ct);
  }

  public Task<(int ExitCode, string StdOut, string StdErr)> TapAsync(int x, int y, CancellationToken ct = default) {
    Log.Tap(_logger, x, y);
    return ExecAsync($"shell input tap {x} {y}", ct);
  }

  public Task<(int ExitCode, string StdOut, string StdErr)> SwipeAsync(int x1, int y1, int x2, int y2, int? durationMs = null, CancellationToken ct = default) {
    Log.Swipe(_logger, x1, y1, x2, y2, durationMs);
    return ExecAsync($"shell input swipe {x1} {y1} {x2} {y2} {(durationMs is null ? string.Empty : durationMs.Value.ToString(CultureInfo.InvariantCulture))}", ct);
  }

  public async Task<byte[]> GetScreenshotPngAsync(CancellationToken ct = default) {
    // Use exec-out for raw PNG
    var cmdArgs = string.IsNullOrWhiteSpace(_serial) ? "exec-out screencap -p" : $"-s {_serial} exec-out screencap -p";
    Log.ScreencapStart(_logger, cmdArgs);
    var psi = new ProcessStartInfo {
      FileName = _adb,
      Arguments = cmdArgs,
      RedirectStandardOutput = true,
      RedirectStandardError = true,
      UseShellExecute = false,
      CreateNoWindow = true,
    };
    using var proc = new Process { StartInfo = psi };
    if (!proc.Start()) throw new InvalidOperationException("Failed to start adb process");
    using var ms = new MemoryStream();
    await proc.StandardOutput.BaseStream.CopyToAsync(ms, ct).ConfigureAwait(false);
    await proc.WaitForExitAsync(ct).ConfigureAwait(false);
    var png = ms.ToArray();
    if (png.Length == 0) {
      var err = await proc.StandardError.ReadToEndAsync(ct).ConfigureAwait(false);
      throw new InvalidOperationException($"Failed to capture screenshot: {err}");
    }
    Log.ScreencapEnd(_logger, png.Length);
    return png;
  }

  private static string Trunc(string s) {
    if (string.IsNullOrEmpty(s)) return s;
    return s.Length <= 200 ? s : string.Concat(s.AsSpan(0, 200), "...");
  }
}

internal static class Log {
  private static readonly Action<ILogger, string, string, Exception?> _execStart =
      LoggerMessage.Define<string, string>(LogLevel.Debug, new EventId(1001, nameof(ExecStart)),
          "ADB exec start: {Adb} {Args}");

  private static readonly Action<ILogger, int, string, string, Exception?> _execEnd =
      LoggerMessage.Define<int, string, string>(LogLevel.Debug, new EventId(1002, nameof(ExecEnd)),
          "ADB exec end ({ExitCode}): cmd={Args} out={Stdout}");

  private static readonly Action<ILogger, int, Exception?> _keyEvent =
      LoggerMessage.Define<int>(LogLevel.Debug, new EventId(1003, nameof(KeyEvent)),
          "ADB keyevent {Key}");

  private static readonly Action<ILogger, int, int, Exception?> _tap =
      LoggerMessage.Define<int, int>(LogLevel.Debug, new EventId(1004, nameof(Tap)),
          "ADB tap {X} {Y}");

  private static readonly Action<ILogger, int, int, int, int, int?, Exception?> _swipe =
      LoggerMessage.Define<int, int, int, int, int?>(LogLevel.Debug, new EventId(1005, nameof(Swipe)),
          "ADB swipe {X1} {Y1} {X2} {Y2} {Dur}");

  private static readonly Action<ILogger, string, Exception?> _screencapStart =
      LoggerMessage.Define<string>(LogLevel.Debug, new EventId(1006, nameof(ScreencapStart)),
          "ADB screencap start: {Args}");

  private static readonly Action<ILogger, int, Exception?> _screencapEnd =
      LoggerMessage.Define<int>(LogLevel.Debug, new EventId(1007, nameof(ScreencapEnd)),
          "ADB screencap end size={Bytes}");

  public static void ExecStart(ILogger? l, string adb, string args) { if (l != null) _execStart(l, adb, args, null); }
  public static void ExecEnd(ILogger? l, int exit, string args, string stdout, string stderr) { if (l != null) _execEnd(l, exit, args, stdout, null); }
  public static void KeyEvent(ILogger? l, int key) { if (l != null) _keyEvent(l, key, null); }
  public static void Tap(ILogger? l, int x, int y) { if (l != null) _tap(l, x, y, null); }
  public static void Swipe(ILogger? l, int x1, int y1, int x2, int y2, int? dur) { if (l != null) _swipe(l, x1, y1, x2, y2, dur, null); }
  public static void ScreencapStart(ILogger? l, string args) { if (l != null) _screencapStart(l, args, null); }
  public static void ScreencapEnd(ILogger? l, int bytes) { if (l != null) _screencapEnd(l, bytes, null); }
}
