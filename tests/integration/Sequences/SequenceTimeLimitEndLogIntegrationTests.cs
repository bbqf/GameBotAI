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
/// Feature 170: a run that reaches its time limit on a sequence with <c>onTimeLimit</c> = <c>end</c> gets
/// the status success. The entry keeps the reason <c>sequence_time_limit</c> and the limit value.
/// </summary>
[Collection("ConfigIsolation")]
public sealed class SequenceTimeLimitEndLogIntegrationTests : OnTimeLimitIntegrationTestBase {
  [Fact]
  public async Task LimitReachedWithEndGivesSuccessAndKeepsTheReasonAndTheLimit() {
    using var app = new WebApplicationFactory<Program>();
    _ = app.CreateClient();
    var services = app.Services;
    await services.GetRequiredService<ISequenceRepository>().CreateAsync(SlowSequence("seq-end", "end")).ConfigureAwait(false);

    using var timer = new CancellationTokenSource();
    using var stop = new CancellationTokenSource();
    using var firing = CancellationTokenSource.CreateLinkedTokenSource(timer.Token, stop.Token);
    timer.CancelAfter(TimeSpan.FromMilliseconds(200));

    using (SequenceTimeLimitScope.Push(200, timer.Token, stop.Token, endsRunAtLimit: true)) {
      var act = async () => await services.GetRequiredService<GameBot.Service.Services.SequenceExecution.ISequenceExecutionService>()
        .ExecuteAsync("seq-end", sessionId: null, parentContext: null, firing.Token).ConfigureAwait(false);
      await act.Should().ThrowAsync<OperationCanceledException>().ConfigureAwait(false);
    }

    var entry = await SequenceEntryAsync(services, "seq-end").ConfigureAwait(false);
    entry.FinalStatus.Should().Be("success");
    entry.CancellationReason.Should().Be(ExecutionCancellationReasons.SequenceTimeLimit);
    entry.TimeLimitMs.Should().Be(200);
  }

  [Fact]
  public async Task FinishingBeforeTheLimitGivesSuccessAndNoReason() {
    using var app = new WebApplicationFactory<Program>();
    _ = app.CreateClient();
    var services = app.Services;
    await services.GetRequiredService<ISequenceRepository>().CreateAsync(QuickSequence("seq-end-quick", "end")).ConfigureAwait(false);

    using var timer = new CancellationTokenSource();
    using (SequenceTimeLimitScope.Push(240000, timer.Token, CancellationToken.None, endsRunAtLimit: true)) {
      await services.GetRequiredService<GameBot.Service.Services.SequenceExecution.ISequenceExecutionService>()
        .ExecuteAsync("seq-end-quick", sessionId: null, parentContext: null).ConfigureAwait(false);
    }

    var entry = await SequenceEntryAsync(services, "seq-end-quick").ConfigureAwait(false);
    entry.FinalStatus.Should().Be("success");
    entry.CancellationReason.Should().BeNull();
    entry.TimeLimitMs.Should().BeNull();
  }

  [Fact]
  public async Task AStepFailureBeforeTheLimitKeepsFailure() {
    using var app = new WebApplicationFactory<Program>();
    using var client = CreateClient(app);
    var services = app.Services;
    var (sequenceId, sessionId) = await CreateFailingSequenceAsync(client, "seq-end-fault", "end").ConfigureAwait(false);

    using var timer = new CancellationTokenSource(); // the limit has not fired
    using (SequenceTimeLimitScope.Push(240000, timer.Token, CancellationToken.None, endsRunAtLimit: true)) {
      await services.GetRequiredService<GameBot.Service.Services.SequenceExecution.ISequenceExecutionService>()
        .ExecuteAsync(sequenceId, sessionId, parentContext: null).ConfigureAwait(false);
    }

    var entry = await SequenceEntryAsync(services, sequenceId).ConfigureAwait(false);
    entry.FinalStatus.Should().Be("failure");
    entry.CancellationReason.Should().BeNull();
    entry.TimeLimitMs.Should().BeNull();
  }
}
