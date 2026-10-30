using System;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using GameBot.Domain.Logging;
using GameBot.Service.Services.SequenceExecution;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace GameBot.IntegrationTests.Sequences;

/// <summary>
/// Feature 170 (story 3): a sequence saved before the feature (no <c>onTimeLimit</c>) and a sequence with
/// <c>fail</c> keep the status failure and the reason <c>sequence_time_limit</c> at the limit.
/// </summary>
[Collection("ConfigIsolation")]
public sealed class SequenceTimeLimitFailRegressionTests : OnTimeLimitIntegrationTestBase {
  [Theory]
  [InlineData(null)]
  [InlineData("fail")]
  public async Task AtTheLimitTheStatusIsFailure(string? onTimeLimit) {
    using var app = new WebApplicationFactory<Program>();
    _ = app.CreateClient();
    var services = app.Services;
    var id = "seq-fail-" + (onTimeLimit ?? "absent");
    await services.GetRequiredService<GameBot.Domain.Commands.ISequenceRepository>().CreateAsync(SlowSequence(id, onTimeLimit)).ConfigureAwait(false);

    using var timer = new CancellationTokenSource();
    using var stop = new CancellationTokenSource();
    using var firing = CancellationTokenSource.CreateLinkedTokenSource(timer.Token, stop.Token);
    timer.CancelAfter(TimeSpan.FromMilliseconds(200));
    // The queue gives endsRunAtLimit = false for fail and for an absent option.
    using (SequenceTimeLimitScope.Push(200, timer.Token, stop.Token, endsRunAtLimit: false)) {
      var act = async () => await services.GetRequiredService<ISequenceExecutionService>()
        .ExecuteAsync(id, sessionId: null, parentContext: null, firing.Token).ConfigureAwait(false);
      await act.Should().ThrowAsync<OperationCanceledException>().ConfigureAwait(false);
    }

    var entry = await SequenceEntryAsync(services, id).ConfigureAwait(false);
    entry.FinalStatus.Should().Be("failure");
    entry.CancellationReason.Should().Be(ExecutionCancellationReasons.SequenceTimeLimit);
    entry.TimeLimitMs.Should().Be(200);
  }
}
