using System;
using System.Linq;
using System.Threading.Tasks;
using GameBot.Domain.Gamepacks;
using GameBot.Domain.QueueSettings;
using GameBot.UnitTests.Gamepacks;
using GameBot.UnitTests.QueueSettings.TestDoubles;
using Xunit;

#pragma warning disable CA2007, CA1707, CA1861

namespace GameBot.UnitTests.QueueSettings;

public sealed class QueueSettingsExporterTests {
  private static async Task InstallPackAsync(QueueSettingsTestKit kit, string name = "pk", string version = "1.0.0") {
    var pack = GamepackTestKit.Pack(
      name,
      version,
      sequences: new[] { GamepackTestKit.Seq("ps", "PackSeq", "pc") },
      commands: new[] { GamepackTestKit.Cmd("pc", "PackCmd", imageId: "packimg") },
      images: new[] { GamepackTestKit.Img("packimg") });
    var outcome = await kit.Packs.Service.InstallAsync(pack, false);
    Assert.Equal(GamepackInstallStatus.Ok, outcome.Status);
  }

  [Fact]
  public async Task EmptyDataGivesValidDocument() {
    using var kit = new QueueSettingsTestKit();

    var doc = await kit.ExportAsync();

    Assert.Equal(1, doc.FormatVersion);
    Assert.Empty(doc.Queues);
    Assert.Empty(doc.QueueTemplates);
    Assert.Empty(doc.Sequences);
    Assert.Empty(doc.Commands);
    Assert.Empty(doc.References);
  }

  [Fact]
  public async Task DocumentHasNoIdAndNoTimeStamp() {
    using var kit = new QueueSettingsTestKit();
    var command = await kit.AddCommandAsync("Back");
    var sequence = await kit.AddSequenceAsync("Daily", command.Id);
    var template = await kit.AddTemplateAsync("T1", sequence.Id);
    var game = await kit.AddGameAsync("Game One");
    var queue = await kit.AddQueueAsync("Q1", template.Id, game.Id, sequence.Id);

    var json = await kit.ExportJsonAsync();

    foreach (var id in new[] { command.Id, sequence.Id, template.Id, game.Id, queue.Id }) {
      Assert.DoesNotContain(id, json, StringComparison.Ordinal);
    }

    Assert.DoesNotContain("\"id\"", json, StringComparison.OrdinalIgnoreCase);
    Assert.DoesNotContain("createdAt", json, StringComparison.OrdinalIgnoreCase);
    Assert.DoesNotContain("updatedAt", json, StringComparison.OrdinalIgnoreCase);
    Assert.DoesNotContain("triggerId", json, StringComparison.OrdinalIgnoreCase);
    Assert.Contains("\"linkedTemplate\": \"T1\"", json, StringComparison.Ordinal);
    Assert.Contains("\"linkedGame\": \"Game One\"", json, StringComparison.Ordinal);
  }

  [Fact]
  public async Task OwnedObjectsAreOnlyReferences() {
    using var kit = new QueueSettingsTestKit();
    await InstallPackAsync(kit);
    var packCommand = await kit.Packs.CommandByNameAsync("PackCmd");
    var packSequence = await kit.Packs.SequenceByNameAsync("PackSeq");
    var mine = await kit.AddSequenceAsync("Mine", packCommand!.Id);
    var template = await kit.AddTemplateAsync("Daily", packSequence!.Id, mine.Id);
    await kit.AddQueueAsync("Q1", template.Id, null, packSequence.Id);

    var doc = await kit.ExportAsync();

    Assert.Equal(new[] { "Mine" }, doc.Sequences.Select(s => SettingsDocumentValidator.NameOf(s)).ToArray());
    Assert.Empty(doc.Commands);
    Assert.Contains(doc.References, r => r.Type == "command" && r.Name == "PackCmd" && r.Gamepack == "pk" && r.Version == "1.0.0" && r.UsedBy == "sequence:Mine");
    Assert.Contains(doc.References, r => r.Type == "sequence" && r.Name == "PackSeq" && r.Gamepack == "pk" && r.UsedBy == "queueTemplate:Daily");
    Assert.Contains(doc.References, r => r.Type == "sequence" && r.Name == "PackSeq" && r.Gamepack == "pk" && r.UsedBy == "queue:Q1");
    Assert.Single(doc.QueueTemplates.Single().Entries.Where(e => e.Sequence == "Mine"));
  }

  [Fact]
  public async Task OwnedImageIsOneNameReferenceWithoutContent() {
    using var kit = new QueueSettingsTestKit();
    await InstallPackAsync(kit);
    await kit.AddImageCommandAsync("UsesPackImage", "packimg");
    await kit.AddImageCommandAsync("UsesOwnImage", "myimg");

    var doc = await kit.ExportAsync();

    var owned = Assert.Single(doc.References, r => r.Type == "image" && r.Name == "packimg");
    Assert.Equal("pk", owned.Gamepack);
    Assert.Equal("1.0.0", owned.Version);
    Assert.Equal("command:UsesPackImage", owned.UsedBy);
    var user = Assert.Single(doc.References, r => r.Type == "image" && r.Name == "myimg");
    Assert.Null(user.Gamepack);
    var json = SettingsDocumentJson.Serialize(doc);
    Assert.DoesNotContain(GamepackTestKit.PngBase64, json, StringComparison.Ordinal);
    Assert.DoesNotContain("dataBase64", json, StringComparison.OrdinalIgnoreCase);
  }

  [Fact]
  public async Task MachineFieldsAreExportedAsStored() {
    using var kit = new QueueSettingsTestKit();
    var queue = await kit.AddQueueAsync("Q1");
    queue.EmulatorInstanceName = "LD-1";
    queue.EmulatorInstanceIndex = 3;
    await kit.Queues.UpdateAsync(queue);

    var doc = await kit.ExportAsync();

    var exported = Assert.Single(doc.Queues);
    Assert.Equal("emulator-5554", exported.EmulatorSerial);
    Assert.Equal("LD-1", exported.EmulatorInstanceName);
    Assert.Equal(3, exported.EmulatorInstanceIndex);
  }
}
