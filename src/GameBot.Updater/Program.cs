using System.Diagnostics.CodeAnalysis;

namespace GameBot.Updater;

[ExcludeFromCodeCoverage]
internal static class Program {
  private static async Task<int> Main(string[] args) {
    var parsed = UpdaterArguments.TryParse(args, out _);
    if (parsed is null) {
      return UpdaterRunner.ExitBadArguments;
    }

    var runner = new UpdaterRunner(new SystemUpdaterHost());
    return await runner.RunAsync(parsed).ConfigureAwait(false);
  }
}
