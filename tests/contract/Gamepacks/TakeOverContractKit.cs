using System;
using System.Linq;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading.Tasks;
using FluentAssertions;
using GameBot.UnitTests.Gamepacks;

#pragma warning disable CA2007, CA1861, CA1707, CA2000, CA1305, CA1307, CA1310, CA1849

namespace GameBot.ContractTests.Gamepacks;

/// <summary>Helpers for the take-over contract tests (feature 149).</summary>
internal static class TakeOverContractKit {
  public const string BackupIdPattern = "^to-[0-9]{8}T[0-9]{9}-[0-9a-f]{8}$";

  /// <summary>A pack with the sequence "Help All" (one step) and the command "Tap help".</summary>
  public static string Pack(string name = "pns-daily", string version = "1.2.0") => GamepackTestKit.Pack(name, version,
    sequences: new[] { GamepackTestKit.Seq("a1", "Help All", "c1") },
    commands: new[] { GamepackTestKit.Cmd("c1", "Tap help", imageId: "help-icon") },
    images: new[] { GamepackTestKit.Img("help-icon") },
    changelog: "Take-over test.");

  /// <summary>Makes the sequence "Help All" (no step) and the command "Tap help" by hand. Both belong to the user.</summary>
  public static async Task MakeUserObjectsAsync(GamepackContractHost host) {
    (await host.Client.PostAsJsonAsync(new Uri("/api/sequences", UriKind.Relative), new { name = "Help All" })).StatusCode.Should().Be(HttpStatusCode.Created);
    (await host.Client.PostAsJsonAsync(new Uri("/api/commands", UriKind.Relative), new { name = "Tap help", steps = Array.Empty<object>() })).StatusCode.Should().Be(HttpStatusCode.Created);
  }

  public static async Task<string> OwnerKindOfSequenceAsync(GamepackContractHost host, string id) =>
    (await host.GetJsonAsync("/api/sequences/" + id)).GetProperty("owner").GetProperty("kind").GetString()!;

  public static async Task<int> StepCountOfSequenceAsync(GamepackContractHost host, string id) =>
    (await host.GetJsonAsync("/api/sequences/" + id)).GetProperty("steps").GetArrayLength();

  public static string BackupFolder(GamepackContractHost host) =>
    System.IO.Path.Combine(host.Root, "gamepacks", "takeover-backups");

  public static int BackupFileCount(GamepackContractHost host) =>
    System.IO.Directory.Exists(BackupFolder(host)) ? System.IO.Directory.GetFiles(BackupFolder(host), "*.json").Length : 0;

  public static string[] ReasonCodes(JsonElement body) =>
    body.GetProperty("reasons").EnumerateArray().Select(r => r.GetProperty("code").GetString()!).ToArray();
}
