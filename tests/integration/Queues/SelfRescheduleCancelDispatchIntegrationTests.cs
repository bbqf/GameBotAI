using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using GameBot.Domain.Actions;
using GameBot.Domain.Commands;
using GameBot.Domain.Commands.SelfReschedule;
using GameBot.Service.Services.ExecutionLog;
using GameBot.Service.Services.QueueExecution;
using GameBot.Service.Services.SequenceExecution;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Xunit;

namespace GameBot.IntegrationTests.Queues;

/// <summary>
/// Feature 123: the dispatch of the reschedule-self option Cancel through the real service graph.
/// A hand-made run handle is in the registry, so the tests see exactly which bookings the step removes
/// (R-004, R-012, FR-001 to FR-004).
/// </summary>
[Collection("ConfigIsolation")]
public sealed class SelfRescheduleCancelDispatchIntegrationTests {
  public SelfRescheduleCancelDispatchIntegrationTests() {
    Environment.SetEnvironmentVariable("GAMEBOT_USE_ADB", "false");
    Environment.SetEnvironmentVariable("GAMEBOT_AUTH_TOKEN", "test-token");
    TestEnvironment.PrepareCleanDataDir();
  }

  private static SequenceStep Reschedule(int order, string stepId, string option, string? relativeOffset = null) {
    var action = new SequenceActionPayload { Type = ActionTypes.RescheduleSelf };
    action.Parameters["option"] = option;
    if (relativeOffset is not null) action.Parameters["timerRelativeOffset"] = relativeOffset;
    return new SequenceStep { Order = order, StepId = stepId, StepType = SequenceStepType.Action, Action = action };
  }

  private static async Task SeedAsync(IServiceProvider services, string id, params SequenceStep[] steps) {
    var sequence = new CommandSequence { Id = id, Name = id };
    sequence.SetSteps(steps);
    await services.GetRequiredService<ISequenceRepository>().CreateAsync(sequence).ConfigureAwait(false);
  }

  private static QueueRunHandle Register(IServiceProvider services, string queueId) {
    var handle = new QueueRunHandle { QueueId = queueId, Cts = new CancellationTokenSource() };
    services.GetRequiredService<IQueueRunRegistry>().TryAdd(queueId, handle).Should().BeTrue();
    return handle;
  }

  private static Task<GameBot.Domain.Services.SequenceExecutionResult> RunAsync(IServiceProvider services, string sequenceId, string? queueId) =>
    services.GetRequiredService<ISequenceExecutionService>().ExecuteAsync(
      sequenceId, sessionId: null, parentContext: queueId is null ? null : new ExecutionLogContext { OriginatingQueueId = queueId });

  [Fact]
  public async Task CancelRemovesTheBookingsOfTheOwnerSequenceOnly() {
    using var app = new WebApplicationFactory<Program>();
    _ = app.CreateClient();
    var services = app.Services;
    await SeedAsync(services, "seq-cancel-own",
      Reschedule(0, "book", "Timer", "00:30:00"),
      Reschedule(1, "cancel", "Cancel")).ConfigureAwait(false);
    var handle = Register(services, "q-cancel-own");
    handle.PendingOncePerRun.Enqueue(new SelfRescheduleEntry("other", "seq-other", SelfRescheduleOption.OncePerRun, null));

    var result = await RunAsync(services, "seq-cancel-own", "q-cancel-own").ConfigureAwait(false);

    result.Status.Should().Be("Succeeded");
    var cancelStep = result.Steps.Single(s => s.CommandId == "cancel");
    cancelStep.ActionOutcome.Should().Be("cancelled");
    cancelStep.Removed.Should().BeTrue();
    handle.HasPendingTimerFirings.Should().BeFalse();
    // The booking of another sequence stays: the owner id is the id of the sequence that holds the step.
    handle.PendingOncePerRun.Should().ContainSingle().Which.SequenceId.Should().Be("seq-other");
  }

  [Fact]
  public async Task CancelWithNoPendingBookingIsANoop() {
    using var app = new WebApplicationFactory<Program>();
    _ = app.CreateClient();
    var services = app.Services;
    await SeedAsync(services, "seq-cancel-none", Reschedule(0, "cancel", "Cancel")).ConfigureAwait(false);
    Register(services, "q-cancel-none");

    var result = await RunAsync(services, "seq-cancel-none", "q-cancel-none").ConfigureAwait(false);

    result.Status.Should().Be("Succeeded");
    var step = result.Steps.Single();
    step.ActionOutcome.Should().Be("noop");
    step.Removed.Should().BeFalse();
  }

  [Fact]
  public async Task CancelOutsideAQueueIsANoop() {
    using var app = new WebApplicationFactory<Program>();
    _ = app.CreateClient();
    var services = app.Services;
    await SeedAsync(services, "seq-cancel-standalone", Reschedule(0, "cancel", "Cancel")).ConfigureAwait(false);

    var result = await RunAsync(services, "seq-cancel-standalone", null).ConfigureAwait(false);

    result.Status.Should().Be("Succeeded");
    var step = result.Steps.Single();
    step.ActionOutcome.Should().Be("noop");
    step.Removed.Should().BeFalse();
  }

  [Fact]
  public async Task CancelWhenTheQueueRunIsNoLongerActiveIsANoop() {
    using var app = new WebApplicationFactory<Program>();
    _ = app.CreateClient();
    var services = app.Services;
    await SeedAsync(services, "seq-cancel-gone", Reschedule(0, "cancel", "Cancel")).ConfigureAwait(false);

    // The context names a queue run that is not in the registry.
    var result = await RunAsync(services, "seq-cancel-gone", "q-gone").ConfigureAwait(false);

    result.Status.Should().Be("Succeeded");
    var step = result.Steps.Single();
    step.ActionOutcome.Should().Be("noop");
    step.Removed.Should().BeFalse();
  }

  [Fact]
  public async Task SecondCancelInOneRunIsANoop() {
    using var app = new WebApplicationFactory<Program>();
    _ = app.CreateClient();
    var services = app.Services;
    await SeedAsync(services, "seq-cancel-twice",
      Reschedule(0, "book", "OncePerRun"),
      Reschedule(1, "cancel1", "Cancel"),
      Reschedule(2, "cancel2", "Cancel")).ConfigureAwait(false);
    Register(services, "q-cancel-twice");

    var result = await RunAsync(services, "seq-cancel-twice", "q-cancel-twice").ConfigureAwait(false);

    result.Status.Should().Be("Succeeded");
    var first = result.Steps.Single(s => s.CommandId == "cancel1");
    first.ActionOutcome.Should().Be("cancelled");
    first.Removed.Should().BeTrue();
    var second = result.Steps.Single(s => s.CommandId == "cancel2");
    second.ActionOutcome.Should().Be("noop");
    second.Removed.Should().BeFalse();
  }

  [Fact]
  public async Task BookingAfterCancelStays() {
    using var app = new WebApplicationFactory<Program>();
    _ = app.CreateClient();
    var services = app.Services;
    await SeedAsync(services, "seq-cancel-rebook",
      Reschedule(0, "book1", "Timer", "00:30:00"),
      Reschedule(1, "cancel", "Cancel"),
      Reschedule(2, "book2", "Timer", "00:40:00")).ConfigureAwait(false);
    var handle = Register(services, "q-cancel-rebook");

    var result = await RunAsync(services, "seq-cancel-rebook", "q-cancel-rebook").ConfigureAwait(false);

    result.Status.Should().Be("Succeeded");
    handle.SnapshotPendingTimerFirings().Should().ContainSingle();
  }

  [Fact]
  public async Task LogItemOfACancelStepHasTheRemovedField() {
    using var app = new WebApplicationFactory<Program>();
    _ = app.CreateClient();
    var services = app.Services;
    await SeedAsync(services, "seq-cancel-log",
      Reschedule(0, "book", "OncePerRun"),
      Reschedule(1, "cancel", "Cancel")).ConfigureAwait(false);
    Register(services, "q-cancel-log");

    await RunAsync(services, "seq-cancel-log", "q-cancel-log").ConfigureAwait(false);

    var log = services.GetRequiredService<IExecutionLogService>();
    var page = await log.QueryAsync(new GameBot.Domain.Logging.ExecutionLogQuery { ObjectType = "sequence", PageSize = 50 }).ConfigureAwait(false);
    var items = page.Items.Where(e => e.ObjectRef.ObjectId == "seq-cancel-log")
      .SelectMany(e => e.Details)
      .Where(d => d.Attributes != null && d.Attributes.TryGetValue("option", out var o) && o as string == "Cancel")
      .ToList();
    items.Should().ContainSingle();
    items[0].Attributes!["actionOutcome"].Should().Be("cancelled");
    items[0].Attributes!["removed"].Should().Be(true);
  }

  // Feature 200 (issue #414): the stored subtree gives each Cancel step that ran the status success.
  [Theory]
  [InlineData(true, "Self-reschedule 'cancel' cancelled: removed 1 pending booking(s) of this sequence; applies to the current run only")]
  [InlineData(false, "Self-reschedule 'cancel' noop: no pending booking of this sequence; nothing to cancel")]
  public async Task CancelStepNodesOfAStoredRunHaveTheStatusSuccess(bool bookFirst, string expectedMessage) {
    using var app = new WebApplicationFactory<Program>();
    _ = app.CreateClient();
    var services = app.Services;
    var sequenceId = bookFirst ? "seq-cancel-status-booked" : "seq-cancel-status-empty";
    var steps = bookFirst
      ? new[] { Reschedule(0, "book", "Timer", "00:30:00"), Reschedule(1, "cancel", "Cancel") }
      : new[] { Reschedule(0, "cancel", "Cancel") };
    await SeedAsync(services, sequenceId, steps).ConfigureAwait(false);
    Register(services, "q-" + sequenceId);

    var result = await RunAsync(services, sequenceId, "q-" + sequenceId).ConfigureAwait(false);
    result.Status.Should().Be("Succeeded");

    var log = services.GetRequiredService<IExecutionLogService>();
    var page = await log.QueryAsync(new GameBot.Domain.Logging.ExecutionLogQuery { ObjectType = "sequence", PageSize = 50 }).ConfigureAwait(false);
    var entry = page.Items.Where(e => e.ObjectRef.ObjectId == sequenceId)
      .OrderByDescending(e => e.TimestampUtc)
      .First();
    var subtree = await log.GetSubtreeAsync(entry.Id).ConfigureAwait(false);

    subtree.Should().NotBeNull();
    var node = subtree!.Root.Children.Single(c => c.Message != null && c.Message.StartsWith("Self-reschedule 'cancel'", StringComparison.Ordinal));
    node.Status.Should().Be("success");
    node.Message.Should().Be(expectedMessage);
  }

  // Feature 200: an error during the cancel gives a failed step. The run continues.
  [Fact]
  public async Task CancelThatThrowsRecordsAFailedStepAndTheRunContinues() {
    using var app = new WebApplicationFactory<Program>().WithWebHostBuilder(b => b.ConfigureServices(s => {
      s.RemoveAll<ISelfRescheduleCoordinator>();
      s.AddSingleton<SelfRescheduleCoordinator>();
      s.AddSingleton<ISelfRescheduleCoordinator>(sp => new ThrowingCancelCoordinator(sp.GetRequiredService<SelfRescheduleCoordinator>()));
    }));
    _ = app.CreateClient();
    var services = app.Services;
    await SeedAsync(services, "seq-cancel-throws",
      Reschedule(0, "cancel", "Cancel"),
      Reschedule(1, "book", "Timer", "00:30:00")).ConfigureAwait(false);
    Register(services, "q-cancel-throws");

    var result = await RunAsync(services, "seq-cancel-throws", "q-cancel-throws").ConfigureAwait(false);

    result.Status.Should().Be("Succeeded");
    var cancelStep = result.Steps.Single(s => s.CommandId == "cancel");
    cancelStep.ActionOutcome.Should().Be("failed");
    cancelStep.Removed.Should().BeFalse();
    cancelStep.Message.Should().Be("cancel not performed: store unavailable");
    result.Steps.Single(s => s.CommandId == "book").ActionOutcome.Should().Be("scheduled");
  }

  /// <summary>Sends each call to the real coordinator, but <c>CancelSelf</c> throws (feature 200).</summary>
  private sealed class ThrowingCancelCoordinator : ISelfRescheduleCoordinator {
    private readonly SelfRescheduleCoordinator _inner;

    public ThrowingCancelCoordinator(SelfRescheduleCoordinator inner) => _inner = inner;

    public SelfRescheduleResult ScheduleSelf(
      string queueId,
      string sequenceId,
      SelfRescheduleOption option,
      TimeOnly? timerTimeOfDay,
      TimeSpan? timerRelativeOffset,
      GameBot.Domain.Parameters.ParameterScope? scope = null,
      SelfRescheduleKeep keep = SelfRescheduleKeep.None,
      string? runId = null,
      SmartSchedulingRequest? smartScheduling = null)
      => _inner.ScheduleSelf(queueId, sequenceId, option, timerTimeOfDay, timerRelativeOffset, scope, keep, runId, smartScheduling);

    public SelfRescheduleCancelResult CancelSelf(string queueId, string sequenceId)
      => throw new InvalidOperationException("store unavailable");

    public DateTimeOffset ResolveTimerFireAt(TimeOnly? timerTimeOfDay, TimeSpan? timerRelativeOffset)
      => _inner.ResolveTimerFireAt(timerTimeOfDay, timerRelativeOffset);
  }
}
