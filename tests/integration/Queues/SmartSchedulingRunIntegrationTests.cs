using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using GameBot.Domain.Actions;
using GameBot.Domain.Commands;
using GameBot.Domain.Logging;
using GameBot.Domain.Queues;
using GameBot.Domain.QueueTemplates;
using GameBot.Service.Services.ExecutionLog;
using GameBot.Service.Services.QueueExecution;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

// CA2000: NewApp gives the factory to its caller, which disposes it.
#pragma warning disable CA2000, CA2007

namespace GameBot.IntegrationTests.Queues;

/// <summary>
/// Feature 189: smart scheduling in a real queue run (queue engine, real dispatch, coordinator). The test clock moves
/// only when the test moves it, so each booked time is exact.
/// </summary>
[Collection("ConfigIsolation")]
public sealed class SmartSchedulingRunIntegrationTests {
  private static readonly DateTimeOffset T0 = new(2026, 11, 13, 12, 0, 0, TimeSpan.Zero);

  public SmartSchedulingRunIntegrationTests() {
    Environment.SetEnvironmentVariable("GAMEBOT_USE_ADB", "false");
    Environment.SetEnvironmentVariable("GAMEBOT_AUTH_TOKEN", "test-token");
    TestEnvironment.PrepareCleanDataDir();
  }

  /// <summary>A clock that moves only when the test moves it.</summary>
  private sealed class TestClock : TimeProvider {
    private long _ticks;

    public TestClock(DateTimeOffset start) => _ticks = start.UtcTicks;

    public override DateTimeOffset GetUtcNow() => new(Interlocked.Read(ref _ticks), TimeSpan.Zero);

    public override TimeZoneInfo LocalTimeZone => TimeZoneInfo.Utc;

    public void Advance(TimeSpan delta) => Interlocked.Add(ref _ticks, delta.Ticks);
  }

  /// <summary>What <see cref="FlakyQueues.GetAsync"/> does while <see cref="FlakyQueues.Mode"/> is set.</summary>
  public enum FlakyMode { Pass, Throw, ReturnNull }

  /// <summary>Sends each call to the real repository. It throws from GetAsync, or gives null, while the mode says so.</summary>
  private sealed class FlakyQueues : IQueueRepository {
    private readonly IQueueRepository _inner;

    public FlakyQueues(IQueueRepository inner) => _inner = inner;

    public volatile FlakyMode Mode;

    public Task<ExecutionQueue?> GetAsync(string id) => Mode switch {
      FlakyMode.Throw => throw new InvalidOperationException("simulated queue read failure"),
      FlakyMode.ReturnNull => Task.FromResult<ExecutionQueue?>(null),
      _ => _inner.GetAsync(id)
    };

    public Task<IReadOnlyList<ExecutionQueue>> ListAsync() => _inner.ListAsync();
    public Task<ExecutionQueue> CreateAsync(ExecutionQueue queue) => _inner.CreateAsync(queue);
    public Task<ExecutionQueue> UpdateAsync(ExecutionQueue queue) => _inner.UpdateAsync(queue);
    public Task<bool> DeleteAsync(string id) => _inner.DeleteAsync(id);
  }

  private static WebApplicationFactory<Program> NewApp(TestClock clock, Func<IQueueRepository, IQueueRepository>? wrapQueues = null) =>
    new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
      builder.ConfigureServices(services => {
        foreach (var descriptor in services.Where(d => d.ServiceType == typeof(TimeProvider)).ToList()) services.Remove(descriptor);
        services.AddSingleton<TimeProvider>(clock);
        if (wrapQueues is not null) {
          var original = services.Last(d => d.ServiceType == typeof(IQueueRepository));
          services.Remove(original);
          services.AddSingleton<IQueueRepository>(sp => wrapQueues((IQueueRepository)original.ImplementationFactory!(sp)));
        }
      }));

  /// <summary>A sequence with one step: a <c>reschedule-self</c> Timer booking of <paramref name="minutes"/> from now.</summary>
  private static CommandSequence Booking(string id, string name, int minutes, bool ignoreSmartScheduling = false) {
    var sequence = new CommandSequence { Id = id, Name = name, IgnoreSmartScheduling = ignoreSmartScheduling };
    var action = new SequenceActionPayload { Type = ActionTypes.RescheduleSelf };
    action.Parameters["option"] = "Timer";
    action.Parameters["timerRelativeOffset"] = TimeSpan.FromMinutes(minutes).ToString("c", CultureInfo.InvariantCulture);
    sequence.SetSteps(new[] { new SequenceStep { Order = 0, StepId = "book", StepType = SequenceStepType.Action, Action = action } });
    return sequence;
  }

  private static QueueTemplateEntry Once(string sequenceId) =>
    new() { SequenceId = sequenceId, ScheduleType = ScheduleType.OncePerRun };

  private static QueueTemplateEntry RelativeTimer(string sequenceId, int minutes) =>
    new() { SequenceId = sequenceId, ScheduleType = ScheduleType.Timer, TimerRelativeOffset = TimeSpan.FromMinutes(minutes) };

  private static async Task SeedAsync(
      IServiceProvider services, string queueId, bool smart, int window, IEnumerable<CommandSequence> sequences,
      params QueueTemplateEntry[] entries) {
    foreach (var sequence in sequences) {
      await services.GetRequiredService<ISequenceRepository>().CreateAsync(sequence);
    }
    var template = new QueueTemplate { Id = $"tpl-{queueId}", Name = $"T-{queueId}" };
    foreach (var entry in entries) template.Entries.Add(entry);
    await services.GetRequiredService<IQueueTemplateRepository>().CreateAsync(template);
    await services.GetRequiredService<IQueueRepository>().CreateAsync(new ExecutionQueue {
      Id = queueId, Name = $"Q-{queueId}", EmulatorSerial = "emu-offline", LinkedTemplateId = template.Id,
      SmartScheduling = smart, SmartSchedulingWindowMinutes = window
    });
  }

  private static async Task SaveQueueAsync(IServiceProvider services, string queueId, Action<ExecutionQueue> change) {
    var repository = services.GetRequiredService<IQueueRepository>();
    var queue = await repository.GetAsync(queueId);
    change(queue!);
    await repository.UpdateAsync(queue!);
  }

  /// <summary>Waits until the condition on the pending Timer bookings of the run is true. Returns the bookings.</summary>
  private static async Task<IReadOnlyList<SelfRescheduleEntry>> WaitForBookingsAsync(
      IServiceProvider services, string queueId, Func<IReadOnlyList<SelfRescheduleEntry>, bool> done, int timeoutMs = 30000) {
    var registry = services.GetRequiredService<IQueueRunRegistry>();
    var sw = Stopwatch.StartNew();
    IReadOnlyList<SelfRescheduleEntry> pending = Array.Empty<SelfRescheduleEntry>();
    while (sw.ElapsedMilliseconds < timeoutMs) {
      if (registry.TryGet(queueId, out var handle)) {
        pending = handle.SnapshotPendingTimerFirings();
        if (done(pending)) return pending;
      }
      await Task.Delay(25);
    }
    return pending;
  }

  private static DateTimeOffset? FireAtOf(IReadOnlyList<SelfRescheduleEntry> pending, string sequenceId) =>
    pending.FirstOrDefault(e => e.SequenceId == sequenceId)?.FireAt;

  private static async Task<List<ExecutionLogEntry>> SequenceRunsAsync(IServiceProvider services, string sequenceId) {
    var page = await services.GetRequiredService<IExecutionLogService>()
      .QueryAsync(new ExecutionLogQuery { ObjectType = "sequence", PageSize = 200 });
    // Only a finished run has the step details.
    return page.Items.Where(e => e.ObjectRef?.ObjectId == sequenceId && e.Details.Any(IsRescheduleDetail)).ToList();
  }

  private static bool IsRescheduleDetail(ExecutionDetailItem d) =>
    d.Attributes != null && d.Attributes.TryGetValue("stepType", out var type) && type as string == "reschedule-self";

  private static async Task<List<ExecutionLogEntry>> WaitForRunsAsync(IServiceProvider services, string sequenceId, int count, int timeoutMs = 30000) {
    var sw = Stopwatch.StartNew();
    while (true) {
      var runs = await SequenceRunsAsync(services, sequenceId);
      if (runs.Count >= count || sw.ElapsedMilliseconds > timeoutMs) return runs;
      await Task.Delay(50);
    }
  }

  private static ExecutionDetailItem RescheduleDetail(ExecutionLogEntry run) => run.Details.First(IsRescheduleDetail);

  private static string U(DateTimeOffset at) => at.ToString("u", CultureInfo.InvariantCulture);

  /// <summary>The text of a log attribute, for an attribute in memory and for one read back as JSON.</summary>
  private static string? Text(object? value) => value switch {
    null => null,
    System.Text.Json.JsonElement e => e.ValueKind == System.Text.Json.JsonValueKind.String ? e.GetString() : e.GetRawText(),
    DateTimeOffset d => d.ToString("o", CultureInfo.InvariantCulture),
    _ => Convert.ToString(value, CultureInfo.InvariantCulture)
  };

  [Fact] // (a) Two near bookings merge into one wake-up.
  public async Task TwoNearBookingsMergeIntoOneWakeUp() {
    var clock = new TestClock(T0);
    using var app = NewApp(clock);
    _ = app.CreateClient();
    var services = app.Services;
    await SeedAsync(services, "q189-a", smart: true, window: 15,
      new[] { Booking("seq-a", "A", 10), Booking("seq-b", "B", 2) }, Once("seq-a"), Once("seq-b"));
    var engine = services.GetRequiredService<IQueueExecutionService>();

    (await engine.StartAsync("q189-a")).Should().Be(QueueStartOutcome.Started);
    try {
      var pending = await WaitForBookingsAsync(services, "q189-a", p => p.Count == 2);
      FireAtOf(pending, "seq-a").Should().Be(T0.AddMinutes(10));
      FireAtOf(pending, "seq-b").Should().Be(T0.AddMinutes(10));

      var monitor = await services.GetRequiredService<IQueueMonitorService>().BuildAsync("q189-a");
      var booked = monitor.Upcoming.Where(i => i.ScheduleKind == ScheduleKind.SelfReschedule).ToList();
      booked.Select(i => i.ExpectedAt).Should().Equal(T0.AddMinutes(10), T0.AddMinutes(10));

      // The execution log of B shows the requested time, the booked time, and the target (FR-020).
      var firstRunOfB = (await WaitForRunsAsync(services, "seq-b", 1)).Should().ContainSingle().Subject;
      var detail = RescheduleDetail(firstRunOfB);
      detail.Message.Should().Contain($"postponed from {U(T0.AddMinutes(2))} to the firing of 'A'")
        .And.Contain("(smart scheduling, window 15 min)");
      DateTimeOffset.Parse(Text(detail.Attributes!["requestedFireAt"])!, CultureInfo.InvariantCulture).Should().Be(T0.AddMinutes(2));
      DateTimeOffset.Parse(Text(detail.Attributes["bookedFireAt"])!, CultureInfo.InvariantCulture).Should().Be(T0.AddMinutes(10));
      Text(detail.Attributes["smartSchedulingTarget"]).Should().Be("A");
      Text(detail.Attributes["smartSchedulingTargetKind"]).Should().Be("SelfReschedule");
      Text(detail.Attributes["smartSchedulingWindowMinutes"]).Should().Be("15");

      // At 12:05 nothing is due: B does not fire at its requested time.
      clock.Advance(TimeSpan.FromMinutes(5));
      await Task.Delay(1000);
      (await SequenceRunsAsync(services, "seq-b")).Should().HaveCount(1);

      // At 12:10 the two fire in the same wake-up.
      clock.Advance(TimeSpan.FromMinutes(5));
      (await WaitForRunsAsync(services, "seq-a", 2)).Should().HaveCount(2);
      (await WaitForRunsAsync(services, "seq-b", 2)).Should().HaveCount(2);
    }
    finally {
      await engine.StopAsync("q189-a");
    }
  }

  [Fact] // (b) With smart scheduling off, the booking and the log are as before the feature (SC-004).
  public async Task WithSmartSchedulingOffTheBookingKeepsTheRequestedTime() {
    var clock = new TestClock(T0);
    using var app = NewApp(clock);
    _ = app.CreateClient();
    var services = app.Services;
    await SeedAsync(services, "q189-b", smart: false, window: 15,
      new[] { Booking("seq-a", "A", 10), Booking("seq-b", "B", 2) }, Once("seq-a"), Once("seq-b"));
    var engine = services.GetRequiredService<IQueueExecutionService>();

    (await engine.StartAsync("q189-b")).Should().Be(QueueStartOutcome.Started);
    try {
      var pending = await WaitForBookingsAsync(services, "q189-b", p => p.Count == 2);
      FireAtOf(pending, "seq-b").Should().Be(T0.AddMinutes(2));

      var run = (await WaitForRunsAsync(services, "seq-b", 1)).Should().ContainSingle().Subject;
      var detail = RescheduleDetail(run);
      detail.Attributes!["message"].Should().Be(
        $"rescheduled this sequence (option Timer, {U(T0.AddMinutes(2))}); applies to the current run only");
      detail.Attributes.Keys.Should().NotContain(new[] {
        "requestedFireAt", "bookedFireAt", "smartSchedulingTarget", "smartSchedulingTargetKind", "smartSchedulingWindowMinutes" });
    }
    finally {
      await engine.StopAsync("q189-b");
    }
  }

  [Fact] // (c) A saved change applies to the next booking of a running queue (FR-007).
  public async Task TurningOnSmartSchedulingInARunningQueueAppliesToTheNextBooking() {
    var clock = new TestClock(T0);
    using var app = NewApp(clock);
    _ = app.CreateClient();
    var services = app.Services;
    await SeedAsync(services, "q189-c", smart: false, window: 15,
      new[] { Booking("seq-a", "A", 10), Booking("seq-b", "B", 2) }, Once("seq-a"), Once("seq-b"));
    var engine = services.GetRequiredService<IQueueExecutionService>();

    (await engine.StartAsync("q189-c")).Should().Be(QueueStartOutcome.Started);
    try {
      var pending = await WaitForBookingsAsync(services, "q189-c", p => p.Count == 2);
      FireAtOf(pending, "seq-b").Should().Be(T0.AddMinutes(2));

      await SaveQueueAsync(services, "q189-c", q => q.SmartScheduling = true);
      clock.Advance(TimeSpan.FromMinutes(2));

      // B fires at 12:02 and books 12:04. The booking of A at 12:10 is in the window.
      pending = await WaitForBookingsAsync(services, "q189-c", p => FireAtOf(p, "seq-b") is { } at && at != T0.AddMinutes(2));
      FireAtOf(pending, "seq-b").Should().Be(T0.AddMinutes(10));
    }
    finally {
      await engine.StopAsync("q189-c");
    }
  }

  [Fact] // (d) A smaller window applies to the next booking. A pending booking keeps its time.
  public async Task ASmallerWindowAppliesToTheNextBookingAndAPendingBookingKeepsItsTime() {
    var clock = new TestClock(T0);
    using var app = NewApp(clock);
    _ = app.CreateClient();
    var services = app.Services;
    await SeedAsync(services, "q189-d", smart: true, window: 15,
      new[] { Booking("seq-a", "A", 10), Booking("seq-b", "B", 2) }, Once("seq-a"), Once("seq-b"));
    var engine = services.GetRequiredService<IQueueExecutionService>();

    (await engine.StartAsync("q189-d")).Should().Be(QueueStartOutcome.Started);
    try {
      var pending = await WaitForBookingsAsync(services, "q189-d", p => p.Count == 2);
      FireAtOf(pending, "seq-b").Should().Be(T0.AddMinutes(10));

      await SaveQueueAsync(services, "q189-d", q => q.SmartSchedulingWindowMinutes = 5);
      await Task.Delay(500);
      pending = await WaitForBookingsAsync(services, "q189-d", p => p.Count == 2);
      FireAtOf(pending, "seq-b").Should().Be(T0.AddMinutes(10));

      // At 12:10 the two fire. A books 12:20. B books 12:12: the target 8 minutes away is out of the window of 5.
      clock.Advance(TimeSpan.FromMinutes(10));
      pending = await WaitForBookingsAsync(services, "q189-d",
        p => FireAtOf(p, "seq-a") == T0.AddMinutes(20) && FireAtOf(p, "seq-b") is { } at && at != T0.AddMinutes(10));
      FireAtOf(pending, "seq-b").Should().Be(T0.AddMinutes(12));
    }
    finally {
      await engine.StopAsync("q189-d");
    }
  }

  [Fact] // (e) and (f) A time-critical sequence keeps its time, and another sequence can move to its booking (US3).
  public async Task ATimeCriticalSequenceKeepsItsTimeAndIsATarget() {
    var clock = new TestClock(T0);
    using var app = NewApp(clock);
    _ = app.CreateClient();
    var services = app.Services;
    await SeedAsync(services, "q189-e", smart: true, window: 15,
      new[] { Booking("seq-a", "A", 10), Booking("seq-s", "S", 2, ignoreSmartScheduling: true), Booking("seq-b", "B", 1) },
      Once("seq-a"), Once("seq-s"), Once("seq-b"));
    var engine = services.GetRequiredService<IQueueExecutionService>();

    (await engine.StartAsync("q189-e")).Should().Be(QueueStartOutcome.Started);
    try {
      var pending = await WaitForBookingsAsync(services, "q189-e", p => p.Count == 3);
      // (e) S ignores smart scheduling: the booking of A at 12:10 is in the window, but S keeps 12:02.
      FireAtOf(pending, "seq-s").Should().Be(T0.AddMinutes(2));
      pending.Single(e => e.SequenceId == "seq-s").Postponement.Should().BeNull();
      // (f) B requests 12:01 and moves to the booking of S at 12:02, the earliest firing in the window.
      FireAtOf(pending, "seq-b").Should().Be(T0.AddMinutes(2));
      pending.Single(e => e.SequenceId == "seq-b").Postponement!.TargetSequenceId.Should().Be("seq-s");
    }
    finally {
      await engine.StopAsync("q189-e");
    }
  }

  [Theory] // (g) A failed queue read never fails the booking: the booking keeps the requested time (Design Note 4).
  [InlineData(FlakyMode.Throw)]
  [InlineData(FlakyMode.ReturnNull)]
  [InlineData(FlakyMode.Pass)] // The control case: with a good read, the booking moves.
  public async Task AFailedQueueReadKeepsTheRequestedTime(FlakyMode mode) {
    var clock = new TestClock(T0);
    FlakyQueues? flaky = null;
    using var app = NewApp(clock, inner => flaky = new FlakyQueues(inner));
    _ = app.CreateClient();
    var services = app.Services;
    var queueId = mode switch { FlakyMode.Throw => "q189-g-throw", FlakyMode.ReturnNull => "q189-g-null", _ => "q189-g-pass" };
    // B fires at 12:01 and books 12:03. The template Timer of A at 12:10 is in the window.
    await SeedAsync(services, queueId, smart: true, window: 15,
      new[] { Booking("seq-a", "A", 30), Booking("seq-b", "B", 2) }, RelativeTimer("seq-b", 1), RelativeTimer("seq-a", 10));
    var engine = services.GetRequiredService<IQueueExecutionService>();

    (await engine.StartAsync(queueId)).Should().Be(QueueStartOutcome.Started);
    try {
      // The run loop must take its start time before the clock moves.
      var registry = services.GetRequiredService<IQueueRunRegistry>();
      var sw = Stopwatch.StartNew();
      while (!(registry.TryGet(queueId, out var handle) && handle.Schedule is not null) && sw.ElapsedMilliseconds < 30000) {
        await Task.Delay(25);
      }
      flaky.Should().NotBeNull();
      flaky!.Mode = mode;
      clock.Advance(TimeSpan.FromMinutes(1));

      var pending = await WaitForBookingsAsync(services, queueId, p => FireAtOf(p, "seq-b") is not null);
      FireAtOf(pending, "seq-b").Should().Be(mode == FlakyMode.Pass ? T0.AddMinutes(10) : T0.AddMinutes(3));
      flaky.Mode = FlakyMode.Pass;

      var run = (await WaitForRunsAsync(services, "seq-b", 1)).Should().ContainSingle().Subject;
      RescheduleDetail(run).Attributes!["actionOutcome"].Should().Be("scheduled");
      engine.IsRunning(queueId).Should().BeTrue();
    }
    finally {
      if (flaky is not null) flaky.Mode = FlakyMode.Pass;
      await engine.StopAsync(queueId);
    }
  }
}
