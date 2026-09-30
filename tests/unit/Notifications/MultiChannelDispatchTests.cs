using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using FluentAssertions;
using GameBot.Domain.Notifications;
using GameBot.Service.Services.Notifications;
using Xunit;

#pragma warning disable CA2007, CA1861, CA1707

namespace GameBot.UnitTests.Notifications;

/// <summary>Feature 120 (V-15, User Story 5): a new target type needs one class and one registration.</summary>
public sealed class MultiChannelDispatchTests {
  [Fact]
  public async Task V15_TwoChannelTypesBothGetTheMessage() {
    var queues = new MemoryQueueRepository();
    queues.Add(new GameBot.Domain.Queues.ExecutionQueue { Id = "q1", Name = "Farm-1", EmulatorSerial = "e", NotificationLevel = NotificationLevel.Failure });
    var sequences = new MemorySequenceRepository();
    sequences.Add("s1", "Collect");
    var targets = new MemoryTargetStore();
    targets.Create(new NotificationTarget { Id = "a", Type = "telegram", Name = "A" });
    targets.Create(new NotificationTarget { Id = "b", Type = "carrier-pigeon", Name = "B" });
    var telegram = new RecordingChannel("telegram");
    var pigeon = new RecordingChannel("carrier-pigeon");
    var dispatcher = new QueueNotificationDispatcher(new CapturingLogger<QueueNotificationDispatcher>());
    using var worker = new QueueNotificationWorker(dispatcher, queues, sequences, targets,
      new INotificationChannel[] { telegram, pigeon }, new CapturingLogger<QueueNotificationWorker>());

    await worker.HandleAsync(NotificationWork.ForJob(NotificationHarness.Job(NotificationRunStatus.Failure)));
    await NotificationHarness.WaitForAsync(() => telegram.Calls == 1 && pigeon.Calls == 1);

    telegram.Sent.Should().ContainSingle().Which.Text.Should().Be("Farm-1 : Collect : \U0001F534 failure");
    pigeon.Sent.Should().ContainSingle().Which.Text.Should().Be(telegram.Sent[0].Text);
  }

  [Fact]
  public void V15_TheDispatcherAndTheWorkerHaveNoTelegramTypeReference() {
    var assembly = typeof(QueueNotificationWorker).Assembly;
    foreach (var type in new[] { typeof(QueueNotificationWorker), typeof(QueueNotificationDispatcher), typeof(NotificationStreakState) }) {
      const BindingFlags all = BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly;
      var used = type.GetFields(all).Select(f => f.FieldType)
        .Concat(type.GetProperties(all).Select(p => p.PropertyType))
        .Concat(type.GetMethods(all).SelectMany(m => m.GetParameters().Select(p => p.ParameterType).Append(m.ReturnType)))
        .Concat(type.GetConstructors(all).SelectMany(c => c.GetParameters().Select(p => p.ParameterType)));
      used.Should().NotContain(typeof(TelegramChannel));
    }

    assembly.GetType(typeof(TelegramChannel).FullName!).Should().NotBeNull();
  }
}
