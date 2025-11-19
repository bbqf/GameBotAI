using System;
using System.Linq;
using System.Threading.Tasks;
using FluentAssertions;
using GameBot.Service.Hosted;
using Xunit;

namespace GameBot.UnitTests;

internal class TriggerEvaluationMetricsTests {
  [Fact]
  public void IncrementsAndDurationWork() {
    var m = new TriggerEvaluationMetrics();

    m.Evaluations.Should().Be(0);
    m.SkippedNoSessions.Should().Be(0);
    m.OverlapSkipped.Should().Be(0);
    m.LastCycleDurationMs.Should().Be(0);

    m.IncrementEvaluations(3);
    m.IncrementSkippedNoSessions();
    m.IncrementOverlapSkipped();
    m.RecordCycleDuration(42);

    m.Evaluations.Should().Be(3);
    m.SkippedNoSessions.Should().Be(1);
    m.OverlapSkipped.Should().Be(1);
    m.LastCycleDurationMs.Should().Be(42);
  }

  [Fact]
  public async Task ThreadSafetyForConcurrentUpdates() {
    var m = new TriggerEvaluationMetrics();
    var tasks = Enumerable.Range(0, 10).Select(async _ => {
      for (int i = 0; i < 1000; i++) {
        m.IncrementEvaluations(1);
        m.IncrementSkippedNoSessions();
        m.IncrementOverlapSkipped();
        m.RecordCycleDuration(i);
        await Task.Yield();
      }
    });

    await Task.WhenAll(tasks).ConfigureAwait(false);

    m.Evaluations.Should().Be(10 * 1000);
    m.SkippedNoSessions.Should().Be(10 * 1000);
    m.OverlapSkipped.Should().Be(10 * 1000);
    m.LastCycleDurationMs.Should().BeGreaterOrEqualTo(0);
  }
}
