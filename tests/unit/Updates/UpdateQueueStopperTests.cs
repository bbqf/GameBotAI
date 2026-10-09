using FluentAssertions;
using GameBot.Domain.Queues;
using GameBot.Service.Services.QueueExecution;
using GameBot.Service.Services.Updates;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using System.Diagnostics.CodeAnalysis;
using Xunit;

namespace GameBot.UnitTests.Updates;

[SuppressMessage("Performance", "CA1861:Avoid constant arrays as arguments", Justification = "Test data. The arrays are small and used once per test.")]
public sealed class UpdateQueueStopperTests {
  private sealed class FakeQueues : IQueueRepository {
    public List<ExecutionQueue> Items { get; } = new();

    public Task<ExecutionQueue?> GetAsync(string id) => Task.FromResult(Items.FirstOrDefault(q => q.Id == id));
    public Task<IReadOnlyList<ExecutionQueue>> ListAsync() => Task.FromResult<IReadOnlyList<ExecutionQueue>>(Items);
    public Task<ExecutionQueue> CreateAsync(ExecutionQueue queue) => throw new NotSupportedException();
    public Task<ExecutionQueue> UpdateAsync(ExecutionQueue queue) => throw new NotSupportedException();
    public Task<bool> DeleteAsync(string id) => throw new NotSupportedException();
  }

  private sealed class FakeExecution : IQueueExecutionService {
    public HashSet<string> Running { get; } = new();
    public List<string> Stopped { get; } = new();
    public HashSet<string> FailOn { get; } = new();

    public Task<QueueStartOutcome> StartAsync(string queueId, CancellationToken ct = default) => throw new NotSupportedException();

    public Task StopAsync(string queueId, CancellationToken ct = default) {
      if (FailOn.Contains(queueId)) {
        throw new InvalidOperationException("stop failed");
      }

      Stopped.Add(queueId);
      Running.Remove(queueId);
      return Task.CompletedTask;
    }

    public bool IsRunning(string queueId) => Running.Contains(queueId);

    public LiveScheduleResult ScheduleRelative(string queueId, string sequenceId, TimeSpan offset) => throw new NotSupportedException();
  }

  private readonly FakeQueues _queues = new();
  private readonly FakeExecution _execution = new();

  private UpdateQueueStopper Stopper() {
    var services = new ServiceCollection();
    services.AddSingleton<IQueueRepository>(_queues);
    services.AddSingleton<IQueueExecutionService>(_execution);
    return new UpdateQueueStopper(services.BuildServiceProvider(), NullLogger<UpdateQueueStopper>.Instance);
  }

  [Fact]
  public async Task OnlyRunningQueuesAreStopped() {
    _queues.Items.AddRange(new[] { new ExecutionQueue { Id = "a" }, new ExecutionQueue { Id = "b" }, new ExecutionQueue { Id = "c" } });
    _execution.Running.UnionWith(new[] { "a", "c" });

    await Stopper().StopAllAsync(CancellationToken.None).ConfigureAwait(true);

    _execution.Stopped.Should().BeEquivalentTo(new[] { "a", "c" });
  }

  [Fact]
  public async Task OneFailedStopDoesNotStopTheOthers() {
    _queues.Items.AddRange(new[] { new ExecutionQueue { Id = "a" }, new ExecutionQueue { Id = "b" } });
    _execution.Running.UnionWith(new[] { "a", "b" });
    _execution.FailOn.Add("a");

    await Stopper().StopAllAsync(CancellationToken.None).ConfigureAwait(true);

    _execution.Stopped.Should().BeEquivalentTo(new[] { "b" });
  }

  [Fact]
  public async Task NoQueuesIsAllRight() {
    var act = () => Stopper().StopAllAsync(CancellationToken.None);

    await act.Should().NotThrowAsync().ConfigureAwait(true);
  }

  [Fact]
  public async Task CancelStopsTheLoop() {
    _queues.Items.Add(new ExecutionQueue { Id = "a" });
    using var cts = new CancellationTokenSource();
    await cts.CancelAsync().ConfigureAwait(true);

    var act = () => Stopper().StopAllAsync(cts.Token);

    await act.Should().ThrowAsync<OperationCanceledException>().ConfigureAwait(true);
  }
}
