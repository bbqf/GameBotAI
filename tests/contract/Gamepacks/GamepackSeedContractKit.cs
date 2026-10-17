using System;
using System.Linq;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading.Tasks;
using FluentAssertions;
using GameBot.UnitTests.Gamepacks;

#pragma warning disable CA2007, CA1861, CA1707, CA2000, CA1305, CA1307, CA1310, CA1849

namespace GameBot.ContractTests.Gamepacks;

/// <summary>Helpers for the seed template contract tests (feature 150).</summary>
internal static class GamepackSeedContractKit {
  /// <summary>A pack with the sequence "Help All" and the given seed templates.</summary>
  public static string Pack(string version = "1.0.0", string name = "pns-daily", object? seeds = null) => GamepackTestKit.Pack(name, version,
    sequences: new[] { GamepackTestKit.Seq("a1", "Help All") },
    seedTemplates: seeds switch { null => null, object[] many => many, _ => new[] { seeds } });

  public static object Daily() => GamepackTestKit.Seed("Daily Help", GamepackTestKit.SeedEntry("Help All", "Timer", timerTimeOfDay: "03:30:00"));

  /// <summary>The queue template list of the instance as (id, name).</summary>
  public static async Task<(string Id, string Name)[]> TemplatesAsync(GamepackContractHost host) =>
    (await host.GetJsonAsync("/api/queue-templates")).EnumerateArray()
      .Select(t => (t.GetProperty("id").GetString()!, t.GetProperty("name").GetString()!)).ToArray();

  public static async Task MakeTemplateAsync(GamepackContractHost host, string name, string sequenceName = "Help All") {
    var sequenceId = await host.SequenceIdAsync(sequenceName);
    var response = await host.Client.PostAsJsonAsync(new Uri("/api/queue-templates", UriKind.Relative),
      new { name, entries = new[] { new { sequenceId, scheduleType = "OncePerRun" } } });
    response.EnsureSuccessStatusCode();
  }

  public static string[] Names(JsonElement array) => array.EnumerateArray().Select(e => e.GetString()!).ToArray();

  public static (string Name, string Reason)[] Skips(JsonElement array) =>
    array.EnumerateArray().Select(e => (e.GetProperty("name").GetString()!, e.GetProperty("reason").GetString()!)).ToArray();
}
