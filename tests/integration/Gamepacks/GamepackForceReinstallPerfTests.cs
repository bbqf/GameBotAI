using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using FluentAssertions;
using GameBot.Domain.Gamepacks;
using GameBot.UnitTests.Gamepacks;
using Xunit;

#pragma warning disable CA2007, CA1861, CA1707, CA1416, CA1305, CA1307, CA1310

namespace GameBot.IntegrationTests.Gamepacks;

/// <summary>
/// Feature 178 (SC-007): a forced install of a pack with 200 objects takes 1.5 times the time of a normal install, or less.
/// The test is a local measure. It compares the medians of at least 5 runs each. The environment variable
/// <c>GAMEBOT_PERF_WRITE=1</c> makes the test write the result into the quickstart file of the feature.
/// </summary>
[Trait("Category", "Performance")]
public sealed class GamepackForceReinstallPerfTests : IDisposable {
  private const int Runs = 7;
  private const double MaxRatio = 1.5;
  private readonly GamepackTestKit _kit = new();

  public void Dispose() => _kit.Dispose();

  private static string BigPack() {
    var sequences = Enumerable.Range(0, 80).Select(i => GamepackTestKit.Seq("s" + i, "Seq " + i, "c" + (i % 80))).ToArray();
    var commands = Enumerable.Range(0, 80).Select(i => GamepackTestKit.Cmd("c" + i, "Cmd " + i)).ToArray();
    var images = Enumerable.Range(0, 40).Select(i => GamepackTestKit.Img("img" + i)).ToArray();
    return GamepackTestKit.Pack("big", "1", sequences, commands, images);
  }

  private async Task<double> TimeAsync(string json, bool force) {
    var watch = Stopwatch.StartNew();
    var outcome = await _kit.Service.InstallAsync(json, new GamepackInstallOptions(Force: force));
    watch.Stop();
    outcome.Status.Should().Be(GamepackInstallStatus.Ok);
    return watch.Elapsed.TotalMilliseconds;
  }

  private static double Median(List<double> values) {
    var sorted = values.OrderBy(v => v).ToList();
    return sorted.Count % 2 == 1 ? sorted[sorted.Count / 2] : (sorted[(sorted.Count / 2) - 1] + sorted[sorted.Count / 2]) / 2;
  }

  [Fact]
  public async Task AForcedInstallOfTwoHundredObjectsCostsAtMostOnePointFiveTimesANormalInstall() {
    var json = BigPack();
    (await _kit.Service.InstallAsync(json, dryRun: false)).Created.Should().Be(200);

    // One warm-up run of each kind.
    await TimeAsync(json, force: false);
    await TimeAsync(json, force: true);

    var normal = new List<double>();
    var forced = new List<double>();
    for (var i = 0; i < Runs; i++) {
      normal.Add(await TimeAsync(json, force: false));
      forced.Add(await TimeAsync(json, force: true));
    }

    var normalMedian = Median(normal);
    var forcedMedian = Median(forced);
    var ratio = forcedMedian / normalMedian;
    WriteResult(normalMedian, forcedMedian, ratio);

    ratio.Should().BeLessThanOrEqualTo(MaxRatio, $"normal median {normalMedian:F1} ms, forced median {forcedMedian:F1} ms");
  }

  private static void WriteResult(double normalMedian, double forcedMedian, double ratio) {
    if (Environment.GetEnvironmentVariable("GAMEBOT_PERF_WRITE") != "1") {
      return;
    }

    var directory = new DirectoryInfo(AppContext.BaseDirectory);
    while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "GameBot.sln"))) {
      directory = directory.Parent;
    }

    if (directory is null) {
      return;
    }

    var path = Path.Combine(directory.FullName, "specs", "178-force-reinstall-gamepack", "quickstart.md");
    if (!File.Exists(path)) {
      return;
    }

    const string heading = "## SC-007 result";
    var text = File.ReadAllText(path, Encoding.UTF8);
    var index = text.IndexOf(heading, StringComparison.Ordinal);
    var tail = string.Empty;
    if (index >= 0) {
      // Keep the sections that follow the result section.
      var next = text.IndexOf("\n## ", index + heading.Length, StringComparison.Ordinal);
      tail = next >= 0 ? text[(next + 1)..] : string.Empty;
      text = text[..index].TrimEnd() + Environment.NewLine + Environment.NewLine;
    }
    else {
      text = text.TrimEnd() + Environment.NewLine + Environment.NewLine;
    }

    var section = new StringBuilder();
    section.AppendLine(heading);
    section.AppendLine();
    section.AppendLine("The timed test is `GamepackForceReinstallPerfTests`. It uses a pack with 200 objects, one warm-up run, and " + Runs.ToString(CultureInfo.InvariantCulture) + " runs of each kind.");
    section.AppendLine();
    section.AppendLine("| Measure | Value |");
    section.AppendLine("|---|---|");
    section.AppendLine("| Median of a normal install | " + normalMedian.ToString("F1", CultureInfo.InvariantCulture) + " ms |");
    section.AppendLine("| Median of a forced install | " + forcedMedian.ToString("F1", CultureInfo.InvariantCulture) + " ms |");
    section.AppendLine("| Ratio (forced / normal) | " + ratio.ToString("F2", CultureInfo.InvariantCulture) + " |");
    section.AppendLine("| Limit | " + MaxRatio.ToString("F1", CultureInfo.InvariantCulture) + " |");
    var result = text + section.ToString();
    if (tail.Length > 0) {
      result = result.TrimEnd() + Environment.NewLine + Environment.NewLine + tail;
    }

    File.WriteAllText(path, result, new UTF8Encoding(false));
  }
}
