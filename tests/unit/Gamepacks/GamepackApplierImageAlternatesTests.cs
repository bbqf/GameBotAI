using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using FluentAssertions;
using GameBot.Domain.Gamepacks;
using Xunit;

#pragma warning disable CA1707, CA2007, CA1861, CA1416

namespace GameBot.UnitTests.Gamepacks;

/// <summary>Feature 153: a gamepack image carries its alternates list through install, undo, removal, take-over, and capture.</summary>
public sealed class GamepackApplierImageAlternatesTests {
  private static readonly JsonSerializerOptions WebOptions = new(JsonSerializerDefaults.Web);

  private static string PackWith(string version, params object[] images) => GamepackTestKit.Pack("p", version, images: images);

  private static string TwoImages(string version = "1", string[]? aList = null, string[]? bList = null) =>
    PackWith(version, GamepackTestKit.ImgAlt("a", aList), GamepackTestKit.ImgAlt("b", bList));

  [Fact]
  public async Task ADryRunOfAKnownListIsValid() {
    using var kit = new GamepackTestKit();

    var result = await kit.CheckAsync(TwoImages(aList: new[] { "b" }));

    result.IsValid.Should().BeTrue();
  }

  [Fact]
  public async Task AnInstallStoresTheDeclaredListInOrderAndKeepsTheOtherImage() {
    using var kit = new GamepackTestKit();

    var outcome = await kit.Service.InstallAsync(PackWith("1", GamepackTestKit.ImgAlt("a", new[] { "c", "b" }), GamepackTestKit.ImgAlt("b", null), GamepackTestKit.ImgAlt("c", null)), false);

    outcome.Status.Should().Be(GamepackInstallStatus.Ok);
    kit.StoredAlternates("a").Should().Equal("c", "b");
    (await kit.Images.ListIdsAsync()).Should().Contain(new[] { "a", "b", "c" });
  }

  [Fact]
  public async Task ANewImageWithoutTheMemberHasAnEmptyList() {
    using var kit = new GamepackTestKit();

    (await kit.Service.InstallAsync(TwoImages(), false)).Status.Should().Be(GamepackInstallStatus.Ok);

    kit.StoredAlternates("a").Should().BeEmpty();
  }

  [Fact]
  public async Task AReinstallWithoutTheMemberKeepsTheStoredListAndTheBytes() {
    using var kit = new GamepackTestKit();
    (await kit.Service.InstallAsync(TwoImages(aList: new[] { "b" }), false)).Status.Should().Be(GamepackInstallStatus.Ok);
    var before = kit.Snapshot();

    (await kit.Service.InstallAsync(TwoImages("2"), false)).Status.Should().Be(GamepackInstallStatus.Ok);

    kit.StoredAlternates("a").Should().Equal("b");
    var after = kit.Snapshot();
    foreach (var pair in before.Where(p => p.Key.Contains("alternates", System.StringComparison.OrdinalIgnoreCase) || p.Key.EndsWith(".png", System.StringComparison.OrdinalIgnoreCase))) {
      after[pair.Key].Should().Be(pair.Value);
    }
  }

  [Fact]
  public async Task AReinstallWithAnEmptyListClearsAndWithAChangedListUpdates() {
    using var kit = new GamepackTestKit();
    (await kit.Service.InstallAsync(PackWith("1", GamepackTestKit.ImgAlt("a", new[] { "b" }), GamepackTestKit.ImgAlt("b", null), GamepackTestKit.ImgAlt("c", null)), false)).Status.Should().Be(GamepackInstallStatus.Ok);

    (await kit.Service.InstallAsync(PackWith("2", GamepackTestKit.ImgAlt("a", new[] { "c" }), GamepackTestKit.ImgAlt("b", null), GamepackTestKit.ImgAlt("c", null)), false)).Status.Should().Be(GamepackInstallStatus.Ok);
    kit.StoredAlternates("a").Should().Equal("c");

    (await kit.Service.InstallAsync(PackWith("3", GamepackTestKit.ImgAlt("a", System.Array.Empty<string>()), GamepackTestKit.ImgAlt("b", null), GamepackTestKit.ImgAlt("c", null)), false)).Status.Should().Be(GamepackInstallStatus.Ok);
    kit.StoredAlternates("a").Should().BeEmpty();
  }

  [Fact]
  public async Task AFailedApplyPutsBackTheOldListAtEveryWrite() {
    int total;
    using (var count = new GamepackTestKit()) {
      (await count.Service.InstallAsync(TwoImages(aList: new[] { "b" }), false)).Status.Should().Be(GamepackInstallStatus.Ok);
      count.Gate.Arm(0);
      (await count.Service.InstallAsync(TwoImages("2", new[] { "b" }, new[] { "a" }), false)).Status.Should().Be(GamepackInstallStatus.Ok);
      total = count.Gate.Calls;
    }

    for (var n = 1; n <= total; n++) {
      using var kit = new GamepackTestKit();
      (await kit.Service.InstallAsync(TwoImages(aList: new[] { "b" }), false)).Status.Should().Be(GamepackInstallStatus.Ok);
      var before = kit.Snapshot();
      kit.Gate.Arm(n);

      var outcome = await kit.Service.InstallAsync(TwoImages("2", System.Array.Empty<string>(), new[] { "a" }), false);

      outcome.Status.Should().Be(GamepackInstallStatus.Failed, "a fault at call " + n);
      kit.Snapshot().Should().BeEquivalentTo(before, "a fault at call " + n);
    }
  }

  [Fact]
  public async Task AFailedFirstInstallLeavesNoList() {
    int total;
    using (var count = new GamepackTestKit()) {
      count.Gate.Arm(0);
      (await count.Service.InstallAsync(TwoImages(aList: new[] { "b" }), false)).Status.Should().Be(GamepackInstallStatus.Ok);
      total = count.Gate.Calls;
    }

    for (var n = 1; n <= total; n++) {
      using var kit = new GamepackTestKit();
      var before = kit.Snapshot();
      kit.Gate.Arm(n);

      (await kit.Service.InstallAsync(TwoImages(aList: new[] { "b" }), false)).Status.Should().Be(GamepackInstallStatus.Failed);

      kit.Snapshot().Should().BeEquivalentTo(before, "a fault at call " + n);
      kit.StoredAlternates("a").Should().BeEmpty();
    }
  }

  [Fact]
  public async Task AnImageThatTheInstallRemovesLosesItsList() {
    using var kit = new GamepackTestKit();
    (await kit.Service.InstallAsync(TwoImages(aList: new[] { "b" }), false)).Status.Should().Be(GamepackInstallStatus.Ok);
    kit.StoredAlternates("a").Should().Equal("b");

    (await kit.Service.InstallAsync(PackWith("2", GamepackTestKit.ImgAlt("b", null)), false)).Status.Should().Be(GamepackInstallStatus.Ok);

    (await kit.Images.ListIdsAsync()).Should().NotContain("a");
    kit.StoredAlternates("a").Should().BeEmpty();
  }

  [Fact]
  public async Task ATakeOverWritesTheDeclaredListAndARestoreGivesTheUserListBack() {
    using var kit = new GamepackTestKit();
    await kit.AddUserObjectsAsync(PackWith("0", GamepackTestKit.Img("a"), GamepackTestKit.Img("b"), GamepackTestKit.Img("c")));
    kit.Alternates.SetAlternates("a", new[] { "c" });

    var outcome = await kit.InstallTakeOverAsync(PackWith("1", GamepackTestKit.ImgAlt("a", new[] { "b" }), GamepackTestKit.ImgAlt("b", null), GamepackTestKit.ImgAlt("c", null)));

    outcome.Status.Should().Be(GamepackInstallStatus.Ok);
    outcome.TakenOver.Should().Be(3);
    kit.StoredAlternates("a").Should().Equal("b");
    var backup = kit.TakeOverBackups.Read(outcome.TakeOverBackupId!)!;
    var json = JsonSerializer.Serialize(backup, WebOptions);
    backup.Entries.Single(e => e.Name == "a").Image!.Alternates.Should().Equal("c");
    backup.Entries.Single(e => e.Name == "b").Image!.Alternates.Should().BeNull();
    json.Split("\"alternates\"").Length.Should().Be(2, "only the image with a list has the member");

    (await kit.Service.RestoreTakeOverAsync(outcome.TakeOverBackupId!)).Status.Should().Be(TakeOverRestoreStatus.Restored);
    kit.StoredAlternates("a").Should().Equal("c");
    kit.StoredAlternates("b").Should().BeEmpty();
  }

  [Fact]
  public async Task ACaptureWritesTheListOnlyWhenItIsNotEmpty() {
    using var kit = new GamepackTestKit();
    (await kit.Service.InstallAsync(TwoImages(aList: new[] { "b" }), false)).Status.Should().Be(GamepackInstallStatus.Ok);
    var registry = kit.Store.Load();
    var record = registry.Gamepacks.Values.Single();

    var text = await kit.Applier.CaptureLiveTextAsync(record, registry);

    var images = JsonDocument.Parse(text).RootElement.GetProperty("images").EnumerateArray().ToDictionary(e => e.GetProperty("id").GetString()!);
    images["a"].GetProperty("alternates").EnumerateArray().Select(e => e.GetString()).Should().Equal("b");
    images["b"].TryGetProperty("alternates", out _).Should().BeFalse();
  }

  [Fact]
  public async Task AnUnknownImageMemberRefusesTheInstallAndCreatesNothing() {
    using var kit = new GamepackTestKit();
    var before = kit.Snapshot();
    var text = GamepackTestKit.Pack("p", "1", images: new object[] { new { id = "a", contentType = "image/png", dataBase64 = GamepackTestKit.PngBase64, bogusMember = 1 } });

    var outcome = await kit.Service.InstallAsync(text, false);

    outcome.Status.Should().Be(GamepackInstallStatus.Invalid);
    kit.Snapshot().Should().BeEquivalentTo(before);
    (await kit.Images.ListIdsAsync()).Should().BeEmpty();
  }

  [Fact]
  public async Task AnInvalidListRefusesTheInstallAndCreatesNothing() {
    using var kit = new GamepackTestKit();
    var before = kit.Snapshot();

    var outcome = await kit.Service.InstallAsync(PackWith("1", GamepackTestKit.ImgAlt("a", new[] { "a" })), false);

    outcome.Status.Should().Be(GamepackInstallStatus.Invalid);
    kit.Snapshot().Should().BeEquivalentTo(before);
  }
}
