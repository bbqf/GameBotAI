using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using GameBot.Domain.Commands;
using GameBot.Domain.Notifications;
using GameBot.Domain.Queues;
using GameBot.Domain.QueueTemplates;
using GameBot.Service.Services.Notifications;
using GameBot.Service.Services.QueueExecution;
using GameBot.UnitTests.Notifications;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

#pragma warning disable CA2007, CA1861, CA1859, CA1707, CA2000

namespace GameBot.IntegrationTests.Notifications;

/// <summary>A dispatcher that records each job, for tests that count jobs (feature 120).</summary>
internal sealed class JobRecordingDispatcher : INotificationDispatcher {
  private readonly List<QueueNotificationJob> _jobs = new();
  private readonly List<string> _resets = new();

  public IReadOnlyList<QueueNotificationJob> Jobs { get { lock (_jobs) return _jobs.ToList(); } }

  public IReadOnlyList<string> Resets { get { lock (_resets) return _resets.ToList(); } }

  public void Enqueue(QueueNotificationJob job) { lock (_jobs) _jobs.Add(job); }

  public void ResetStreaks(string queueId) { lock (_resets) _resets.Add(queueId); }
}

/// <summary>Helpers that build a host and seed queues for the notification tests.</summary>
internal static class NotificationIntegrationHelpers {
  /// <summary>A host with a dispatcher that records, in place of the real one.</summary>
  public static WebApplicationFactory<Program> HostWithDispatcher(INotificationDispatcher dispatcher)
    => new WebApplicationFactory<Program>().WithWebHostBuilder(b => b.ConfigureServices(s => {
      s.RemoveAll<INotificationDispatcher>();
      s.AddSingleton(dispatcher);
    }));

  /// <summary>A host with the real dispatcher and worker, and a channel that records, for type telegram.</summary>
  public static WebApplicationFactory<Program> HostWithChannel(RecordingChannel channel)
    => new WebApplicationFactory<Program>().WithWebHostBuilder(b => b.ConfigureServices(s => {
      s.RemoveAll<INotificationChannel>();
      s.AddSingleton<INotificationChannel>(channel);
    }));

  /// <summary>Adds one enabled telegram target to the store of the host.</summary>
  public static NotificationTarget AddTarget(IServiceProvider services, string name = "Phone") {
    var target = new NotificationTarget { Type = "telegram", Name = name };
    target.Settings["chatId"] = "1";
    target.Settings["botToken"] = "123456:ABCDEFGHIJKLMNOPQRSTUVWXYZ";
    return services.GetRequiredService<INotificationTargetStore>().Create(target);
  }

  /// <summary>A sequence with a few key steps. With ADB stubbed it succeeds and needs no device.</summary>
  public static async Task SeedSequenceAsync(IServiceProvider services, string sequenceId, int stepCount = 3) {
    var commands = services.GetRequiredService<ICommandRepository>();
    var command = new Command { Id = $"c-{sequenceId}", Name = $"Cmd-{sequenceId}" };
    command.Steps.Add(new CommandStep { Type = CommandStepType.KeyInput, Order = 0, KeyInput = new KeyInputConfig { Key = "KEYCODE_HOME" } });
    await commands.AddAsync(command);
    var sequence = new CommandSequence { Id = sequenceId, Name = $"Seq-{sequenceId}" };
    sequence.SetSteps(Enumerable.Range(0, stepCount).Select(i => new SequenceStep {
      Order = i, StepId = $"s{i}", StepType = SequenceStepType.Command, CommandId = command.Id
    }).ToArray());
    await services.GetRequiredService<ISequenceRepository>().CreateAsync(sequence);
  }

  /// <summary>Seeds a queue with a template of the given sequence IDs, at a notification level.</summary>
  public static async Task SeedQueueAsync(IServiceProvider services, string queueId, NotificationLevel level, params string[] sequenceIds) {
    var template = new QueueTemplate { Id = $"tpl-{queueId}", Name = $"T-{queueId}" };
    foreach (var id in sequenceIds) template.Entries.Add(new QueueTemplateEntry { SequenceId = id, ScheduleType = ScheduleType.OncePerRun });
    await services.GetRequiredService<IQueueTemplateRepository>().CreateAsync(template);
    await services.GetRequiredService<IQueueRepository>().CreateAsync(new ExecutionQueue {
      Id = queueId, Name = $"Q-{queueId}", EmulatorSerial = "emu-offline", LinkedTemplateId = template.Id, NotificationLevel = level
    });
  }

  public static async Task RunToCompletionAsync(IServiceProvider services, string queueId) {
    var engine = services.GetRequiredService<IQueueExecutionService>();
    await engine.StartAsync(queueId);
    var sw = Stopwatch.StartNew();
    while (engine.IsRunning(queueId) && sw.ElapsedMilliseconds < 15000) await Task.Delay(20);
  }

  public static async Task WaitForAsync(Func<bool> condition, int timeoutMs = 10000) {
    var sw = Stopwatch.StartNew();
    while (!condition() && sw.ElapsedMilliseconds < timeoutMs) await Task.Delay(10);
  }
}
