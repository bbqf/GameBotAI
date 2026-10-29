using System;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using GameBot.Domain.Commands;
using GameBot.Domain.Logging;
using GameBot.Domain.Parameters;
using GameBot.Domain.Queues;
using GameBot.Domain.QueueTemplates;
using GameBot.Service.Services.ExecutionLog;
using GameBot.Service.Services.QueueExecution;
using Microsoft.Extensions.DependencyInjection;

namespace GameBot.IntegrationTests.Queues;

/// <summary>Shared helpers for the feature 168 run tests. A key-input step needs no device.</summary>
internal static class FriendlyRunHelpers {
  public static Command KeyCommand(string id, string reference, params ParameterDeclaration[] declarations) {
    var command = new Command { Id = id, Name = $"Cmd-{id}" };
    command.Steps.Add(new CommandStep {
      Type = CommandStepType.KeyInput,
      Order = 0,
      KeyInput = new KeyInputConfig { Key = reference }
    });
    foreach (var declaration in declarations) command.Parameters.Add(declaration);
    return command;
  }

  public static CommandSequence SequenceInvoking(
      string id, string commandId, System.Collections.ObjectModel.Collection<ParameterBinding>? bindings = null,
      params ParameterDeclaration[] declarations) {
    var sequence = new CommandSequence { Id = id, Name = $"Seq-{id}" };
    foreach (var declaration in declarations) sequence.Parameters.Add(declaration);
    sequence.SetSteps(new[] {
      new SequenceStep {
        Order = 0, StepId = "s1", StepType = SequenceStepType.Command, CommandId = commandId, ParameterBindings = bindings
      }
    });
    return sequence;
  }

  public static async Task SeedAsync(
      IServiceProvider services,
      Command command,
      CommandSequence sequence,
      string queueId,
      Action<QueueTemplateEntry>? configureEntry = null) {
    await services.GetRequiredService<ICommandRepository>().AddAsync(command).ConfigureAwait(false);
    await services.GetRequiredService<ISequenceRepository>().CreateAsync(sequence).ConfigureAwait(false);

    var entry = new QueueTemplateEntry { SequenceId = sequence.Id, ScheduleType = ScheduleType.OncePerRun };
    configureEntry?.Invoke(entry);
    var template = new QueueTemplate { Id = $"tpl-{queueId}", Name = $"T-{queueId}" };
    template.Entries.Add(entry);
    await services.GetRequiredService<IQueueTemplateRepository>().CreateAsync(template).ConfigureAwait(false);

    var queue = new ExecutionQueue {
      Id = queueId, Name = $"Q-{queueId}", EmulatorSerial = "emu-offline", LinkedTemplateId = template.Id
    };
    await services.GetRequiredService<IQueueRepository>().CreateAsync(queue).ConfigureAwait(false);
  }

  public static async Task RunToCompletionAsync(IQueueExecutionService engine, string queueId) {
    await engine.StartAsync(queueId).ConfigureAwait(false);
    var sw = Stopwatch.StartNew();
    while (engine.IsRunning(queueId) && sw.ElapsedMilliseconds < 10000) {
      await Task.Delay(20).ConfigureAwait(false);
    }
  }

  /// <summary>The "parameters" detail items of the execution log: the text and the structured data.</summary>
  public static async Task<(string Text, string Data)> ParameterLogAsync(IExecutionLogService log) {
    var page = await log.QueryAsync(new ExecutionLogQuery { ObjectType = "command", PageSize = 200 })
        .ConfigureAwait(false);
    var items = page.Items
        .SelectMany(item => item.Details ?? Array.Empty<ExecutionDetailItem>())
        .Where(d => string.Equals(d.Kind, "parameters", StringComparison.Ordinal))
        .ToList();
    var text = string.Join(" | ", items.Select(d => d.Message));
    var data = string.Join(" | ", items.Select(d => System.Text.Json.JsonSerializer.Serialize(d.Attributes)));
    return (text, data);
  }
}
