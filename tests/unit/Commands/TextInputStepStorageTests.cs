using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Threading.Tasks;
using FluentAssertions;
using GameBot.Domain.Commands;
using Xunit;

namespace GameBot.UnitTests.Commands;

/// <summary>Feature 192: a TextInput step saves and loads, and other commands keep the same JSON.</summary>
public sealed class TextInputStepStorageTests : IDisposable {
  private readonly string _root = Path.Combine(Path.GetTempPath(), "GameBotTextInputRepo", Guid.NewGuid().ToString("N"));

  [Fact]
  public async Task ATextInputStepKeepsItsTextAfterSaveAndLoad() {
    var repo = new FileCommandRepository(_root);
    var created = await repo.AddAsync(new Command {
      Id = string.Empty,
      Name = "Type",
      Steps = new Collection<CommandStep> {
        new() {
          Type = CommandStepType.TextInput,
          Order = 0,
          TextInput = new TextInputConfig { Text = "250" },
          FieldTemplates = new Dictionary<string, string> { ["textInput.text"] = "{{fighterBatch}}" }
        }
      }
    }).ConfigureAwait(false);

    var loaded = await repo.GetAsync(created.Id).ConfigureAwait(false);

    var step = loaded!.Steps.Should().ContainSingle().Subject;
    step.Type.Should().Be(CommandStepType.TextInput);
    step.TextInput!.Text.Should().Be("250");
    step.FieldTemplates.Should().ContainKey("textInput.text");
  }

  [Fact]
  public async Task ACommandWithoutATextInputStepHasNoTextInputPropertyInItsJson() {
    var repo = new FileCommandRepository(_root);
    var created = await repo.AddAsync(new Command {
      Id = string.Empty,
      Name = "Key",
      Steps = new Collection<CommandStep> {
        new() { Type = CommandStepType.KeyInput, Order = 0, KeyInput = new KeyInputConfig { Key = "BACK" } }
      }
    }).ConfigureAwait(false);

    var json = await File.ReadAllTextAsync(Path.Combine(_root, "commands", created.Id + ".json")).ConfigureAwait(false);

    json.Should().NotContainEquivalentOf("textInput");
  }

  public void Dispose() {
    try {
      if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
    }
#pragma warning disable CA1031 // cleanup in a test
    catch (IOException) {
      // ignore cleanup failures
    }
#pragma warning restore CA1031
  }
}
