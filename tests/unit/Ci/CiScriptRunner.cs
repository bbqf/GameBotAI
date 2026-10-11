using System.Diagnostics;

namespace GameBot.UnitTests.Ci;

internal static class CiScriptRunner {
  internal static string FindRepoRoot() {
    var dir = new DirectoryInfo(AppContext.BaseDirectory);
    while (dir is not null) {
      if (File.Exists(Path.Combine(dir.FullName, "GameBot.sln"))) {
        return dir.FullName;
      }

      dir = dir.Parent;
    }

    throw new DirectoryNotFoundException("Unable to locate repository root containing GameBot.sln");
  }

  // Runs a script under scripts/ci and returns its standard output lines.
  internal static IReadOnlyList<string> Run(string scriptName, params string[] arguments) {
    var (exitCode, lines, error) = RunWithExitCode(scriptName, arguments);
    if (exitCode != 0) {
      throw new InvalidOperationException($"{scriptName} failed with exit code {exitCode}: {error}{string.Join('\n', lines)}");
    }

    return lines;
  }

  // Runs a script under scripts/ci and returns the exit code with the output.
  internal static (int ExitCode, IReadOnlyList<string> Lines, string Error) RunWithExitCode(string scriptName, params string[] arguments) {
    var script = Path.Combine(FindRepoRoot(), "scripts", "ci", scriptName);
    var startInfo = new ProcessStartInfo {
      FileName = ResolveShell(),
      RedirectStandardOutput = true,
      RedirectStandardError = true,
      UseShellExecute = false,
      CreateNoWindow = true,
    };
    startInfo.ArgumentList.Add("-NoProfile");
    startInfo.ArgumentList.Add("-NonInteractive");
    startInfo.ArgumentList.Add("-File");
    startInfo.ArgumentList.Add(script);
    foreach (var argument in arguments) {
      startInfo.ArgumentList.Add(argument);
    }

    using var process = Process.Start(startInfo) ?? throw new InvalidOperationException("Cannot start PowerShell.");
    var errorTask = process.StandardError.ReadToEndAsync();
    var output = process.StandardOutput.ReadToEnd();
    process.WaitForExit();
    var error = errorTask.GetAwaiter().GetResult();
    return (process.ExitCode, output.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries), error);
  }

  // PowerShell 7 runs the scripts in CI. Windows PowerShell 5.1 is the fallback on a developer PC.
  private static string ResolveShell() {
    var path = Environment.GetEnvironmentVariable("PATH") ?? string.Empty;
    var extension = OperatingSystem.IsWindows() ? ".exe" : string.Empty;
    foreach (var directory in path.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries)) {
      if (File.Exists(Path.Combine(directory, "pwsh" + extension))) {
        return Path.Combine(directory, "pwsh" + extension);
      }
    }

    return "powershell";
  }
}
