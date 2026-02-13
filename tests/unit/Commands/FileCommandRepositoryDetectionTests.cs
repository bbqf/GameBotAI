using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Threading.Tasks;
using FluentAssertions;
using GameBot.Domain.Commands;
using Xunit;

namespace GameBot.UnitTests.Commands;

public sealed class FileCommandRepositoryDetectionTests : IDisposable {
  private readonly string _root;

  public FileCommandRepositoryDetectionTests() {
    _root = Path.Combine(Path.GetTempPath(), "GameBotCommandRepo", Guid.NewGuid().ToString("N"));
  }

  [Fact]
  public async Task AddAndGetPreservesDetectionFields() {
    var repo = new FileCommandRepository(_root);
    var command = new Command {
      Id = string.Empty,
      Name = "DetectCmd",
      Steps = new Collection<CommandStep> {
        new CommandStep { Type = CommandStepType.Action, TargetId = "a1", Order = 0 }
      },
      Detection = new DetectionTarget("template_a", 0.91, offsetX: 5, offsetY: -3, selectionStrategy: DetectionSelectionStrategy.FirstMatch)
    };

    var created = await repo.AddAsync(command).ConfigureAwait(false);
    created.Id.Should().NotBeNullOrWhiteSpace();

    var loaded = await repo.GetAsync(created.Id).ConfigureAwait(false);
    loaded.Should().NotBeNull();
    loaded!.Detection.Should().NotBeNull();
    loaded.Detection!.ReferenceImageId.Should().Be("template_a");
    loaded.Detection.Confidence.Should().BeApproximately(0.91, 0.0001);
    loaded.Detection.OffsetX.Should().Be(5);
    loaded.Detection.OffsetY.Should().Be(-3);
    loaded.Detection.SelectionStrategy.Should().Be(DetectionSelectionStrategy.FirstMatch);
  }

  [Fact]
  public async Task UpdatePersistsDetectionChanges() {
    var repo = new FileCommandRepository(_root);
    var command = new Command {
      Id = string.Empty,
      Name = "DetectCmd",
      Steps = new Collection<CommandStep> {
        new CommandStep { Type = CommandStepType.Action, TargetId = "a1", Order = 0 }
      },
      Detection = new DetectionTarget("template_a", 0.9)
    };

    var created = await repo.AddAsync(command).ConfigureAwait(false);

    created.Detection = new DetectionTarget("template_b", 0.6, offsetX: -2, offsetY: 8, selectionStrategy: DetectionSelectionStrategy.HighestConfidence);
    await repo.UpdateAsync(created).ConfigureAwait(false);

    var reloaded = await repo.GetAsync(created.Id).ConfigureAwait(false);
    reloaded.Should().NotBeNull();
    reloaded!.Detection.Should().NotBeNull();
    reloaded.Detection!.ReferenceImageId.Should().Be("template_b");
    reloaded.Detection.Confidence.Should().BeApproximately(0.6, 0.0001);
    reloaded.Detection.OffsetX.Should().Be(-2);
    reloaded.Detection.OffsetY.Should().Be(8);
    reloaded.Detection.SelectionStrategy.Should().Be(DetectionSelectionStrategy.HighestConfidence);
  }

  public void Dispose() {
    try {
      if (Directory.Exists(_root)) {
        Directory.Delete(_root, recursive: true);
      }
    } catch {
      // ignore cleanup failures
    }
  }
}
