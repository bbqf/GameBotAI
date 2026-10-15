using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using GameBot.Domain.Commands;
using GameBot.Domain.Games;
using GameBot.Domain.Queues;
using GameBot.Domain.QueueSettings;
using GameBot.Domain.QueueTemplates;
using GameBot.UnitTests.Gamepacks;

#pragma warning disable CA2007, CA1707, CA1002, CA1819, CA2227, CA1822, CA1305, CA1307, CA1861

namespace GameBot.UnitTests.QueueSettings.TestDoubles;

/// <summary>
/// Real file-backed stores in a temporary folder, with the gamepack services, the exporter, and the importer on top
/// (feature 145). The importer writes through the probed repositories, so a test can count writes and inject faults.
/// </summary>
internal sealed class QueueSettingsTestKit : IDisposable {
  private static readonly JsonSerializerOptions CommandJson = BuildCommandJson();

  public QueueSettingsTestKit() {
    Packs = new GamepackTestKit();
    Queues = new FileQueueRepository(Packs.Root);
    Runtime = new QueueRuntimeStore();
    Templates = new FileQueueTemplateRepository(Packs.Root);
    Games = new FileGameRepository(Packs.Root);
    Probe = new WriteProbe();
    Exporter = new QueueSettingsExporter(Packs.Sequences, Packs.Commands, Queues, Runtime, Templates, Games, Packs.Service);
    Importer = new QueueSettingsImporter(
      new ProbedSequenceRepository(Packs.Sequences, Probe),
      new ProbedCommandRepository(Packs.Commands, Probe),
      new ProbedQueueRepository(Queues, Probe),
      Runtime,
      new ProbedQueueTemplateRepository(Templates, Probe),
      Games,
      Packs.Images,
      Packs.Service);
  }

  public GamepackTestKit Packs { get; }
  public FileQueueRepository Queues { get; }
  public QueueRuntimeStore Runtime { get; }
  public FileQueueTemplateRepository Templates { get; }
  public FileGameRepository Games { get; }
  public WriteProbe Probe { get; }
  public QueueSettingsExporter Exporter { get; }
  public QueueSettingsImporter Importer { get; }

  public void Dispose() => Packs.Dispose();

  // ---- builders ----

  public async Task<Command> AddCommandAsync(string name, string? callsCommandId = null) {
    var steps = new List<object>();
    if (callsCommandId is not null) {
      steps.Add(new { type = "Command", order = 0, targetId = callsCommandId });
    }
    else {
      steps.Add(new { type = "KeyInput", order = 0, keyInput = new { key = "BACK" } });
    }

    var json = JsonSerializer.Serialize(new { id = Guid.NewGuid().ToString("N"), name, steps, triggerId = "trigger-" + name });
    var command = JsonSerializer.Deserialize<Command>(json, CommandJson)!;
    return await Packs.Commands.AddAsync(command);
  }

  public async Task<Command> AddImageCommandAsync(string name, string imageId) {
    var json = JsonSerializer.Serialize(new {
      id = Guid.NewGuid().ToString("N"),
      name,
      steps = new[] {
        new {
          type = "PrimitiveTap",
          order = 0,
          primitiveTap = new { detectionTarget = new { referenceImageId = imageId, confidence = 0.8, offsetX = 0, offsetY = 0, selectionStrategy = "HighestConfidence" } }
        }
      }
    });
    return await Packs.Commands.AddAsync(JsonSerializer.Deserialize<Command>(json, CommandJson)!);
  }

  public async Task<CommandSequence> AddSequenceAsync(string name, params string[] commandIds) {
    var sequence = new CommandSequence {
      Id = Guid.NewGuid().ToString("N"),
      Name = name,
      Version = 1,
      CreatedAt = DateTimeOffset.UtcNow.AddDays(-1),
      UpdatedAt = DateTimeOffset.UtcNow.AddDays(-1)
    };
    sequence.SetSteps(commandIds.Select((c, i) => new SequenceStep { Order = i, StepId = "s" + i, CommandId = c, StepType = SequenceStepType.Command }));
    return await Packs.Sequences.CreateAsync(sequence);
  }

  public async Task<QueueTemplate> AddTemplateAsync(string name, params string[] sequenceIds) {
    var template = new QueueTemplate { Id = Guid.NewGuid().ToString("N"), Name = name };
    foreach (var id in sequenceIds) {
      template.Entries.Add(new QueueTemplateEntry { SequenceId = id });
    }

    return await Templates.CreateAsync(template);
  }

  public async Task<GameArtifact> AddGameAsync(string name) =>
    await Games.AddAsync(new GameArtifact { Id = Guid.NewGuid().ToString("N"), Name = name });

  public async Task<ExecutionQueue> AddQueueAsync(string name, string? templateId = null, string? gameId = null, params string[] entrySequenceIds) {
    var queue = await Queues.CreateAsync(new ExecutionQueue {
      Id = Guid.NewGuid().ToString("N"),
      Name = name,
      EmulatorSerial = "emulator-5554",
      LinkedTemplateId = templateId,
      LinkedGameId = gameId
    });
    if (entrySequenceIds.Length > 0) {
      Runtime.SetEntries(queue.Id, entrySequenceIds);
    }

    return queue;
  }

  // ---- reads ----

  public async Task<ExecutionQueue?> QueueByNameAsync(string name) =>
    (await Queues.ListAsync()).FirstOrDefault(q => string.Equals(q.Name, name, StringComparison.OrdinalIgnoreCase));

  public async Task<QueueTemplate?> TemplateByNameAsync(string name) => await Templates.FindByNameAsync(name);

  public async Task<SettingsDocument> ExportAsync() => await Exporter.ExportAsync();

  public async Task<string> ExportJsonAsync() => SettingsDocumentJson.Serialize(await Exporter.ExportAsync());

  public static JsonObject Obj(string json) => JsonNode.Parse(json)!.AsObject();

  /// <summary>A document with the given parts. The lists are JSON text.</summary>
  public static string Doc(string queues = "[]", string templates = "[]", string sequences = "[]", string commands = "[]", string references = "[]", int formatVersion = 1) =>
    $"{{\"formatVersion\":{formatVersion},\"queues\":{queues},\"queueTemplates\":{templates},\"sequences\":{sequences},\"commands\":{commands},\"references\":{references}}}";

  public static string SequenceJson(string name, params string[] commandNames) =>
    JsonSerializer.Serialize(new {
      Name = name,
      Version = 1,
      Steps = commandNames.Select((c, i) => new { Order = i, StepId = "s" + i, CommandId = c, StepType = "Command" }).ToArray()
    });

  public static string CommandJsonText(string name) =>
    JsonSerializer.Serialize(new { name, steps = new[] { new { type = "KeyInput", order = 0, keyInput = new { key = "BACK" } } } });

  public static string QueueJson(string name, string? template = null, string? game = null, params string[] entries) =>
    JsonSerializer.Serialize(new { name, emulatorSerial = "emulator-5554", linkedTemplate = template, linkedGame = game, entries });

  public static string TemplateJson(string name, params string[] sequences) =>
    JsonSerializer.Serialize(new { name, entries = sequences.Select(s => new { sequence = s, enabled = true, scheduleType = "OncePerRun" }).ToArray() });

  public static string List(params string[] items) => "[" + string.Join(",", items) + "]";

  private static JsonSerializerOptions BuildCommandJson() {
    var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
    options.Converters.Add(new JsonStringEnumConverter());
    return options;
  }
}
