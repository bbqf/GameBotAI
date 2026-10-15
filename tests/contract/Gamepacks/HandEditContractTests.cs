using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using GameBot.UnitTests.Gamepacks;
using Xunit;

#pragma warning disable CA2007, CA1861, CA1707, CA2000

namespace GameBot.ContractTests.Gamepacks;

/// <summary>Feature 143 (T027): hand edit, hand create, and hand delete of gamepack-owned objects (FR-015, US2).</summary>
public sealed class HandEditContractTests {
  private static string Pack() => GamepackTestKit.Pack("pns-daily", "1.0",
    sequences: new[] { GamepackTestKit.Seq("a1", "Help All") },
    commands: new[] { GamepackTestKit.Cmd("c1", "Tap help") },
    images: new[] { GamepackTestKit.Img("help-icon") });

  [Fact]
  public async Task AHandEditOfAnOwnedObjectIsAcceptedAndTheOwnerStays() {
    using var host = new GamepackContractHost();
    (await host.InstallAsync(Pack())).StatusCode.Should().Be(HttpStatusCode.OK);
    var cmdId = await host.CommandIdAsync("Tap help");

    var patch = await host.Client.PatchAsJsonAsync(new Uri("/api/commands/" + cmdId, UriKind.Relative), new { triggerId = (string?)null, name = "Tap help" });
    patch.StatusCode.Should().Be(HttpStatusCode.OK);

    var owner = (await host.GetJsonAsync("/api/commands/" + cmdId)).GetProperty("owner");
    owner.GetProperty("kind").GetString().Should().Be("gamepack");
    owner.GetProperty("name").GetString().Should().Be("pns-daily");

    var seqId = await host.SequenceIdAsync("Help All");
    var put = await host.Client.PutAsJsonAsync(new Uri("/api/sequences/" + seqId, UriKind.Relative), new { name = "Help All", steps = Array.Empty<object>() });
    put.StatusCode.Should().BeOneOf(HttpStatusCode.OK, HttpStatusCode.BadRequest);
    (await host.GetJsonAsync("/api/sequences/" + seqId)).GetProperty("owner").GetProperty("kind").GetString().Should().Be("gamepack");
  }

  [Fact]
  public async Task AHandCreateWithTheNameOfAnOwnedObjectGives409AndTheOwnerStays() {
    using var host = new GamepackContractHost();
    (await host.InstallAsync(Pack())).StatusCode.Should().Be(HttpStatusCode.OK);

    var seq = await host.Client.PostAsJsonAsync(new Uri("/api/sequences", UriKind.Relative), new { name = "help all" });
    seq.StatusCode.Should().Be(HttpStatusCode.Conflict);
    var cmd = await host.Client.PostAsJsonAsync(new Uri("/api/commands", UriKind.Relative), new { name = "TAP HELP", steps = Array.Empty<object>() });
    cmd.StatusCode.Should().Be(HttpStatusCode.Conflict);
    var img = await host.Client.PostAsJsonAsync(new Uri("/api/images", UriKind.Relative), new { id = "help-icon", data = GamepackTestKit.PngBase64 });
    img.StatusCode.Should().Be(HttpStatusCode.Conflict);

    (await host.GetJsonAsync("/api/sequences")).GetArrayLength().Should().Be(1);
    (await host.GetJsonAsync("/api/sequences"))[0].GetProperty("owner").GetProperty("name").GetString().Should().Be("pns-daily");
    (await host.GetJsonAsync("/api/images/help-icon/metadata")).GetProperty("owner").GetProperty("kind").GetString().Should().Be("gamepack");
  }

  [Fact]
  public async Task AHandCreateWithAnyOtherNameWorksAsBefore() {
    using var host = new GamepackContractHost();
    (await host.InstallAsync(Pack())).StatusCode.Should().Be(HttpStatusCode.OK);
    (await host.Client.PostAsJsonAsync(new Uri("/api/sequences", UriKind.Relative), new { name = "Other" })).StatusCode.Should().Be(HttpStatusCode.Created);
  }

  [Fact]
  public async Task AHandDeleteOfAnOwnedObjectWorksAndTheNextHandMadeObjectBelongsToTheUser() {
    using var host = new GamepackContractHost();
    (await host.InstallAsync(Pack())).StatusCode.Should().Be(HttpStatusCode.OK);
    var seqId = await host.SequenceIdAsync("Help All");

    (await host.Client.DeleteAsync(new Uri("/api/sequences/" + seqId, UriKind.Relative))).StatusCode.Should().Be(HttpStatusCode.NoContent);
    (await host.Client.PostAsJsonAsync(new Uri("/api/sequences", UriKind.Relative), new { name = "Help All" })).StatusCode.Should().Be(HttpStatusCode.Created);

    var newId = await host.SequenceIdAsync("Help All");
    (await host.GetJsonAsync("/api/sequences/" + newId)).GetProperty("owner").GetProperty("kind").GetString().Should().Be("user");

    // The next install is refused: the hand-made object has the name (US2 scenario 6).
    var again = await host.InstallAsync(Pack());
    again.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
  }

  [Fact]
  public async Task AHandDeletedOwnedObjectIsCreatedAgainByTheNextInstall() {
    using var host = new GamepackContractHost();
    (await host.InstallAsync(Pack())).StatusCode.Should().Be(HttpStatusCode.OK);
    var cmdId = await host.CommandIdAsync("Tap help");
    (await host.Client.DeleteAsync(new Uri("/api/commands/" + cmdId, UriKind.Relative))).StatusCode.Should().Be(HttpStatusCode.NoContent);

    var again = await host.InstallAsync(Pack());

    again.StatusCode.Should().Be(HttpStatusCode.OK);
    var body = await GamepackContractHost.ReadAsync(again);
    body.GetProperty("created").GetInt32().Should().Be(1);
    body.GetProperty("updated").GetInt32().Should().Be(2);
  }
}
