using System.Linq;
using FluentAssertions;
using GameBot.Domain.Gamepacks.Managed;
using GameBot.Domain.QueueTemplates;
using Xunit;

#pragma warning disable CA2007, CA1707, CA1861

namespace GameBot.UnitTests.Gamepacks.Managed;

/// <summary>Feature 148 (T019): the scope rules.</summary>
public sealed class ManagedUpdateScopeTests {
  private static QueueTemplate Template(string id, string name, params string[] sequenceIds) {
    var t = new QueueTemplate { Id = id, Name = name };
    foreach (var s in sequenceIds) {
      t.Entries.Add(new QueueTemplateEntry { SequenceId = s });
    }

    return t;
  }

  private static readonly string[] Installed = { "s1", "s2" };

  [Fact]
  public void ATemplateIsAffectedWhenOneEntryRefersToAnInstalledSequence() {
    var templates = new[] { Template("t1", "A", "s1"), Template("t2", "B", "x"), Template("t3", "C") };
    var queues = new[] {
      new ManagedQueueInfo("q1", "Q1", "t1", true),
      new ManagedQueueInfo("q2", "Q2", "t2", true),
      new ManagedQueueInfo("q3", "Q3", null, true)
    };

    var scope = ManagedUpdateScope.Compute(templates, queues, Installed, null);

    scope.AffectedTemplates.Select(t => t.Name).Should().Equal("A");
    scope.AffectedQueues.Select(q => q.Id).Should().Equal("q1");
    scope.MissingTemplates.Should().BeEmpty();
  }

  [Fact]
  public void OnlyRunningAffectedQueuesAreStopped() {
    var templates = new[] { Template("t1", "A", "s1") };
    var queues = new[] { new ManagedQueueInfo("q1", "Q1", "t1", true), new ManagedQueueInfo("q2", "Q2", "t1", false) };

    var scope = ManagedUpdateScope.Compute(templates, queues, Installed, null);

    scope.AffectedQueues.Should().HaveCount(2);
    scope.QueuesToStop.Select(q => q.Id).Should().Equal("q1");
  }

  [Fact]
  public void TheTemplateListLimitsTheScopeAndIgnoresCase() {
    var templates = new[] { Template("t1", "Alpha", "s1"), Template("t2", "Beta", "s2") };
    var queues = new[] { new ManagedQueueInfo("q1", "Q1", "t1", true), new ManagedQueueInfo("q2", "Q2", "t2", true) };

    var scope = ManagedUpdateScope.Compute(templates, queues, Installed, new[] { "ALPHA" });

    scope.AffectedTemplates.Select(t => t.Name).Should().Equal("Alpha");
    scope.AffectedQueues.Select(q => q.Id).Should().Equal("q1");
  }

  [Fact]
  public void AListedTemplateThatTheGamepackDoesNotAffectIsReportedAsNotAffected() {
    var templates = new[] { Template("t1", "Alpha", "s1"), Template("t2", "Plain", "x") };

    var scope = ManagedUpdateScope.Compute(templates, System.Array.Empty<ManagedQueueInfo>(), Installed, new[] { "alpha", "plain" });

    scope.AffectedTemplates.Select(t => t.Name).Should().Equal("Alpha");
    scope.NotAffectedTemplates.Should().Equal("Plain");
  }

  [Fact]
  public void AMissingTemplateNameIsListed() {
    var templates = new[] { Template("t1", "Alpha", "s1") };

    var scope = ManagedUpdateScope.Compute(templates, System.Array.Empty<ManagedQueueInfo>(), Installed, new[] { "alpha", "nope" });

    scope.MissingTemplates.Should().Equal("nope");
  }

  [Fact]
  public void AFirstInstallHasNoAffectedTemplate() {
    var templates = new[] { Template("t1", "Alpha", "s1") };
    var queues = new[] { new ManagedQueueInfo("q1", "Q1", "t1", true) };

    var scope = ManagedUpdateScope.Compute(templates, queues, System.Array.Empty<string>(), null);

    scope.AffectedTemplates.Should().BeEmpty();
    scope.QueuesToStop.Should().BeEmpty();
  }
}
