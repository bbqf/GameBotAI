using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using FluentAssertions;
using GameBot.Domain.Gamepacks;
using GameBot.Domain.Gamepacks.Seeds;
using GameBot.Domain.QueueTemplates;
using Xunit;

#pragma warning disable CA1707, CA2007, CA1861, CA1002, CA1812

namespace GameBot.UnitTests.Gamepacks;

/// <summary>A template store that counts calls and can fail a delete (feature 150).</summary>
internal sealed class RecordingTemplateRepository : IQueueTemplateRepository {
  private readonly IQueueTemplateRepository _inner;

  public RecordingTemplateRepository(IQueueTemplateRepository inner) => _inner = inner;

  public int Creates { get; private set; }
  public int Deletes { get; private set; }
  public HashSet<string> FailDeleteOfName { get; } = new(StringComparer.OrdinalIgnoreCase);
  public List<string> DeleteAttempts { get; } = new();

  public string? GetFilePath(string id) => _inner.GetFilePath(id);
  public Task<QueueTemplate?> GetAsync(string id) => _inner.GetAsync(id);
  public Task<IReadOnlyList<QueueTemplate>> ListAsync() => _inner.ListAsync();
  public Task<QueueTemplate?> FindByNameAsync(string name) => _inner.FindByNameAsync(name);

  public Task<QueueTemplate> CreateAsync(QueueTemplate item) {
    Creates++;
    return _inner.CreateAsync(item);
  }

  public Task<QueueTemplate> UpdateAsync(QueueTemplate item) => _inner.UpdateAsync(item);

  public async Task<bool> DeleteAsync(string id) {
    DeleteAttempts.Add(id);
    var existing = await _inner.GetAsync(id);
    if (existing is not null && FailDeleteOfName.Contains(existing.Name)) {
      throw new InvalidOperationException("Injected delete fault for " + existing.Name);
    }

    Deletes++;
    return await _inner.DeleteAsync(id);
  }
}

/// <summary>Feature 150 (T014): the applier writes the planned seed templates.</summary>
public sealed class GamepackApplierSeedTests {
  internal static string Pack(params object[] seeds) => GamepackTestKit.Pack("p", "1",
    sequences: new[] { GamepackTestKit.Seq("a1", "Help All") },
    seedTemplates: seeds);

  [Fact]
  public async Task TheApplierWritesThePlannedTemplatesWithNewIdAndNoOwnerEntry() {
    using var kit = new GamepackTestKit();

    var outcome = await kit.Service.InstallAsync(Pack(
      GamepackTestKit.Seed("Daily", GamepackTestKit.SeedEntry("Help All", "Timer", timerTimeOfDay: "03:30:00"), GamepackTestKit.SeedEntry("Help All", enabled: false))), false);

    outcome.Status.Should().Be(GamepackInstallStatus.Ok, outcome.ErrorMessage);
    outcome.SeedTemplatesCreated.Should().Equal("Daily");
    outcome.SeedTemplatesSkipped.Should().BeEmpty();
    var template = (await kit.Templates.ListAsync()).Single();
    template.Name.Should().Be("Daily");
    template.Id.Should().NotBeNullOrWhiteSpace();
    template.Entries.Should().HaveCount(2);
    template.Entries[0].SequenceId.Should().Be((await kit.SequenceByNameAsync("Help All"))!.Id);
    template.Entries[0].ScheduleType.Should().Be(ScheduleType.Timer);
    template.Entries[1].Enabled.Should().BeFalse();
    kit.Store.Load().Owners.Keys.Should().NotContain(k => k.Contains(template.Id, StringComparison.OrdinalIgnoreCase));
    kit.Store.Load().Gamepacks["p"].Objects.Should().NotContain(o => o.Id == template.Id);
  }

  [Fact]
  public async Task AUsedNameIsSkippedAndTheExistingTemplateStaysTheSame() {
    using var kit = new GamepackTestKit();
    var existing = await kit.AddTemplateAsync("Daily");

    var outcome = await kit.Service.InstallAsync(Pack(GamepackTestKit.Seed("daily", GamepackTestKit.SeedEntry("Help All"))), false);

    outcome.Status.Should().Be(GamepackInstallStatus.Ok);
    outcome.SeedTemplatesCreated.Should().BeEmpty();
    outcome.SeedTemplatesSkipped.Should().ContainSingle().Which.Should().Be(new SeedSkip("daily", SeedSkipReasons.Exists));
    var list = await kit.Templates.ListAsync();
    list.Should().ContainSingle();
    list[0].Id.Should().Be(existing.Id);
    list[0].Entries.Should().BeEmpty();
  }

  [Fact]
  public async Task AnApplierWithoutAStoreFailsAndChangesNothingWhenThePlanHasASeed() {
    using var kit = new GamepackTestKit();
    var parsed = GamepackFileSerializer.Parse(Pack(GamepackTestKit.Seed("Daily", GamepackTestKit.SeedEntry("Help All"))));
    var plan = (await kit.Validator.ValidateAsync(parsed.File!, parsed.Problems, seeds: true)).Plan!;
    var applier = new GamepackApplier(kit.Sequences, kit.Commands, kit.Images, kit.Store, kit.Gate);
    var before = kit.Snapshot();

    var outcome = await applier.ApplyAsync(plan, GamepackPreviousHandling.Keep);

    outcome.Status.Should().Be(GamepackApplyStatus.Failed);
    kit.Snapshot().Should().BeEquivalentTo(before);
  }
}
