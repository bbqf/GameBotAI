using System.Diagnostics;
using System.Threading.Tasks;
using FluentAssertions;
using GameBot.Domain.Parameters;
using GameBot.Domain.Queues;
using GameBot.Domain.QueueTemplates;
using GameBot.Service.Services.QueueExecution;
using Xunit;

namespace GameBot.UnitTests.Queues;

/// <summary>
/// Feature 078 (US1): the scope a queue run hands to each firing. This is the mechanism that lets one
/// command and one sequence serve N emulator instances — the queue supplies its own serial, so three
/// queues that already differ by serial need no new configuration at all.
/// </summary>
public sealed partial class QueueExecutionServiceTests {
  private static readonly string[] SingleDailyEntry = { "Daily" };
  private static readonly string[] TwoEntries = { "A", "B" };

  private static async Task RunAndWaitAsync(Harness h, string queueId) {
    (await h.Service.StartAsync(queueId).ConfigureAwait(false)).Should().Be(QueueStartOutcome.Started);
    var sw = Stopwatch.StartNew();
    while (h.Service.IsRunning(queueId) && sw.ElapsedMilliseconds < 5000) {
      await Task.Delay(10).ConfigureAwait(false);
    }
  }

  [Fact] // FR-010 / SC-002: the motivating case, with zero new configuration.
  public async Task EachQueueSuppliesItsOwnEmulatorSerialToTheSameSequence() {
    var h = new Harness();

    // ONE template and ONE sequence, shared by two queues that differ only by serial.
    var template = new QueueTemplate { Id = "tpl-shared", Name = "Shared" };
    template.Entries.Add(new QueueTemplateEntry { SequenceId = "Daily" });
    h.Templates.Add(template);
    h.Queues.Add(new ExecutionQueue {
      Id = "q5558", Name = "PNS 5558", EmulatorSerial = "emulator-5558", LinkedTemplateId = "tpl-shared"
    });
    h.Queues.Add(new ExecutionQueue {
      Id = "q5560", Name = "PNS 5560", EmulatorSerial = "emulator-5560", LinkedTemplateId = "tpl-shared"
    });

    await RunAndWaitAsync(h, "q5558").ConfigureAwait(false);
    await RunAndWaitAsync(h, "q5560").ConfigureAwait(false);

    h.Sequences.Scopes.Should().HaveCount(2);
    Resolve(h, 0, ParameterNameRules.QueueEmulatorSerial).Should().Be("emulator-5558");
    Resolve(h, 1, ParameterNameRules.QueueEmulatorSerial).Should().Be("emulator-5560");
  }

  [Fact] // FR-010: instance name, index and linked game are exposed too.
  public async Task QueueInstanceAndGameAreExposedAsBuiltIns() {
    var h = new Harness();
    var template = new QueueTemplate { Id = "tpl-q", Name = "T" };
    template.Entries.Add(new QueueTemplateEntry { SequenceId = "Daily" });
    h.Templates.Add(template);
    h.Queues.Add(new ExecutionQueue {
      Id = "q", Name = "Q", EmulatorSerial = "emulator-5558", EmulatorInstanceName = "PNS-1",
      EmulatorInstanceIndex = 3, LinkedGameId = "game-7", LinkedTemplateId = "tpl-q"
    });

    await RunAndWaitAsync(h, "q").ConfigureAwait(false);

    Resolve(h, 0, ParameterNameRules.QueueInstanceName).Should().Be("PNS-1");
    Resolve(h, 0, ParameterNameRules.QueueInstanceIndex).Should().Be("3");
    Resolve(h, 0, ParameterNameRules.QueueGameId).Should().Be("game-7");
  }

  [Fact] // FR-011: an unset queue field is absent from scope, not an empty value.
  public async Task UnsetQueueFieldsAreAbsentFromTheFiringScope() {
    var h = new Harness();
    h.AddQueue("q", SingleDailyEntry);

    await RunAndWaitAsync(h, "q").ConfigureAwait(false);

    h.Sequences.Scopes[0].Scope.TryResolve(ParameterNameRules.QueueGameId, out _).Should().BeFalse();
    h.Sequences.Scopes[0].Scope.TryResolve(ParameterNameRules.QueueInstanceName, out _).Should().BeFalse();
  }

  [Fact] // FR-012: entry values override the queue built-ins for that entry's firing.
  public async Task TemplateEntryValueOverridesTheQueueBuiltIn() {
    var h = new Harness();
    var template = new QueueTemplate { Id = "tpl-q", Name = "T" };
    var entry = new QueueTemplateEntry { SequenceId = "Daily" };
    entry.ParameterValues.Add(new ParameterBinding {
      Name = ParameterNameRules.QueueEmulatorSerial, Value = "emulator-override"
    });
    template.Entries.Add(entry);
    h.Templates.Add(template);
    h.Queues.Add(new ExecutionQueue {
      Id = "q", Name = "Q", EmulatorSerial = "emulator-5558", LinkedTemplateId = "tpl-q"
    });

    await RunAndWaitAsync(h, "q").ConfigureAwait(false);

    Resolve(h, 0, ParameterNameRules.QueueEmulatorSerial).Should().Be("emulator-override");
  }

  [Fact] // FR-012 / FR-012a: two entries on one sequence hold independent values.
  public async Task TwoEntriesReferencingOneSequenceHoldIndependentValues() {
    var h = new Harness();
    var template = new QueueTemplate { Id = "tpl-q", Name = "T" };
    var first = new QueueTemplateEntry { SequenceId = "Daily" };
    first.ParameterValues.Add(new ParameterBinding { Name = "slot", Value = "one" });
    var second = new QueueTemplateEntry { SequenceId = "Daily" };
    second.ParameterValues.Add(new ParameterBinding { Name = "slot", Value = "two" });
    template.Entries.Add(first);
    template.Entries.Add(second);
    h.Templates.Add(template);
    h.Queues.Add(new ExecutionQueue {
      Id = "q", Name = "Q", EmulatorSerial = "emulator-5558", LinkedTemplateId = "tpl-q"
    });

    await RunAndWaitAsync(h, "q").ConfigureAwait(false);

    h.Sequences.Scopes.Should().HaveCount(2);
    Resolve(h, 0, "slot").Should().Be("one");
    Resolve(h, 1, "slot").Should().Be("two");
  }

  [Fact] // FR-012a: an ad-hoc name the sequence never declares still enters the run scope.
  public async Task AdHocEntryValueEntersTheRunScope() {
    var h = new Harness();
    var template = new QueueTemplate { Id = "tpl-q", Name = "T" };
    var entry = new QueueTemplateEntry { SequenceId = "Daily" };
    entry.ParameterValues.Add(new ParameterBinding { Name = "adbSerial", Value = "emulator-9999" });
    template.Entries.Add(entry);
    h.Templates.Add(template);
    h.Queues.Add(new ExecutionQueue {
      Id = "q", Name = "Q", EmulatorSerial = "emulator-5558", LinkedTemplateId = "tpl-q"
    });

    await RunAndWaitAsync(h, "q").ConfigureAwait(false);

    Resolve(h, 0, "adbSerial").Should().Be("emulator-9999");
  }

  [Fact] // FR-032 / SC-007: a template with no parameter values behaves exactly as before.
  public async Task UnparametrizedRunGetsOnlyTheQueueBuiltIns() {
    var h = new Harness();
    h.AddQueue("q", TwoEntries);

    await RunAndWaitAsync(h, "q").ConfigureAwait(false);

    h.Sequences.Executed.Should().Equal("A", "B");
    foreach (var (_, scope) in h.Sequences.Scopes) {
      scope.Describe().Should().OnlyContain(e => e.Name.StartsWith("queue.", System.StringComparison.Ordinal));
    }
  }

  private static string? Resolve(Harness h, int firingIndex, string name) =>
      h.Sequences.Scopes[firingIndex].Scope.TryResolve(name, out var value) ? value.Text : null;
}
