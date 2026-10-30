using System.Linq;
using System.Threading.Tasks;
using FluentAssertions;
using GameBot.Domain.Gamepacks;
using GameBot.Domain.Gamepacks.Managed;
using GameBot.Domain.QueueTemplates;
using Xunit;

#pragma warning disable CA1707, CA2007, CA1861

namespace GameBot.UnitTests.Gamepacks;

/// <summary>
/// Feature 171: a seed template that a gamepack creates gets an entry ID for each entry. An existing template
/// is not changed by a gamepack install. A new entry that a managed update books in a template gets an ID only
/// when the template already has IDs.
/// </summary>
public sealed class SeedTemplateEntryIdTests {
  [Fact]
  public async Task ASeedTemplateGetsAUniqueIdForEachEntry() {
    using var kit = new GamepackTestKit();

    var outcome = await kit.Service.InstallAsync(GamepackApplierSeedTests.Pack(
      GamepackTestKit.Seed("Daily", GamepackTestKit.SeedEntry("Help All"), GamepackTestKit.SeedEntry("Help All"))), false);

    outcome.Status.Should().Be(GamepackInstallStatus.Ok, outcome.ErrorMessage);
    var template = (await kit.Templates.ListAsync()).Single();
    template.NeedsUpgrade.Should().BeFalse();
    template.Entries.Select(e => e.EntryId).Should().OnlyHaveUniqueItems().And.OnlyContain(id => !string.IsNullOrEmpty(id));
  }

  [Fact]
  public async Task AnExistingTemplateIsNotChangedBySeed() {
    using var kit = new GamepackTestKit();
    var existing = new QueueTemplate { Id = "mine", Name = "Daily" };
    existing.Entries.Add(new QueueTemplateEntry { SequenceId = "old" });
    await kit.Templates.CreateAsync(existing);

    var outcome = await kit.Service.InstallAsync(GamepackApplierSeedTests.Pack(
      GamepackTestKit.Seed("Daily", GamepackTestKit.SeedEntry("Help All"))), false);

    outcome.Status.Should().Be(GamepackInstallStatus.Ok, outcome.ErrorMessage);
    var template = (await kit.Templates.GetAsync("mine"))!;
    template.NeedsUpgrade.Should().BeTrue();
    template.Entries.Single().SequenceId.Should().Be("old");
  }

  [Fact]
  public void TheBookerGivesAnIdToANewEntryOfATemplateThatHasIds() {
    var template = new QueueTemplate { Name = "T" };
    template.Entries.Add(new QueueTemplateEntry { SequenceId = "s1", EntryId = "e1" });

    TemplateEntryBooker.Book(template, new[] { new NewSequence("s2", "Second") });

    template.Entries.Should().HaveCount(2);
    template.Entries[1].EntryId.Should().NotBeNullOrEmpty().And.NotBe("e1");
    template.Entries[1].Enabled.Should().BeFalse();
  }

  [Fact]
  public void TheBookerGivesNoIdToANewEntryOfATemplateThatNeedsAnUpgrade() {
    var template = new QueueTemplate { Name = "T" };
    template.Entries.Add(new QueueTemplateEntry { SequenceId = "s1" });

    TemplateEntryBooker.Book(template, new[] { new NewSequence("s2", "Second") });

    template.Entries[1].EntryId.Should().BeEmpty();
    template.NeedsUpgrade.Should().BeTrue();
  }

  [Fact]
  public void TheBookerGivesAnIdToTheFirstEntryOfAnEmptyTemplate() {
    var template = new QueueTemplate { Name = "T" };

    TemplateEntryBooker.Book(template, new[] { new NewSequence("s1", "First") });

    template.Entries.Single().EntryId.Should().NotBeNullOrEmpty();
  }
}
