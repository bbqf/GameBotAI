using System.Diagnostics;
using System.Text;

namespace GameBot.Emulator.Adb;

public sealed class AdbClient
{
    private readonly string _adb;

    public AdbClient()
    {
        _adb = AdbResolver.ResolveAdbPath() ?? "adb"; // Use PATH fallback
    }

    public AdbClient(string adbPath)
    {
        _adb = adbPath;
    }

    public async Task<(int ExitCode, string StdOut, string StdErr)> ExecAsync(string arguments, CancellationToken ct = default)
    {
        var psi = new ProcessStartInfo
        {
            FileName = _adb,
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

        if (!proc.Start()) throw new InvalidOperationException("Failed to start adb process");
        proc.BeginOutputReadLine();
        proc.BeginErrorReadLine();

        await proc.WaitForExitAsync(ct);
        return (proc.ExitCode, stdout.ToString().TrimEnd(), stderr.ToString().TrimEnd());
    }

    public Task<(int ExitCode, string StdOut, string StdErr)> KeyEventAsync(int keyCode, CancellationToken ct = default)
        => ExecAsync($"shell input keyevent {keyCode}", ct);

    public Task<(int ExitCode, string StdOut, string StdErr)> TapAsync(int x, int y, CancellationToken ct = default)
        => ExecAsync($"shell input tap {x} {y}", ct);

    public Task<(int ExitCode, string StdOut, string StdErr)> SwipeAsync(int x1, int y1, int x2, int y2, int? durationMs = null, CancellationToken ct = default)
        => ExecAsync($"shell input swipe {x1} {y1} {x2} {y2} {(durationMs is null ? string.Empty : durationMs.Value.ToString())}", ct);

    public async Task<byte[]> GetScreenshotPngAsync(CancellationToken ct = default)
    {
        // Use exec-out for raw PNG
        var psi = new ProcessStartInfo
        {
            FileName = _adb,
            Arguments = "exec-out screencap -p",
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        using var proc = new Process { StartInfo = psi };
        if (!proc.Start()) throw new InvalidOperationException("Failed to start adb process");
        await using var ms = new MemoryStream();
        await proc.StandardOutput.BaseStream.CopyToAsync(ms, ct);
        await proc.WaitForExitAsync(ct);
        var png = ms.ToArray();
        if (png.Length == 0)
        {
            var err = await proc.StandardError.ReadToEndAsync();
            throw new InvalidOperationException($"Failed to capture screenshot: {err}");
        }
        return png;
    }
}
