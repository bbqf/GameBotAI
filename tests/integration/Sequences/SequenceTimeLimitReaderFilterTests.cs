using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using GameBot.Domain.Logging;
using GameBot.Service.Services.ExecutionLog;
using GameBot.Service.Services.SequenceExecution;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace GameBot.IntegrationTests.Sequences;

/// <summary>
/// Feature 170 (story 2): a reader tells a planned time-limit stop from a real fault with the structured
/// fields (status and reason) only. The contract tests never read the summary text.
/// </summary>
[Collection("ConfigIsolation")]
public sealed class SequenceTimeLimitReaderFilterTests : OnTimeLimitIntegrationTestBase {
  private static async Task RunEndStopAsync(IServiceProvider services, string id) {
    using var timer = new CancellationTokenSource();
    using var stop = new CancellationTokenSource();
    using var firing = CancellationTokenSource.CreateLinkedTokenSource(timer.Token, stop.Token);
    timer.CancelAfter(TimeSpan.FromMilliseconds(200));
    using (SequenceTimeLimitScope.Push(200, timer.Token, stop.Token, endsRunAtLimit: true)) {
      var act = async () => await services.GetRequiredService<ISequenceExecutionService>()
        .ExecuteAsync(id, sessionId: null, parentContext: null, firing.Token).ConfigureAwait(false);
      await act.Should().ThrowAsync<OperationCanceledException>().ConfigureAwait(false);
    }
  }

  [Fact]
  public async Task StatusAndReasonSeparateAPlannedStopFromARealFault() {
    using var app = new WebApplicationFactory<Program>();
    using var client = CreateClient(app);
    var services = app.Services;
    await services.GetRequiredService<GameBot.Domain.Commands.ISequenceRepository>().CreateAsync(SlowSequence("seq-planned", "end")).ConfigureAwait(false);
    var (faultId, sessionId) = await CreateFailingSequenceAsync(client, "seq-fault").ConfigureAwait(false);

    await RunEndStopAsync(services, "seq-planned").ConfigureAwait(false);
    using var timer = new CancellationTokenSource();
    using (SequenceTimeLimitScope.Push(240000, timer.Token, CancellationToken.None)) {
      await services.GetRequiredService<ISequenceExecutionService>().ExecuteAsync(faultId, sessionId, parentContext: null).ConfigureAwait(false);
    }

    var planned = await SequenceEntryAsync(services, "seq-planned").ConfigureAwait(false);
    var fault = await SequenceEntryAsync(services, faultId).ConfigureAwait(false);

    planned.FinalStatus.Should().Be("success");
    planned.CancellationReason.Should().Be(ExecutionCancellationReasons.SequenceTimeLimit);
    fault.FinalStatus.Should().Be("failure");
    fault.CancellationReason.Should().BeNull();

    // A reader finds the planned stops with a filter on the two fields. The message is not read.
    var all = (await services.GetRequiredService<IExecutionLogService>()
      .QueryAsync(new ExecutionLogQuery { ObjectType = "sequence", RootsOnly = true, PageSize = 100 }).ConfigureAwait(false)).Items;
    all.Where(e => e.FinalStatus == "success" && e.CancellationReason == ExecutionCancellationReasons.SequenceTimeLimit)
      .Select(e => e.Id).Should().Equal(planned.Id);
  }

  // Soft check, not a contract: the text of the summary SHOULD say that the stop was by choice.
  [Fact]
  public async Task TheSummaryTextSaysThatTheStopWasByChoice() {
    using var app = new WebApplicationFactory<Program>();
    _ = app.CreateClient();
    var services = app.Services;
    await services.GetRequiredService<GameBot.Domain.Commands.ISequenceRepository>().CreateAsync(SlowSequence("seq-planned-text", "end")).ConfigureAwait(false);

    await RunEndStopAsync(services, "seq-planned-text").ConfigureAwait(false);

    var entry = await SequenceEntryAsync(services, "seq-planned-text").ConfigureAwait(false);
    entry.Summary.Should().Contain("by choice");
  }
}
