using System;
using System.Linq;
using System.Threading.Tasks;
using FluentAssertions;
using GameBot.Domain.Commands;
using GameBot.Domain.Gamepacks;
using GameBot.UnitTests.Gamepacks;
using Xunit;

#pragma warning disable CA2007, CA1861, CA1707, CA1416

namespace GameBot.IntegrationTests.Gamepacks;

/// <summary>Feature 143 (T016): install and update on real file stores (SC-002).</summary>
public sealed class GamepackInstallIntegrationTests : IDisposable {
  private readonly GamepackTestKit _kit = new();

  public void Dispose() => _kit.Dispose();

  private async Task<GamepackInstallOutcome> InstallAsync(string json) {
    var outcome = await _kit.Service.InstallAsync(json, dryRun: false);
    outcome.Status.Should().Be(GamepackInstallStatus.Ok, string.Join("; ", outcome.Problems.Select(p => p.Message)) + outcome.ErrorMessage);
    return outcome;
  }

  private static string V1() => GamepackTestKit.Pack("pns-daily", "1.0",
    sequences: new[] { GamepackTestKit.Seq("s1", "Daily", "c1"), GamepackTestKit.Seq("s2", "Dropped") },
    commands: new[] { GamepackTestKit.Cmd("c1", "Tap", imageId: "icon") },
    images: new[] { GamepackTestKit.Img("icon") });

  private static string V2() => GamepackTestKit.Pack("pns-daily", "2.0",
    sequences: new[] { GamepackTestKit.Seq("x1", "Daily", "x2"), GamepackTestKit.Seq("x3", "Added") },
    commands: new[] { GamepackTestKit.Cmd("x2", "Tap", imageId: "icon") },
    images: new[] { GamepackTestKit.Img("icon") });

  [Fact]
  public async Task AFreshInstallMakesEveryObjectWithTheGamepackAsOwner() {
    var outcome = await InstallAsync(V1());

    outcome.Created.Should().Be(4);
    foreach (var seq in await _kit.Sequences.ListAsync()) {
      _kit.OwnerOf("sequence", seq.Id).ToText().Should().Be("pns-daily 1.0");
    }

    foreach (var cmd in await _kit.Commands.ListAsync()) {
      _kit.OwnerOf("command", cmd.Id).ToText().Should().Be("pns-daily 1.0");
    }

    _kit.OwnerOf("image", "icon").ToText().Should().Be("pns-daily 1.0");
    _kit.Service.List().Should().ContainSingle(g => g.Name == "pns-daily" && !g.HasPreviousVersion);
  }

  [Fact]
  public async Task AnUpdateChangesTheOwnerVersionKeepsIdsAndRemovesDroppedObjects() {
    await InstallAsync(V1());
    var dailyId = (await _kit.SequenceByNameAsync("Daily"))!.Id;
    var tapId = (await _kit.CommandByNameAsync("Tap"))!.Id;

    var outcome = await InstallAsync(V2());

    outcome.Created.Should().Be(1);
    outcome.Updated.Should().Be(3);
    outcome.Removed.Should().Be(1);
    (await _kit.SequenceByNameAsync("Dropped")).Should().BeNull();
    var daily = (await _kit.SequenceByNameAsync("Daily"))!;
    daily.Id.Should().Be(dailyId);
    daily.Steps.Single().CommandId.Should().Be(tapId);
    _kit.OwnerOf("sequence", daily.Id).ToText().Should().Be("pns-daily 2.0");
    _kit.OwnerOf("sequence", (await _kit.SequenceByNameAsync("Added"))!.Id).ToText().Should().Be("pns-daily 2.0");
    _kit.Store.Load().Gamepacks["pns-daily"].Previous!.Version.Should().Be("1.0");
  }

  [Fact]
  public async Task ASameVersionReinstallKeepsTheOldPreviousVersion() {
    await InstallAsync(V1());
    await InstallAsync(V2());
    var previousBefore = _kit.Store.ReadPrevious("pns-daily");

    await InstallAsync(V2());

    _kit.Store.ReadPrevious("pns-daily").Should().Be(previousBefore);
    _kit.Store.Load().Gamepacks["pns-daily"].Previous!.Version.Should().Be("1.0");
  }

  [Fact]
  public async Task AnEmptyGamepackInstallsAndIsListed() {
    await InstallAsync(GamepackTestKit.Pack("empty", "1"));
    _kit.Service.List().Should().ContainSingle(g => g.Name == "empty");
  }

  [Fact]
  public async Task AFailedInstallOfVersionTwoKeepsThePreviousFileAndRegistryAsBefore() {
    await InstallAsync(V1());
    var before = _kit.Snapshot();
    _kit.Gate.Arm(7);

    var outcome = await _kit.Service.InstallAsync(V2(), dryRun: false);

    outcome.Status.Should().Be(GamepackInstallStatus.Failed);
    _kit.Snapshot().Should().BeEquivalentTo(before);
  }

  [Fact]
  public async Task TwoInstallsAtOnceRunOneAfterTheOther() {
    var first = _kit.Service.InstallAsync(GamepackTestKit.Pack("one", "1", sequences: new[] { GamepackTestKit.Seq("a", "A") }), false);
    var second = _kit.Service.InstallAsync(GamepackTestKit.Pack("two", "1", sequences: new[] { GamepackTestKit.Seq("b", "B") }), false);
    var results = await Task.WhenAll(first, second);
    results.Should().OnlyContain(r => r.Status == GamepackInstallStatus.Ok);
    _kit.Service.List().Should().HaveCount(2);
  }

  // Feature 155: a step with a command reference that no command resolves.
  private static object SeqWithReference(string commandId, string? commandName) => new {
    id = "sq",
    name = "Seq",
    version = 1,
    steps = new[] {
      new { order = 0, stepId = "s0", stepType = "Action", commandReference = new { commandId, commandName } }
    }
  };

  [Theory]
  [InlineData("ghost", null)]
  [InlineData("", "Ghost Name")]
  public async Task AnUnresolvedCommandReferenceGivesTheSameProblemInDryRunAndInstallAndWritesNothing(string id, string? name) {
    var json = GamepackTestKit.Pack("p", "1", sequences: new[] { SeqWithReference(id, name) });
    var before = _kit.Snapshot();

    var dry = await _kit.Service.InstallAsync(json, dryRun: true);
    var real = await _kit.Service.InstallAsync(json, dryRun: false);

    dry.Status.Should().Be(GamepackInstallStatus.Invalid);
    real.Status.Should().Be(GamepackInstallStatus.Invalid);
    var expected = $"Command '{(!string.IsNullOrEmpty(id) ? id : name)}' is not in the gamepack or in the installation.";
    dry.Problems.Should().ContainSingle(p => p.Category == "reference" && p.Message == expected);
    real.Problems.Should().BeEquivalentTo(dry.Problems);
    _kit.Snapshot().Should().BeEquivalentTo(before);
    (await _kit.Sequences.ListAsync()).Should().BeEmpty();
  }

  [Fact]
  public async Task ResolvedCommandReferencesStillInstall() {
    var json = GamepackTestKit.Pack("p", "1",
      sequences: new[] {
        SeqWithReference("c1", null),
        new { id = "sq2", name = "Seq2", version = 1, steps = new[] { new { order = 0, stepId = "s0", stepType = "Action", commandReference = new { commandId = "", commandName = (string?)"Tap" } } } }
      },
      commands: new[] { GamepackTestKit.Cmd("c1", "Tap") });
    await InstallAsync(json);
    (await _kit.Sequences.ListAsync()).Should().HaveCount(2);
  }

  [Fact]
  public async Task AHandMadeObjectIsNeverTouched() {
    await _kit.Sequences.CreateAsync(new CommandSequence { Id = "mine", Name = "Mine" });
    await InstallAsync(V1());
    await InstallAsync(V2());
    (await _kit.Sequences.GetAsync("mine"))!.Name.Should().Be("Mine");
    _kit.OwnerOf("sequence", "mine").IsUser.Should().BeTrue();
  }
}
