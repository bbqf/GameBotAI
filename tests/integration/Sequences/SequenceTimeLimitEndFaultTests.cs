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
/// Feature 170 (spec scenario 5): a step fault that the run records at the same time as the limit keeps
/// the status failure, also for a sequence with <c>end</c>.
/// </summary>
[Collection("ConfigIsolation")]
public sealed class SequenceTimeLimitEndFaultTests : OnTimeLimitIntegrationTestBase {
  [Fact]
  public async Task AFaultAtTheSameTimeAsTheLimitKeepsFailure() {
    using var app = new WebApplicationFactory<Program>();
    using var client = CreateClient(app);
    var services = app.Services;
    var (sequenceId, sessionId) = await CreateFailingSequenceAsync(client, "seq-end-same-time", "end").ConfigureAwait(false);

    using var timer = new CancellationTokenSource();
    await timer.CancelAsync().ConfigureAwait(false); // the limit has fired; the failed step is also in the result

    GameBot.Domain.Services.SequenceExecutionResult result;
    using (SequenceTimeLimitScope.Push(240000, timer.Token, CancellationToken.None, endsRunAtLimit: true)) {
      result = await services.GetRequiredService<ISequenceExecutionService>()
        .ExecuteAsync(sequenceId, sessionId, parentContext: null).ConfigureAwait(false);
    }

    result.Status.Should().Be("Failed");
    var entry = await SequenceEntryAsync(services, sequenceId).ConfigureAwait(false);
    entry.FinalStatus.Should().Be("failure", "a real step fault is never hidden by the option");
    entry.CancellationReason.Should().Be(ExecutionCancellationReasons.SequenceTimeLimit);
    entry.TimeLimitMs.Should().Be(240000);
  }
}
