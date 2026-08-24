using System.Collections.ObjectModel;
using System.Text.Json;
using FluentAssertions;
using GameBot.Domain.Commands;
using GameBot.Domain.Parameters;
using GameBot.Domain.QueueTemplates;
using Xunit;

namespace GameBot.UnitTests.Parameters;

/// <summary>
/// Feature 078 (FR-004, FR-032): every new member is additive and omitted when empty, so entities
/// stored before the feature load unchanged and re-serialize byte-identically. Without this the
/// operator's whole authoring store would be rewritten on first save.
/// </summary>
public sealed class PersistenceBackCompatTests {
  private static readonly JsonSerializerOptions WebOptions =
      new(JsonSerializerDefaults.Web) { WriteIndented = true };

  // ── Reading pre-feature JSON ───────────────────────────────────────────────

  [Fact]
  public void CommandWithoutParametersMemberDeserializesAsUnparametrized() {
    const string json = """
      { "id": "c1", "name": "Ensure game", "steps": [] }
      """;

    var command = JsonSerializer.Deserialize<Command>(json, WebOptions);

    command!.Parameters.Should().BeEmpty();
  }

  [Fact]
  public void CommandStepWithoutNewMembersDeserializesAsUnparametrized() {
    // Step types persist as their numeric enum value (the repositories use JsonSerializerDefaults.Web
    // with no string-enum converter), so pre-feature JSON is reproduced in that form here.
    var json = $$"""
      { "type": {{(int)CommandStepType.KeyInput}}, "order": 0, "keyInput": { "key": "KEYCODE_HOME" } }
      """;

    var step = JsonSerializer.Deserialize<CommandStep>(json, WebOptions);

    step!.Type.Should().Be(CommandStepType.KeyInput);
    step.FieldTemplates.Should().BeNull();
    step.ParameterBindings.Should().BeNull();
  }

  [Fact]
  public void SequenceWithoutParametersMemberDeserializesAsUnparametrized() {
    const string json = """
      { "id": "s1", "name": "Daily", "version": 1, "steps": [] }
      """;

    var sequence = JsonSerializer.Deserialize<CommandSequence>(json, WebOptions);

    sequence!.Parameters.Should().BeEmpty();
  }

  [Fact]
  public void TemplateEntryWithoutParameterValuesDeserializesAsEmpty() {
    const string json = """
      { "sequenceId": "s1", "enabled": true, "scheduleType": "OncePerRun" }
      """;

    var entry = JsonSerializer.Deserialize<QueueTemplateEntry>(json, WebOptions);

    entry!.ParameterValues.Should().BeEmpty();
    entry.Enabled.Should().BeTrue();
  }

  // ── Writing: the new members must not appear when empty ────────────────────

  [Fact]
  public void UnparametrizedCommandSerializesWithoutAParametersMember() {
    var command = new Command { Id = "c1", Name = "Ensure game" };

    var json = JsonSerializer.Serialize(command, WebOptions);

    json.Should().NotContain("parameters");
  }

  [Fact]
  public void UnparametrizedCommandStepSerializesWithoutTheNewMembers() {
    var step = new CommandStep {
      Type = CommandStepType.KeyInput,
      Order = 0,
      KeyInput = new KeyInputConfig { Key = "KEYCODE_HOME" }
    };

    var json = JsonSerializer.Serialize(step, WebOptions);

    json.Should().NotContain("fieldTemplates");
    json.Should().NotContain("parameterBindings");
  }

  [Fact]
  public void UnparametrizedSequenceSerializesWithoutAParametersMember() {
    var sequence = new CommandSequence { Id = "s1", Name = "Daily" };

    var json = JsonSerializer.Serialize(sequence, WebOptions);

    json.Should().NotContain("parameters");
  }

  [Fact]
  public void TemplateEntryWithoutValuesSerializesWithoutAParameterValuesMember() {
    var entry = new QueueTemplateEntry { SequenceId = "s1" };

    var json = JsonSerializer.Serialize(entry, WebOptions);

    json.Should().NotContain("parameterValues");
  }

  [Fact]
  public void PreFeatureCommandJsonRoundTripsByteIdentically() {
    const string json = """
      {
        "id": "c1",
        "name": "Ensure game",
        "triggerId": null,
        "steps": [],
        "detection": null
      }
      """;

    var command = JsonSerializer.Deserialize<Command>(json, WebOptions);
    var reserialized = JsonSerializer.Deserialize<JsonElement>(
        JsonSerializer.Serialize(command, WebOptions), WebOptions);

    reserialized.TryGetProperty("parameters", out _).Should().BeFalse();
  }

  // ── Writing: the new members DO appear once populated ──────────────────────

  [Fact]
  public void ParametrizedCommandRoundTripsItsDeclarations() {
    var command = new Command { Id = "c1", Name = "Ensure game" };
    command.Parameters.Add(new ParameterDeclaration {
      Name = "adbSerial",
      Type = ParameterValueType.Text,
      Default = "emulator-5558",
      Required = true,
      Description = "Target emulator."
    });

    var round = JsonSerializer.Deserialize<Command>(JsonSerializer.Serialize(command, WebOptions), WebOptions);

    round!.Parameters.Should().ContainSingle();
    round.Parameters[0].Name.Should().Be("adbSerial");
    round.Parameters[0].Default.Should().Be("emulator-5558");
    round.Parameters[0].Required.Should().BeTrue();
    round.Parameters[0].Description.Should().Be("Target emulator.");
  }

  [Fact]
  public void TemplateEntryRoundTripsItsParameterValues() {
    var entry = new QueueTemplateEntry { SequenceId = "s1" };
    entry.ParameterValues.Add(new ParameterBinding { Name = "adbSerial", Value = "emulator-5560" });

    var round = JsonSerializer.Deserialize<QueueTemplateEntry>(
        JsonSerializer.Serialize(entry, WebOptions), WebOptions);

    round!.ParameterValues.Should().ContainSingle(b => b.Name == "adbSerial" && b.Value == "emulator-5560");
  }

  [Fact]
  public void SequenceStepRoundTripsItsParameterBindings() {
    var step = new SequenceStep {
      Order = 0,
      StepId = "s1",
      StepType = SequenceStepType.Command,
      CommandId = "cmd",
      ParameterBindings = new Collection<ParameterBinding> {
        new() { Name = "adbSerial", Value = null }
      }
    };

    var round = JsonSerializer.Deserialize<SequenceStep>(JsonSerializer.Serialize(step, WebOptions), WebOptions);

    round!.ParameterBindings.Should().ContainSingle(b => b.Name == "adbSerial" && b.Value == null);
  }

  [Fact]
  public void CommandStepRoundTripsItsFieldTemplates() {
    var step = new CommandStep {
      Type = CommandStepType.Swipe,
      Order = 0,
      Swipe = new SwipeConfig { StartX = 1, StartY = 2, EndX = 3, EndY = 4 },
      FieldTemplates = new System.Collections.Generic.Dictionary<string, string> {
        ["swipe.startX"] = "{{originX}}"
      }
    };

    var round = JsonSerializer.Deserialize<CommandStep>(JsonSerializer.Serialize(step, WebOptions), WebOptions);

    round!.FieldTemplates.Should().ContainKey("swipe.startX");
    round.FieldTemplates!["swipe.startX"].Should().Be("{{originX}}");
  }
}
