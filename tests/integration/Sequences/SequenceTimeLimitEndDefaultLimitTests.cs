using System;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using GameBot.Domain.Commands;
using GameBot.Domain.Logging;
using GameBot.Service.Services.SequenceExecution;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace GameBot.IntegrationTests.Sequences;

/// <summary>
/// Feature 170 (spec scenario 4): a sequence with <c>end</c> and no <c>watchdogTimeoutMs</c> uses the
/// default limit of 240000 ms. The test fires the limit timer by hand, so it does not wait 4 minutes.
/// </summary>
[Collection("ConfigIsolation")]
public sealed class SequenceTimeLimitEndDefaultLimitTests : OnTimeLimitIntegrationTestBase {
  [Fact]
  public async Task EndAtTheDefaultLimitGivesSuccessWithTheDefaultLimitValue() {
    using var app = new WebApplicationFactory<Program>();
    _ = app.CreateClient();
    var services = app.Services;
    var sequence = SlowSequence("seq-end-default", "end");
    sequence.WatchdogTimeoutMs.Should().BeNull();
    await services.GetRequiredService<ISequenceRepository>().CreateAsync(sequence).ConfigureAwait(false);

    using var timer = new CancellationTokenSource();
    using var stop = new CancellationTokenSource();
    using var firing = CancellationTokenSource.CreateLinkedTokenSource(timer.Token, stop.Token);
    timer.CancelAfter(TimeSpan.FromMilliseconds(200));

    using (SequenceTimeLimitScope.Push(SequenceTimeLimits.Resolve(null), timer.Token, stop.Token, endsRunAtLimit: true)) {
      var act = async () => await services.GetRequiredService<ISequenceExecutionService>()
        .ExecuteAsync("seq-end-default", sessionId: null, parentContext: null, firing.Token).ConfigureAwait(false);
      await act.Should().ThrowAsync<OperationCanceledException>().ConfigureAwait(false);
    }

    var entry = await SequenceEntryAsync(services, "seq-end-default").ConfigureAwait(false);
    entry.FinalStatus.Should().Be("success");
    entry.CancellationReason.Should().Be(ExecutionCancellationReasons.SequenceTimeLimit);
    entry.TimeLimitMs.Should().Be(240000);
  }
}
