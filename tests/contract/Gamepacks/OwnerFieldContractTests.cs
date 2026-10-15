using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using GameBot.UnitTests.Gamepacks;
using Xunit;

#pragma warning disable CA2007, CA1861, CA1707, CA2000

namespace GameBot.ContractTests.Gamepacks;

/// <summary>Feature 143 (T015): the read-only owner field on sequence, command, and image reads (FR-007).</summary>
public sealed class OwnerFieldContractTests {
  private static string Pack() => GamepackTestKit.Pack("pns-daily", "1.2.0",
    sequences: new[] { GamepackTestKit.Seq("a1", "Help All", "c1") },
    commands: new[] { GamepackTestKit.Cmd("c1", "Tap help", imageId: "help-icon") },
    images: new[] { GamepackTestKit.Img("help-icon") });

  [Fact]
  public async Task SingleAndListReadsShowTheGamepackOwner() {
    using var host = new GamepackContractHost();
    (await host.InstallAsync(Pack())).StatusCode.Should().Be(HttpStatusCode.OK);
    var seqId = await host.SequenceIdAsync("Help All");
    var cmdId = await host.CommandIdAsync("Tap help");

    var seq = await host.GetJsonAsync("/api/sequences/" + seqId);
    seq.GetProperty("owner").GetProperty("kind").GetString().Should().Be("gamepack");
    seq.GetProperty("owner").GetProperty("name").GetString().Should().Be("pns-daily");
    seq.GetProperty("owner").GetProperty("version").GetString().Should().Be("1.2.0");

    var cmd = await host.GetJsonAsync("/api/commands/" + cmdId);
    cmd.GetProperty("owner").GetProperty("name").GetString().Should().Be("pns-daily");

    var image = await host.GetJsonAsync("/api/images/help-icon/metadata");
    image.GetProperty("owner").GetProperty("version").GetString().Should().Be("1.2.0");

    (await host.GetJsonAsync("/api/sequences"))[0].GetProperty("owner").GetProperty("kind").GetString().Should().Be("gamepack");
    (await host.GetJsonAsync("/api/commands"))[0].GetProperty("owner").GetProperty("kind").GetString().Should().Be("gamepack");
    var images = await host.GetJsonAsync("/api/images");
    images.GetProperty("ids")[0].GetString().Should().Be("help-icon");
    images.GetProperty("items")[0].GetProperty("owner").GetProperty("name").GetString().Should().Be("pns-daily");
  }

  [Fact]
  public async Task AHandMadeObjectShowsTheUserAsOwner() {
    using var host = new GamepackContractHost();
    var seq = await host.Client.PostAsJsonAsync(new Uri("/api/sequences", UriKind.Relative), new { name = "Mine" });
    seq.StatusCode.Should().Be(HttpStatusCode.Created);
    var cmd = await host.Client.PostAsJsonAsync(new Uri("/api/commands", UriKind.Relative), new { name = "My command", steps = Array.Empty<object>() });
    cmd.StatusCode.Should().Be(HttpStatusCode.Created);
    var img = await host.Client.PostAsJsonAsync(new Uri("/api/images", UriKind.Relative), new { id = "my-image", data = GamepackTestKit.PngBase64 });
    img.StatusCode.Should().Be(HttpStatusCode.Created);

    var seqId = await host.SequenceIdAsync("Mine");
    var cmdId = await host.CommandIdAsync("My command");

    var user = (await host.GetJsonAsync("/api/sequences/" + seqId)).GetProperty("owner");
    user.GetProperty("kind").GetString().Should().Be("user");
    user.TryGetProperty("name", out _).Should().BeFalse();
    (await host.GetJsonAsync("/api/commands/" + cmdId)).GetProperty("owner").GetProperty("kind").GetString().Should().Be("user");
    (await host.GetJsonAsync("/api/images/my-image/metadata")).GetProperty("owner").GetProperty("kind").GetString().Should().Be("user");
  }

  [Fact]
  public async Task AWriteRequestIgnoresTheOwnerField() {
    using var host = new GamepackContractHost();
    var fakeOwner = new { kind = "gamepack", name = "fake", version = "9" };

    var seq = await host.Client.PostAsJsonAsync(new Uri("/api/sequences", UriKind.Relative), new { name = "With owner", owner = fakeOwner });
    seq.StatusCode.Should().Be(HttpStatusCode.Created);
    var cmd = await host.Client.PostAsJsonAsync(new Uri("/api/commands", UriKind.Relative), new { name = "Cmd with owner", steps = Array.Empty<object>(), owner = fakeOwner });
    cmd.StatusCode.Should().Be(HttpStatusCode.Created);

    var seqId = await host.SequenceIdAsync("With owner");
    var cmdId = await host.CommandIdAsync("Cmd with owner");
    (await host.GetJsonAsync("/api/sequences/" + seqId)).GetProperty("owner").GetProperty("kind").GetString().Should().Be("user");
    (await host.GetJsonAsync("/api/commands/" + cmdId)).GetProperty("owner").GetProperty("kind").GetString().Should().Be("user");

    var patch = await host.Client.PatchAsJsonAsync(new Uri("/api/commands/" + cmdId, UriKind.Relative), new { name = "Renamed", owner = fakeOwner });
    patch.StatusCode.Should().Be(HttpStatusCode.OK);
    (await host.GetJsonAsync("/api/commands/" + cmdId)).GetProperty("owner").GetProperty("kind").GetString().Should().Be("user");
  }
}
