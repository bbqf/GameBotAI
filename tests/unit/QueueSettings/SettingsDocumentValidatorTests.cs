using System.Linq;
using System.Threading.Tasks;
using GameBot.Domain.QueueSettings;
using GameBot.UnitTests.QueueSettings.TestDoubles;
using Xunit;

#pragma warning disable CA2007, CA1707

namespace GameBot.UnitTests.QueueSettings;

public sealed class SettingsDocumentValidatorTests {
  private static async Task AssertRefusedWithoutWritesAsync(QueueSettingsTestKit kit, string json, string expectedText) {
    var outcome = await kit.Importer.ImportAsync(json);

    Assert.Equal(ImportStatus.Invalid, outcome.Status);
    Assert.Contains(outcome.Errors, e => e.Contains(expectedText, System.StringComparison.Ordinal));
    Assert.Equal(0, kit.Probe.Writes);
    Assert.Equal(0, kit.Probe.Deletes);
  }

  [Fact]
  public async Task BadJsonIsRefused() {
    using var kit = new QueueSettingsTestKit();
    await AssertRefusedWithoutWritesAsync(kit, "{ not json", "not valid JSON");
  }

  [Fact]
  public async Task BodyThatIsNotAnObjectIsRefused() {
    using var kit = new QueueSettingsTestKit();
    await AssertRefusedWithoutWritesAsync(kit, "[1,2]", "JSON object");
  }

  [Fact]
  public async Task FormatVersionTwoIsRefused() {
    using var kit = new QueueSettingsTestKit();
    await AssertRefusedWithoutWritesAsync(kit, QueueSettingsTestKit.Doc(formatVersion: 2), "not supported");
  }

  [Fact]
  public async Task MissingFormatVersionIsRefused() {
    using var kit = new QueueSettingsTestKit();
    await AssertRefusedWithoutWritesAsync(kit, "{\"queues\":[]}", "formatVersion");
  }

  [Fact]
  public async Task EmptyNameIsRefused() {
    using var kit = new QueueSettingsTestKit();
    var json = QueueSettingsTestKit.Doc(sequences: QueueSettingsTestKit.List(QueueSettingsTestKit.SequenceJson("")));
    await AssertRefusedWithoutWritesAsync(kit, json, "has no name");
  }

  [Fact]
  public async Task DuplicateSequenceNamesAreRefusedAndNamed() {
    using var kit = new QueueSettingsTestKit();
    var json = QueueSettingsTestKit.Doc(sequences: QueueSettingsTestKit.List(
      QueueSettingsTestKit.SequenceJson("Daily"), QueueSettingsTestKit.SequenceJson("Daily")));
    await AssertRefusedWithoutWritesAsync(kit, json, "'Daily'");
  }

  [Fact]
  public async Task NamesThatDifferOnlyInCaseAreDuplicates() {
    using var kit = new QueueSettingsTestKit();
    var json = QueueSettingsTestKit.Doc(commands: QueueSettingsTestKit.List(
      QueueSettingsTestKit.CommandJsonText("Back"), QueueSettingsTestKit.CommandJsonText("BACK")));
    await AssertRefusedWithoutWritesAsync(kit, json, "used more than once");
  }

  [Fact]
  public async Task DuplicateQueueAndTemplateNamesAreRefused() {
    using var kit = new QueueSettingsTestKit();
    var json = QueueSettingsTestKit.Doc(
      queues: QueueSettingsTestKit.List(QueueSettingsTestKit.QueueJson("Q"), QueueSettingsTestKit.QueueJson("q")),
      templates: QueueSettingsTestKit.List(QueueSettingsTestKit.TemplateJson("T"), QueueSettingsTestKit.TemplateJson("t")));

    var outcome = await kit.Importer.ImportAsync(json);

    Assert.Equal(ImportStatus.Invalid, outcome.Status);
    Assert.Equal(2, outcome.Errors.Count);
    Assert.Equal(0, kit.Probe.Writes);
  }

  [Fact]
  public void ValidDocumentGivesParsedDocument() {
    var json = QueueSettingsTestKit.Doc(
      queues: QueueSettingsTestKit.List(QueueSettingsTestKit.QueueJson("Q")),
      sequences: QueueSettingsTestKit.List(QueueSettingsTestKit.SequenceJson("S", "C")),
      commands: QueueSettingsTestKit.List(QueueSettingsTestKit.CommandJsonText("C")));

    var result = SettingsDocumentValidator.Validate(json);

    Assert.True(result.IsValid);
    Assert.Single(result.Document!.Queues);
    Assert.Equal("S", SettingsDocumentValidator.NameOf(result.Document.Sequences.Single()));
  }
}
