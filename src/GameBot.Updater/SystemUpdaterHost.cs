using System.ComponentModel;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;

namespace GameBot.Updater;

/// <summary>The real effects of the updater. Process and file calls only. The manual check in quickstart.md covers them.</summary>
[ExcludeFromCodeCoverage]
internal sealed class SystemUpdaterHost : IUpdaterHost {
  private const string ServiceExeName = "GameBot.Service.exe";

  public DateTimeOffset UtcNow => DateTimeOffset.UtcNow;

  public bool WaitForExit(int pid, TimeSpan timeout) {
    Process process;
    try {
      process = Process.GetProcessById(pid);
    }
    catch (ArgumentException) {
      return true;
    }

    using (process) {
      try {
        return process.WaitForExit(timeout);
      }
      catch (InvalidOperationException) {
        return true;
      }
    }
  }

  public async Task<int> RunMsiexecAsync(string arguments) {
    var info = new ProcessStartInfo("msiexec.exe", arguments) {
      UseShellExecute = false,
      CreateNoWindow = true
    };

    using var process = Process.Start(info) ?? throw new InvalidOperationException("msiexec did not start.");
    await process.WaitForExitAsync().ConfigureAwait(false);
    return process.ExitCode;
  }

  public bool StartBot(string installFolder) {
    var exe = Path.Combine(installFolder, ServiceExeName);
    if (!File.Exists(exe)) {
      return false;
    }

    try {
      var info = new ProcessStartInfo(exe) {
        UseShellExecute = false,
        CreateNoWindow = true,
        WorkingDirectory = installFolder
      };
      using var process = Process.Start(info);
      return process is not null;
    }
    catch (Win32Exception) {
      return false;
    }
    catch (InvalidOperationException) {
      return false;
    }
  }

  public void WriteResultFile(string path, string json) {
    var directory = Path.GetDirectoryName(path);
    if (!string.IsNullOrEmpty(directory)) {
      Directory.CreateDirectory(directory);
    }

    // Write to a temporary file first, so the bot never reads a half-written file.
    var temp = path + ".tmp";
    File.WriteAllText(temp, json);
    File.Move(temp, path, overwrite: true);
  }
}
