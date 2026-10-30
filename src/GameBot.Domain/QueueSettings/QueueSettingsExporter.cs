using System.Text.Json.Nodes;
using GameBot.Domain.Commands;
using GameBot.Domain.Games;
using GameBot.Domain.Gamepacks;
using GameBot.Domain.Queues;
using GameBot.Domain.QueueTemplates;

namespace GameBot.Domain.QueueSettings;

/// <summary>
/// Makes the settings document (feature 145). It reads the stores and takes no lock. It writes the queues, the queue
/// templates, and the sequences and commands that no gamepack owns. It writes each use of a gamepack-owned object as a
/// name reference with no content (FR-003, FR-015).
/// </summary>
public sealed class QueueSettingsExporter {
  private readonly ISequenceRepository _sequences;
  private readonly ICommandRepository _commands;
  private readonly IQueueRepository _queues;
  private readonly IQueueRuntimeStore _runtime;
  private readonly IQueueTemplateRepository _templates;
  private readonly IGameRepository _games;
  private readonly GamepackService _gamepacks;
  private readonly TimeProvider _time;

  /// <summary>Creates the exporter.</summary>
  /// <param name="sequences">The sequence store.</param>
  /// <param name="commands">The command store.</param>
  /// <param name="queues">The queue store.</param>
  /// <param name="runtime">The runtime store with the queue entries.</param>
  /// <param name="templates">The queue template store.</param>
  /// <param name="games">The game store.</param>
  /// <param name="gamepacks">The gamepack service, for the owner records.</param>
  /// <param name="timeProvider">The clock, or null.</param>
  public QueueSettingsExporter(
      ISequenceRepository sequences,
      ICommandRepository commands,
      IQueueRepository queues,
      IQueueRuntimeStore runtime,
      IQueueTemplateRepository templates,
      IGameRepository games,
      GamepackService gamepacks,
      TimeProvider? timeProvider = null) {
    _sequences = sequences ?? throw new ArgumentNullException(nameof(sequences));
    _commands = commands ?? throw new ArgumentNullException(nameof(commands));
    _queues = queues ?? throw new ArgumentNullException(nameof(queues));
    _runtime = runtime ?? throw new ArgumentNullException(nameof(runtime));
    _templates = templates ?? throw new ArgumentNullException(nameof(templates));
    _games = games ?? throw new ArgumentNullException(nameof(games));
    _gamepacks = gamepacks ?? throw new ArgumentNullException(nameof(gamepacks));
    _time = timeProvider ?? TimeProvider.System;
  }

  /// <summary>Makes the document.</summary>
  /// <param name="ct">The cancellation token.</param>
  public async Task<SettingsDocument> ExportAsync(CancellationToken ct = default) {
    var owners = _gamepacks.LoadOwners();
    var sequences = await _sequences.ListAsync().ConfigureAwait(false);
    var commands = await _commands.ListAsync(ct).ConfigureAwait(false);
    var templates = await _templates.ListAsync().ConfigureAwait(false);
    var queues = await _queues.ListAsync().ConfigureAwait(false);
    var games = await _games.ListAsync(ct).ConfigureAwait(false);

    var sequenceNames = sequences.Where(s => !string.IsNullOrWhiteSpace(s.Name)).ToDictionary(s => s.Id, s => s.Name, StringComparer.OrdinalIgnoreCase);
    var commandNames = commands.Where(c => !string.IsNullOrWhiteSpace(c.Name)).ToDictionary(c => c.Id, c => c.Name, StringComparer.OrdinalIgnoreCase);
    var templateNames = templates.ToDictionary(t => t.Id, t => t.Name, StringComparer.OrdinalIgnoreCase);
    var gameNames = games.ToDictionary(g => g.Id, g => g.Name, StringComparer.OrdinalIgnoreCase);
    var references = new ReferenceCollector();

    var sequenceDocs = new List<JsonObject>();
    foreach (var sequence in sequences.Where(s => owners.Resolve(GamepackObjectTypes.Sequence, s.Id).IsUser).OrderBy(s => s.Name, StringComparer.OrdinalIgnoreCase)) {
      var usedBy = SettingsObjectTypes.Sequence + ":" + sequence.Name;
      foreach (var id in GamepackReferences.CommandIds(sequence)) {
        references.AddIfOwned(owners, GamepackObjectTypes.Command, SettingsObjectTypes.Command, id, commandNames, usedBy);
      }

      foreach (var id in GamepackReferences.ImageIds(sequence)) {
        references.AddImage(owners, id, usedBy);
      }

      sequenceDocs.Add(SettingsNormalizer.SequenceToDoc(sequence, id => commandNames.GetValueOrDefault(id)));
    }

    var commandDocs = new List<JsonObject>();
    foreach (var command in commands.Where(c => owners.Resolve(GamepackObjectTypes.Command, c.Id).IsUser).OrderBy(c => c.Name, StringComparer.OrdinalIgnoreCase)) {
      var usedBy = SettingsObjectTypes.Command + ":" + command.Name;
      foreach (var id in GamepackReferences.CommandIds(command)) {
        references.AddIfOwned(owners, GamepackObjectTypes.Command, SettingsObjectTypes.Command, id, commandNames, usedBy);
      }

      foreach (var id in GamepackReferences.ImageIds(command)) {
        references.AddImage(owners, id, usedBy);
      }

      commandDocs.Add(SettingsNormalizer.CommandToDoc(command, id => commandNames.GetValueOrDefault(id)));
    }

    var templateDocs = new List<QueueTemplateDoc>();
    foreach (var template in templates.OrderBy(t => t.Name, StringComparer.OrdinalIgnoreCase)) {
      var usedBy = SettingsObjectTypes.QueueTemplate + ":" + template.Name;
      var entries = new List<QueueTemplateEntryDoc>();
      foreach (var entry in template.Entries) {
        if (!sequenceNames.TryGetValue(entry.SequenceId, out var sequenceName)) {
          continue;
        }

        references.AddIfOwned(owners, GamepackObjectTypes.Sequence, SettingsObjectTypes.Sequence, entry.SequenceId, sequenceNames, usedBy);
        entries.Add(ToEntryDoc(entry, sequenceName));
      }

      templateDocs.Add(new QueueTemplateDoc { Name = template.Name, Entries = entries });
    }

    var queueDocs = new List<QueueSettingsDoc>();
    foreach (var queue in queues.OrderBy(q => q.Name, StringComparer.OrdinalIgnoreCase)) {
      var usedBy = SettingsObjectTypes.Queue + ":" + queue.Name;
      var entryNames = new List<string>();
      foreach (var entry in _runtime.GetEntries(queue.Id)) {
        if (sequenceNames.TryGetValue(entry.SequenceId, out var sequenceName)) {
          references.AddIfOwned(owners, GamepackObjectTypes.Sequence, SettingsObjectTypes.Sequence, entry.SequenceId, sequenceNames, usedBy);
          entryNames.Add(sequenceName);
        }
      }

      string? gameName = null;
      if (!string.IsNullOrWhiteSpace(queue.LinkedGameId) && gameNames.TryGetValue(queue.LinkedGameId, out var foundGame)) {
        gameName = foundGame;
        references.Add(new NameReference { Type = SettingsObjectTypes.Game, Name = foundGame, UsedBy = usedBy });
      }

      string? templateName = null;
      if (!string.IsNullOrWhiteSpace(queue.LinkedTemplateId) && templateNames.TryGetValue(queue.LinkedTemplateId, out var foundTemplate)) {
        templateName = foundTemplate;
      }

      queueDocs.Add(ToQueueDoc(queue, templateName, gameName, entryNames));
    }

    return new SettingsDocument {
      FormatVersion = SettingsDocument.CurrentFormatVersion,
      ExportedAtUtc = _time.GetUtcNow(),
      Queues = queueDocs,
      QueueTemplates = templateDocs,
      Sequences = sequenceDocs,
      Commands = commandDocs,
      References = references.ToList()
    };
  }

  internal static QueueTemplateEntryDoc ToEntryDoc(QueueTemplateEntry entry, string sequenceName) => new() {
    EntryId = string.IsNullOrEmpty(entry.EntryId) ? null : entry.EntryId,
    Sequence = sequenceName,
    Enabled = entry.Enabled,
    ScheduleType = entry.ScheduleType,
    TimerTimeOfDay = entry.TimerTimeOfDay,
    TimerRelativeOffset = entry.TimerRelativeOffset,
    ParameterValues = entry.ParameterValues.ToList()
  };

  internal static QueueSettingsDoc ToQueueDoc(ExecutionQueue queue, string? templateName, string? gameName, IReadOnlyList<string> entries) => new() {
    Name = queue.Name,
    EmulatorSerial = queue.EmulatorSerial,
    EmulatorInstanceName = queue.EmulatorInstanceName,
    EmulatorInstanceIndex = queue.EmulatorInstanceIndex,
    CycleExecution = queue.CycleExecution,
    PauseWhenIdle = queue.PauseWhenIdle,
    IdleThresholdSeconds = queue.IdleThresholdSeconds,
    ResumeOnServiceStart = queue.ResumeOnServiceStart,
    NotificationLevel = queue.NotificationLevel,
    FailurePolicy = queue.FailurePolicy,
    DeviceRecovery = queue.DeviceRecovery,
    LinkedTemplate = templateName,
    LinkedGame = gameName,
    Entries = entries
  };

  /// <summary>Collects the references and drops a repeat of the same type, name, and user.</summary>
  private sealed class ReferenceCollector {
    private readonly List<NameReference> _list = new();
    private readonly HashSet<string> _seen = new(StringComparer.OrdinalIgnoreCase);

    public void Add(NameReference reference) {
      if (_seen.Add(reference.Type + "|" + reference.Name + "|" + reference.UsedBy)) {
        _list.Add(reference);
      }
    }

    public void AddIfOwned(GamepackOwnerResolver owners, string ownerType, string referenceType, string id, Dictionary<string, string> names, string usedBy) {
      var owner = owners.Resolve(ownerType, id);
      if (owner.IsUser || !names.TryGetValue(id, out var name)) {
        return;
      }

      Add(new NameReference { Type = referenceType, Name = name, Gamepack = owner.Name, Version = owner.Version, UsedBy = usedBy });
    }

    public void AddImage(GamepackOwnerResolver owners, string imageId, string usedBy) {
      var owner = owners.Resolve(GamepackObjectTypes.Image, imageId);
      Add(new NameReference { Type = SettingsObjectTypes.Image, Name = imageId, Gamepack = owner.Name, Version = owner.Version, UsedBy = usedBy });
    }

    public List<NameReference> ToList() => _list
      .OrderBy(r => r.Type, StringComparer.Ordinal)
      .ThenBy(r => r.Name, StringComparer.OrdinalIgnoreCase)
      .ThenBy(r => r.UsedBy, StringComparer.OrdinalIgnoreCase)
      .ToList();
  }
}
