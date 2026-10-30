using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using FluentAssertions;
using GameBot.Domain.QueueTemplates;
using GameBot.Service.Services.QueueExecution;
using Xunit;

namespace GameBot.UnitTests.Queues;

/// <summary>
/// Feature 171 (plan, performance goals): the diff and the build of the new entries for a template of 200 entries take far
/// under 200 ms. The limit here is 1 s, so a busy build machine does not make the test flaky.
/// </summary>
public sealed class QueueTemplateDiffPerformanceTests {
  private static QueueTemplateEntry Entry(int i, bool enabled = true) =>
    new() { EntryId = "e" + i, SequenceId = "seq-" + (i % 20), Enabled = enabled, ScheduleType = ScheduleType.OncePerRun };

  [Fact]
  public void TheDiffAndTheApplyOfTwoHundredEntriesAreFast() {
    var live = Enumerable.Range(0, 200).Select(i => Entry(i)).ToList();
    var target = new QueueTemplate { Id = "t", Name = "T" };
    foreach (var i in Enumerable.Range(0, 200).Reverse()) target.Entries.Add(Entry(i, enabled: i % 3 != 0));
    var plan = QueueRunPlan.Build(live, 0);
    var names = new Dictionary<string, string>();
    QueueTemplateDiff.Compute(plan, target, names); // warm up

    var watch = Stopwatch.StartNew();
    var diff = QueueTemplateDiff.Compute(plan, target, names);
    var built = QueueLiveApplyBuilder.Build(plan, target, new HashSet<string>(diff.Differences.Select(d => d.Key), StringComparer.Ordinal));
    var newPlan = QueueRunPlan.FromAll(built.AllEntries, built.AllKeys, 1);
    watch.Stop();

    diff.Differences.Should().Contain(d => d.Type == "order").And.Contain(d => d.Type == "disabled");
    newPlan.AllEntries.Should().HaveCount(200);
    watch.ElapsedMilliseconds.Should().BeLessThan(1000);
  }
}
