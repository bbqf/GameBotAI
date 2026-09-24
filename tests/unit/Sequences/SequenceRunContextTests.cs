using System;
using System.IO;
using System.Threading.Tasks;
using FluentAssertions;
using GameBot.Domain.Commands;
using GameBot.Domain.Queues;
using GameBot.Domain.Services;
using GameBot.Service.Services.SequenceExecution;
using GameBot.UnitTests.Queues;
using Xunit;

#pragma warning disable CA2007

namespace GameBot.UnitTests.Sequences;

/// <summary>
/// Feature 105: <see cref="SequenceExecutionService"/> pushes a <see cref="SequenceRunContext"/> for a
/// queue run only. The tests call the push helper that <c>ExecuteCoreAsync</c> uses, so no emulator,
/// session or execution log is necessary.
/// </summary>
public sealed class SequenceRunContextTests : IDisposable {
  private readonly string _root;
  private readonly FileSequenceRunStatisticsStore _store;
  private readonly LastRunConditionEvaluator _evaluator;

  public SequenceRunContextTests() {
    _root = Path.Combine(Path.GetTempPath(), "GameBotRunContextTests", Guid.NewGuid().ToString("N"));
    _store = new FileSequenceRunStatisticsStore(_root);
    _evaluator = new LastRunConditionEvaluator(_store, new FakeTimeProvider(new DateTimeOffset(2026, 9, 24, 14, 0, 0, TimeSpan.Zero)));
  }

  public void Dispose() {
    _store.Dispose();
    try { Directory.Delete(_root, recursive: true); } catch { /* ignore */ }
  }

  private static readonly LastRunStepCondition SelfSince11 = new() { Sequence = "self", Status = "success", Since = "11:00" };

  [Fact]
  public async Task AQueueRunPushesAContextWithTheQueueAndTheSequence() {
    await _store.RecordAsync("q1", "seq-a", new SequenceRunRecord {
      StartedAt = new DateTimeOffset(2026, 9, 24, 12, 0, 0, TimeSpan.Zero),
      EndedAt = new DateTimeOffset(2026, 9, 24, 12, 1, 0, TimeSpan.Zero),
      Status = SequenceRunStatus.Success
    });

    using (SequenceExecutionService.PushRunContext(_evaluator, "seq-a", "q1", dryRun: false)) {
      var context = SequenceRunContext.Current;
      context.Should().NotBeNull();
      context!.QueueId.Should().Be("q1");
      context.SequenceId.Should().Be("seq-a");
      (await context.LastRunEvaluator(SelfSince11, default)).Should().BeTrue("self resolves to seq-a in q1");
    }

    SequenceRunContext.Current.Should().BeNull();
  }

  [Theory]
  [InlineData(null, false)]
  [InlineData("", false)]
  [InlineData("q1", true)]
  public void AnAdHocRunAndADryRunPushNoContext(string? queueId, bool dryRun) {
    using var scope = SequenceExecutionService.PushRunContext(_evaluator, "seq-a", queueId, dryRun);

    scope.Should().BeNull();
    SequenceRunContext.Current.Should().BeNull();
  }

  [Fact]
  public void WithNoEvaluatorNoContextIsPushed() {
    using var scope = SequenceExecutionService.PushRunContext(null, "seq-a", "q1", dryRun: false);

    scope.Should().BeNull();
    SequenceRunContext.Current.Should().BeNull();
  }

  [Fact]
  public void ASimulatedNestedRunPushesItsOwnContextAndTheOuterComesBack() {
    // No step type runs another sequence at this time, so a real nested run is not possible. This
    // test simulates one with a second direct push inside the outer run, the same call that a nested
    // ExecuteAsync would make (spec edge case "Nested sequence run").
    using var outer = SequenceExecutionService.PushRunContext(_evaluator, "seq-outer", "q1", dryRun: false);
    SequenceRunContext.Current!.SequenceId.Should().Be("seq-outer");

    using (SequenceExecutionService.PushRunContext(_evaluator, "seq-nested", "q1", dryRun: false)) {
      SequenceRunContext.Current!.SequenceId.Should().Be("seq-nested", "self names the nested sequence");
      SequenceRunContext.Current.QueueId.Should().Be("q1");
    }

    SequenceRunContext.Current!.SequenceId.Should().Be("seq-outer");
  }

  [Fact]
  public void DisposePutsBackTheEarlierValueAndASecondDisposeDoesNothing() {
    var first = new SequenceRunContext("q1", "a", (_, _) => Task.FromResult(true));
    var second = new SequenceRunContext("q1", "b", (_, _) => Task.FromResult(false));

    var outer = SequenceRunContext.Push(first);
    var inner = SequenceRunContext.Push(second);
    SequenceRunContext.Current.Should().BeSameAs(second);

    inner.Dispose();
    SequenceRunContext.Current.Should().BeSameAs(first);
    inner.Dispose();
    SequenceRunContext.Current.Should().BeSameAs(first);

    outer.Dispose();
    SequenceRunContext.Current.Should().BeNull();
  }
}
