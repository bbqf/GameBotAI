using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;

namespace GameBot.Service.Services.Updates;

/// <summary>Starts the updater as a separate process. It keeps running after the bot exits.</summary>
[ExcludeFromCodeCoverage]
internal sealed class ProcessUpdaterStarter : IUpdaterProcessStarter {
  public void StartDetached(string exePath, IReadOnlyList<string> arguments, string workingDirectory) {
    var info = new ProcessStartInfo(exePath) {
      UseShellExecute = false,
      CreateNoWindow = true,
      WorkingDirectory = workingDirectory
    };
    foreach (var argument in arguments) {
      info.ArgumentList.Add(argument);
    }

    using var process = Process.Start(info) ?? throw new InvalidOperationException("The updater did not start.");
  }
}
