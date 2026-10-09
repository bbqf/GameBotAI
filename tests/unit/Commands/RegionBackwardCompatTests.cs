using System.Text.Json;
using FluentAssertions;
using GameBot.Domain.Actions;
using GameBot.Domain.Commands;
using Xunit;

#pragma warning disable CA2007

namespace GameBot.UnitTests.Commands;

/// <summary>
/// Feature 130 (FR-008, SC-003): stored data with no region loads without error, has a null region,
/// and is written again with no region field. Data with a region round-trips.
/// </summary>
public sealed class RegionBackwardCompatTests : IDisposable {
  private readonly string _root = Path.Combine(Path.GetTempPath(), "GameBotRegionCompat", Guid.NewGuid().ToString("N"));

  public void Dispose() {
    if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
  }

  private static SequenceStep GuardedStep(PixelRegion? region) => new() {
    Order = 0,
    StepId = "s1",
    CommandId = "s1",
    StepType = SequenceStepType.Action,
    Action = new SequenceActionPayload { Type = ActionTypes.Tap },
    Condition = new ImageVisibleStepCondition { ImageId = "price-icon", MinSimilarity = 0.9, Region = region }
  };

  private async Task<(CommandSequence Loaded, string Json)> RoundTripSequenceAsync(PixelRegion? region) {
    var repo = new FileSequenceRepository(_root);
    var sequence = new CommandSequence { Id = "seq-compat", Name = "compat" };
    sequence.SetSteps(new[] { GuardedStep(region) });
    await repo.CreateAsync(sequence);
    var loaded = await repo.GetAsync("seq-compat");
    var json = await File.ReadAllTextAsync(Path.Combine(_root, "commands", "sequences", "seq-compat.json"));
    return (loaded!, json);
  }

  [Fact]
  public async Task SequenceWithNoRegionLoadsWithANullRegionAndWritesNoRegionField() {
    var (loaded, json) = await RoundTripSequenceAsync(null);

    var condition = loaded.Steps[0].Condition.Should().BeOfType<ImageVisibleStepCondition>().Subject;
    condition.Region.Should().BeNull();
    json.Should().NotContain("Region", "an image condition with no region has the same stored text as before");
  }

  [Fact]
  public async Task SequenceFileWrittenBeforeTheFeatureLoads() {
    var (_, json) = await RoundTripSequenceAsync(null);
    // The stored text holds no region. Load it again from that text only.
    var loaded = JsonSerializer.Deserialize<CommandSequence>(json);

    loaded!.Steps[0].Condition.Should().BeOfType<ImageVisibleStepCondition>().Which.Region.Should().BeNull();
  }

  [Fact]
  public async Task SequenceWithARegionRoundTrips() {
    var (loaded, json) = await RoundTripSequenceAsync(new PixelRegion(0, 400, 540, 120));

    var region = loaded.Steps[0].Condition.Should().BeOfType<ImageVisibleStepCondition>().Subject.Region;
    region.Should().NotBeNull();
    (region!.X, region.Y, region.Width, region.Height).Should().Be((0, 400, 540, 120));
    json.Should().Contain("Region");
  }

  [Fact]
  public async Task SequenceRepositoryRefusesABadRegion() {
    var repo = new FileSequenceRepository(_root);
    var sequence = new CommandSequence { Id = "seq-bad", Name = "bad" };
    sequence.SetSteps(new[] { GuardedStep(new PixelRegion(0, 0, 0, 10)) });

    var act = async () => await repo.CreateAsync(sequence);

    (await act.Should().ThrowAsync<InvalidOperationException>()).Which.Message.Should().Contain("region.width must be greater than 0");
    File.Exists(Path.Combine(_root, "commands", "sequences", "seq-bad.json")).Should().BeFalse();
  }

  [Fact]
  public async Task CommandWithNoRegionLoadsWithANullRegionAndWritesNoRegionField() {
    var repo = new FileCommandRepository(_root);
    var command = new Command { Id = "cmd-compat", Name = "compat", Detection = new DetectionTarget("tpl", 0.9) };

    await repo.AddAsync(command);
    var loaded = await repo.GetAsync("cmd-compat");
    var json = await File.ReadAllTextAsync(Path.Combine(_root, "commands", "cmd-compat.json"));

    loaded!.Detection!.Region.Should().BeNull();
    json.Should().NotContain("region");
  }

  [Fact]
  public async Task CommandWithARegionRoundTrips() {
    var repo = new FileCommandRepository(_root);
    var command = new Command { Id = "cmd-region", Name = "region", Detection = new DetectionTarget("tpl", 0.9, region: new PixelRegion(300, 400, 240, 120)) };

    await repo.AddAsync(command);
    var loaded = await repo.GetAsync("cmd-region");

    var region = loaded!.Detection!.Region;
    region.Should().NotBeNull();
    (region!.X, region.Y, region.Width, region.Height).Should().Be((300, 400, 240, 120));
  }
}
