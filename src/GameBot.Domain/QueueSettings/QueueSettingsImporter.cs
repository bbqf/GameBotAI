using GameBot.Domain.Commands;
using GameBot.Domain.Games;
using GameBot.Domain.Gamepacks;
using GameBot.Domain.Images;
using GameBot.Domain.Queues;
using GameBot.Domain.QueueTemplates;

namespace GameBot.Domain.QueueSettings;

/// <summary>
/// Imports a settings document by name (feature 145). The import checks the whole document first. It then holds the
/// gamepack gate (FR-017), plans each item, and writes commands, sequences, templates, and queues in this order. It
/// never writes a gamepack-owned object (FR-006), never deletes (FR-007), never stops or starts a queue (FR-016), and
/// does nothing for an object that already has the document values (FR-011). A repository failure stops the apply and
/// gives a partial report (FR-018). A write that is done stays.
/// </summary>
public sealed class QueueSettingsImporter {
  private readonly ISequenceRepository _sequences;
  private readonly ICommandRepository _commands;
  private readonly IQueueRepository _queues;
  private readonly IQueueRuntimeStore _runtime;
  private readonly IQueueTemplateRepository _templates;
  private readonly IGameRepository _games;
  private readonly IImageRepository _images;
  private readonly GamepackService _gamepacks;
  private readonly TimeProvider _time;

  /// <summary>Creates the importer.</summary>
  /// <param name="sequences">The sequence store.</param>
  /// <param name="commands">The command store.</param>
  /// <param name="queues">The queue store.</param>
  /// <param name="runtime">The runtime store with the queue entries.</param>
  /// <param name="templates">The queue template store.</param>
  /// <param name="games">The game store.</param>
  /// <param name="images">The image store.</param>
  /// <param name="gamepacks">The gamepack service, for the owner records and the gate.</param>
  /// <param name="timeProvider">The clock, or null.</param>
  public QueueSettingsImporter(
      ISequenceRepository sequences,
      ICommandRepository commands,
      IQueueRepository queues,
      IQueueRuntimeStore runtime,
      IQueueTemplateRepository templates,
      IGameRepository games,
      IImageRepository images,
      GamepackService gamepacks,
      TimeProvider? timeProvider = null) {
    _sequences = sequences ?? throw new ArgumentNullException(nameof(sequences));
    _commands = commands ?? throw new ArgumentNullException(nameof(commands));
    _queues = queues ?? throw new ArgumentNullException(nameof(queues));
    _runtime = runtime ?? throw new ArgumentNullException(nameof(runtime));
    _templates = templates ?? throw new ArgumentNullException(nameof(templates));
    _games = games ?? throw new ArgumentNullException(nameof(games));
    _images = images ?? throw new ArgumentNullException(nameof(images));
    _gamepacks = gamepacks ?? throw new ArgumentNullException(nameof(gamepacks));
    _time = timeProvider ?? TimeProvider.System;
  }

  /// <summary>Imports the text of a settings document.</summary>
  /// <param name="json">The body text.</param>
  /// <param name="ct">The cancellation token.</param>
  public async Task<ImportOutcome> ImportAsync(string json, CancellationToken ct = default) {
    ArgumentNullException.ThrowIfNull(json);
    var check = SettingsDocumentValidator.Validate(json);
    if (!check.IsValid) {
      return new ImportOutcome { Status = ImportStatus.Invalid, Errors = check.Errors };
    }

    var document = check.Document!;
    return await _gamepacks.RunExclusiveAsync(() => RunAsync(document, ct), ct).ConfigureAwait(false);
  }

  private async Task<ImportOutcome> RunAsync(SettingsDocument document, CancellationToken ct) {
    var plan = await PlanAsync(document, ct).ConfigureAwait(false);
    return await ApplyAsync(plan).ConfigureAwait(false);
  }

  // ---- apply ----

  private static async Task<ImportOutcome> ApplyAsync(Plan plan) {
    var applied = new List<ImportItem>();
    var notApplied = new List<ImportItem>();
    ImportItem? failed = null;
    foreach (var planned in plan.Items) {
      if (failed is not null) {
        // After a failure, an item that needs no write is still true. An item that needs a write is not applied.
        (planned.Write is null ? applied : notApplied).Add(planned.Item);
        continue;
      }

      if (planned.Write is null) {
        applied.Add(planned.Item);
        continue;
      }

      try {
        await planned.Write().ConfigureAwait(false);
        applied.Add(planned.Item);
      }
      catch (Exception ex) when (ex is not OperationCanceledException) {
        failed = planned.Item.With(ImportOutcomes.Failed, "The write failed: " + ex.Message);
      }
    }

    var report = new ImportReport {
      Items = applied,
      UnresolvedReferences = plan.Unresolved.ToList(),
      Summary = new ImportSummary {
        Created = applied.Count(i => i.Outcome == ImportOutcomes.Created),
        Updated = applied.Count(i => i.Outcome == ImportOutcomes.Updated),
        Unchanged = applied.Count(i => i.Outcome == ImportOutcomes.Unchanged),
        Skipped = applied.Count(i => i.Outcome == ImportOutcomes.Skipped)
      },
      Failed = failed,
      NotApplied = notApplied
    };
    return new ImportOutcome { Status = failed is null ? ImportStatus.Ok : ImportStatus.WriteFailed, Report = report };
  }

  // ---- plan ----

  private sealed record PlannedItem(ImportItem Item, Func<Task>? Write);

  private sealed class Plan {
    private readonly Dictionary<string, UnresolvedReference> _unresolved = new(StringComparer.OrdinalIgnoreCase);

    public List<PlannedItem> Items { get; } = new();

    public IEnumerable<UnresolvedReference> Unresolved => _unresolved.Values;

    public void AddUnresolved(UnresolvedReference reference) =>
      _unresolved.TryAdd(reference.Type + "|" + reference.Name + "|" + reference.UsedBy, reference);
  }

  private async Task<Plan> PlanAsync(SettingsDocument document, CancellationToken ct) {
    var owners = _gamepacks.LoadOwners();
    var existingCommands = await _commands.ListAsync(ct).ConfigureAwait(false);
    var existingSequences = await _sequences.ListAsync().ConfigureAwait(false);
    var existingTemplates = await _templates.ListAsync().ConfigureAwait(false);
    var existingQueues = await _queues.ListAsync().ConfigureAwait(false);
    var games = await _games.ListAsync(ct).ConfigureAwait(false);
    var imageIds = new HashSet<string>(await _images.ListIdsAsync(ct).ConfigureAwait(false), StringComparer.OrdinalIgnoreCase);

    var context = new PlanContext(document, owners, imageIds, new Plan(), new NameMap());
    foreach (var command in existingCommands) {
      context.Map.Add(SettingsObjectTypes.Command, command.Name, command.Id);
    }

    foreach (var sequence in existingSequences) {
      context.Map.Add(SettingsObjectTypes.Sequence, sequence.Name, sequence.Id);
    }

    foreach (var template in existingTemplates) {
      context.Map.Add(SettingsObjectTypes.QueueTemplate, template.Name, template.Id);
    }

    foreach (var game in games) {
      context.Map.Add(SettingsObjectTypes.Game, game.Name, game.Id);
    }

    PlanCommands(context, existingCommands);
    PlanSequences(context, existingSequences, existingCommands);
    PlanTemplates(context, existingTemplates);
    PlanQueues(context, existingQueues);
    return context.Plan;
  }

  private sealed record PlanContext(
      SettingsDocument Document,
      GamepackOwnerResolver Owners,
      HashSet<string> ImageIds,
      Plan Plan,
      NameMap Map);

  private static ImportItem NewItem(string type, string name, string outcome, string? reason = null, bool restart = false) =>
    new() { Type = type, Name = name, Outcome = outcome, Reason = reason, RestartRequired = restart };

  private static string NewId() => Guid.NewGuid().ToString("N");

  private static void Unresolved(PlanContext context, string type, string name, string usedBy) {
    var hint = context.Document.References.FirstOrDefault(r =>
      string.Equals(r.Type, type, StringComparison.Ordinal)
      && string.Equals(r.Name, name, StringComparison.OrdinalIgnoreCase)
      && r.Gamepack is not null);
    context.Plan.AddUnresolved(new UnresolvedReference {
      Type = type,
      Name = name,
      UsedBy = usedBy,
      Gamepack = hint?.Gamepack,
      Version = hint?.Version
    });
  }

  private static void CheckImages(PlanContext context, IEnumerable<string> imageIds, string usedBy) {
    foreach (var id in imageIds.Distinct(StringComparer.OrdinalIgnoreCase)) {
      if (!context.ImageIds.Contains(id)) {
        Unresolved(context, SettingsObjectTypes.Image, id, usedBy);
      }
    }
  }

  private static void CheckCommands(PlanContext context, IEnumerable<string> names, string usedBy) {
    foreach (var name in names.Distinct(StringComparer.OrdinalIgnoreCase)) {
      if (!context.Map.TryGetId(SettingsObjectTypes.Command, name, out _)) {
        Unresolved(context, SettingsObjectTypes.Command, name, usedBy);
      }
    }
  }

  private static string? MapCommand(PlanContext context, string name) =>
    context.Map.TryGetId(SettingsObjectTypes.Command, name, out var id) ? id : null;

  // Commands

  private void PlanCommands(PlanContext context, IReadOnlyList<Command> existing) {
    var namesById = existing.ToDictionary(c => c.Id, c => c.Name, StringComparer.OrdinalIgnoreCase);
    var docs = context.Document.Commands.Where(d => d is not null).ToList();
    var newIds = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
    foreach (var doc in docs) {
      var name = SettingsDocumentValidator.NameOf(doc)!;
      if (!existing.Any(c => SameName(c.Name, name))) {
        newIds[name] = NewId();
        context.Map.Add(SettingsObjectTypes.Command, name, newIds[name]);
      }
    }

    foreach (var doc in docs) {
      var name = SettingsDocumentValidator.NameOf(doc)!;
      var matches = existing.Where(c => SameName(c.Name, name)).ToList();
      var usedBy = SettingsObjectTypes.Command + ":" + name;
      if (matches.Any(c => !context.Owners.Resolve(GamepackObjectTypes.Command, c.Id).IsUser)) {
        context.Plan.Items.Add(new PlannedItem(NewItem(SettingsObjectTypes.Command, name, ImportOutcomes.Skipped, ImportReasons.OwnedByGamepack), null));
        continue;
      }

      var model = SettingsNormalizer.ReadCommand(doc);
      CheckCommands(context, GamepackReferences.CommandIds(model), usedBy);
      CheckImages(context, GamepackReferences.ImageIds(model), usedBy);

      var current = matches.FirstOrDefault();
      if (current is null) {
        var id = newIds[name];
        context.Plan.Items.Add(new PlannedItem(NewItem(SettingsObjectTypes.Command, name, ImportOutcomes.Created), async () => {
          var command = SettingsNormalizer.ReadCommand(doc);
          GamepackReferences.RewriteCommandIds(command, n => MapCommand(context, n));
          command.Id = id;
          await _commands.AddAsync(command).ConfigureAwait(false);
        }));
        continue;
      }

      var same = SettingsNormalizer.CanonicalCommand(SettingsNormalizer.CommandToDoc(current, i => namesById.GetValueOrDefault(i)))
        == SettingsNormalizer.CanonicalCommand(doc);
      if (same) {
        context.Plan.Items.Add(new PlannedItem(NewItem(SettingsObjectTypes.Command, name, ImportOutcomes.Unchanged), null));
        continue;
      }

      context.Plan.Items.Add(new PlannedItem(NewItem(SettingsObjectTypes.Command, name, ImportOutcomes.Updated), async () => {
        var command = SettingsNormalizer.ReadCommand(doc);
        GamepackReferences.RewriteCommandIds(command, n => MapCommand(context, n));
        command.Id = current.Id;
        command.TriggerId = current.TriggerId;
        var updated = await _commands.UpdateAsync(command).ConfigureAwait(false);
        if (updated is null) {
          throw new InvalidOperationException($"The command '{name}' could not be updated.");
        }
      }));
    }
  }

  // Sequences

  private void PlanSequences(PlanContext context, IReadOnlyList<CommandSequence> existing, IReadOnlyList<Command> existingCommands) {
    var commandNames = existingCommands.ToDictionary(c => c.Id, c => c.Name, StringComparer.OrdinalIgnoreCase);
    var docs = context.Document.Sequences.Where(d => d is not null).ToList();
    var newIds = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
    foreach (var doc in docs) {
      var name = SettingsDocumentValidator.NameOf(doc)!;
      if (!existing.Any(s => SameName(s.Name, name))) {
        newIds[name] = NewId();
        context.Map.Add(SettingsObjectTypes.Sequence, name, newIds[name]);
      }
    }

    foreach (var doc in docs) {
      var name = SettingsDocumentValidator.NameOf(doc)!;
      var matches = existing.Where(s => SameName(s.Name, name)).ToList();
      var usedBy = SettingsObjectTypes.Sequence + ":" + name;
      if (matches.Any(s => !context.Owners.Resolve(GamepackObjectTypes.Sequence, s.Id).IsUser)) {
        context.Plan.Items.Add(new PlannedItem(NewItem(SettingsObjectTypes.Sequence, name, ImportOutcomes.Skipped, ImportReasons.OwnedByGamepack), null));
        continue;
      }

      var model = SettingsNormalizer.ReadSequence(doc);
      CheckCommands(context, GamepackReferences.CommandIds(model), usedBy);
      CheckImages(context, GamepackReferences.ImageIds(model), usedBy);

      var current = matches.FirstOrDefault();
      if (current is null) {
        var id = newIds[name];
        context.Plan.Items.Add(new PlannedItem(NewItem(SettingsObjectTypes.Sequence, name, ImportOutcomes.Created), async () => {
          var sequence = SettingsNormalizer.ReadSequence(doc);
          GamepackReferences.RewriteCommandIds(sequence, n => MapCommand(context, n));
          sequence.Id = id;
          var now = _time.GetUtcNow();
          sequence.CreatedAt = now;
          sequence.UpdatedAt = now;
          await _sequences.CreateAsync(sequence).ConfigureAwait(false);
        }));
        continue;
      }

      var same = SettingsNormalizer.CanonicalSequence(SettingsNormalizer.SequenceToDoc(current, i => commandNames.GetValueOrDefault(i)))
        == SettingsNormalizer.CanonicalSequence(doc);
      if (same) {
        context.Plan.Items.Add(new PlannedItem(NewItem(SettingsObjectTypes.Sequence, name, ImportOutcomes.Unchanged), null));
        continue;
      }

      context.Plan.Items.Add(new PlannedItem(NewItem(SettingsObjectTypes.Sequence, name, ImportOutcomes.Updated), async () => {
        var sequence = SettingsNormalizer.ReadSequence(doc);
        GamepackReferences.RewriteCommandIds(sequence, n => MapCommand(context, n));
        sequence.Id = current.Id;
        sequence.CreatedAt = current.CreatedAt;
        sequence.UpdatedAt = _time.GetUtcNow();
        await _sequences.UpdateAsync(sequence).ConfigureAwait(false);
      }));
    }
  }

  // Queue templates

  private void PlanTemplates(PlanContext context, IReadOnlyList<QueueTemplate> existing) {
    var newIds = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
    foreach (var doc in context.Document.QueueTemplates.Where(t => t is not null)) {
      if (!existing.Any(t => SameName(t.Name, doc.Name))) {
        newIds[doc.Name] = NewId();
        context.Map.Add(SettingsObjectTypes.QueueTemplate, doc.Name, newIds[doc.Name]);
      }
    }

    foreach (var doc in context.Document.QueueTemplates.Where(t => t is not null)) {
      var usedBy = SettingsObjectTypes.QueueTemplate + ":" + doc.Name;
      var entries = new List<QueueTemplateEntryDoc>();
      var sequenceIds = new List<string>();
      foreach (var entry in doc.Entries) {
        if (!context.Map.TryGetId(SettingsObjectTypes.Sequence, entry.Sequence, out var sequenceId)) {
          Unresolved(context, SettingsObjectTypes.Sequence, entry.Sequence, usedBy);
          continue;
        }

        var canonicalName = context.Map.NameOf(SettingsObjectTypes.Sequence, sequenceId) ?? entry.Sequence;
        entries.Add(CopyEntry(entry, canonicalName));
        sequenceIds.Add(sequenceId);
      }

      var desired = new QueueTemplateDoc { Name = doc.Name, Entries = entries };
      var current = existing.FirstOrDefault(t => SameName(t.Name, doc.Name));
      if (current is null) {
        var id = newIds[doc.Name];
        context.Plan.Items.Add(new PlannedItem(NewItem(SettingsObjectTypes.QueueTemplate, doc.Name, ImportOutcomes.Created), async () => {
          var template = new QueueTemplate { Id = id, Name = doc.Name };
          FillEntries(template, entries, sequenceIds);
          await _templates.CreateAsync(template).ConfigureAwait(false);
        }));
        continue;
      }

      var currentDoc = ToTemplateDoc(current, context);
      if (SettingsNormalizer.Canonical(currentDoc) == SettingsNormalizer.Canonical(desired)) {
        context.Plan.Items.Add(new PlannedItem(NewItem(SettingsObjectTypes.QueueTemplate, doc.Name, ImportOutcomes.Unchanged), null));
        continue;
      }

      context.Plan.Items.Add(new PlannedItem(NewItem(SettingsObjectTypes.QueueTemplate, doc.Name, ImportOutcomes.Updated), async () => {
        current.Name = doc.Name;
        FillEntries(current, entries, sequenceIds);
        await _templates.UpdateAsync(current).ConfigureAwait(false);
      }));
    }
  }

  private static QueueTemplateEntryDoc CopyEntry(QueueTemplateEntryDoc entry, string sequenceName) => new() {
    Sequence = sequenceName,
    Enabled = entry.Enabled,
    ScheduleType = entry.ScheduleType,
    TimerTimeOfDay = entry.TimerTimeOfDay,
    TimerRelativeOffset = entry.TimerRelativeOffset,
    ParameterValues = entry.ParameterValues ?? Array.Empty<GameBot.Domain.Parameters.ParameterBinding>()
  };

  private static void FillEntries(QueueTemplate template, List<QueueTemplateEntryDoc> docs, List<string> sequenceIds) {
    template.Entries.Clear();
    for (var i = 0; i < docs.Count; i++) {
      var entry = new QueueTemplateEntry {
        SequenceId = sequenceIds[i],
        Enabled = docs[i].Enabled,
        ScheduleType = docs[i].ScheduleType,
        TimerTimeOfDay = docs[i].TimerTimeOfDay,
        TimerRelativeOffset = docs[i].TimerRelativeOffset
      };
      foreach (var binding in docs[i].ParameterValues) {
        entry.ParameterValues.Add(binding);
      }

      template.Entries.Add(entry);
    }
  }

  private static QueueTemplateDoc ToTemplateDoc(QueueTemplate template, PlanContext context) {
    var entries = new List<QueueTemplateEntryDoc>();
    foreach (var entry in template.Entries) {
      var name = context.Map.NameOf(SettingsObjectTypes.Sequence, entry.SequenceId);
      if (name is not null) {
        entries.Add(QueueSettingsExporter.ToEntryDoc(entry, name));
      }
    }

    return new QueueTemplateDoc { Name = template.Name, Entries = entries };
  }

  // Queues

  private void PlanQueues(PlanContext context, IReadOnlyList<ExecutionQueue> existing) {
    foreach (var doc in context.Document.Queues.Where(q => q is not null)) {
      var usedBy = SettingsObjectTypes.Queue + ":" + doc.Name;
      string? templateId = null;
      string? templateName = null;
      if (!string.IsNullOrWhiteSpace(doc.LinkedTemplate)) {
        if (context.Map.TryGetId(SettingsObjectTypes.QueueTemplate, doc.LinkedTemplate, out var found)) {
          templateId = found;
          templateName = context.Map.NameOf(SettingsObjectTypes.QueueTemplate, found) ?? doc.LinkedTemplate;
        }
        else {
          Unresolved(context, SettingsObjectTypes.QueueTemplate, doc.LinkedTemplate, usedBy);
        }
      }

      string? gameId = null;
      string? gameName = null;
      if (!string.IsNullOrWhiteSpace(doc.LinkedGame)) {
        if (context.Map.TryGetId(SettingsObjectTypes.Game, doc.LinkedGame, out var found)) {
          gameId = found;
          gameName = context.Map.NameOf(SettingsObjectTypes.Game, found) ?? doc.LinkedGame;
        }
        else {
          Unresolved(context, SettingsObjectTypes.Game, doc.LinkedGame, usedBy);
        }
      }

      var entryNames = new List<string>();
      var entryIds = new List<string>();
      foreach (var entry in doc.Entries) {
        if (context.Map.TryGetId(SettingsObjectTypes.Sequence, entry, out var sequenceId)) {
          entryNames.Add(context.Map.NameOf(SettingsObjectTypes.Sequence, sequenceId) ?? entry);
          entryIds.Add(sequenceId);
        }
        else {
          Unresolved(context, SettingsObjectTypes.Sequence, entry, usedBy);
        }
      }

      var desired = WithLinks(doc, templateName, gameName, entryNames);
      var current = existing.FirstOrDefault(q => SameName(q.Name, doc.Name));
      if (current is null) {
        context.Plan.Items.Add(new PlannedItem(NewItem(SettingsObjectTypes.Queue, doc.Name, ImportOutcomes.Created), async () => {
          var queue = new ExecutionQueue { Id = NewId() };
          Apply(queue, doc, templateId, gameId);
          await _queues.CreateAsync(queue).ConfigureAwait(false);
          if (entryIds.Count > 0) {
            _runtime.SetEntries(queue.Id, entryIds);
          }
        }));
        continue;
      }

      var currentDoc = ToQueueDoc(current, context);
      var settingsDiffer = SettingsNormalizer.Canonical(currentDoc, includeEntries: false) != SettingsNormalizer.Canonical(desired, includeEntries: false);
      var currentEntryIds = _runtime.GetEntries(current.Id).Select(e => e.SequenceId).ToList();
      var entriesDiffer = !currentEntryIds.SequenceEqual(entryIds, StringComparer.OrdinalIgnoreCase);
      if (!settingsDiffer && !entriesDiffer) {
        context.Plan.Items.Add(new PlannedItem(NewItem(SettingsObjectTypes.Queue, doc.Name, ImportOutcomes.Unchanged), null));
        continue;
      }

      var restart = _runtime.GetStatus(current.Id) != QueueExecutionStatus.Stopped;
      context.Plan.Items.Add(new PlannedItem(NewItem(SettingsObjectTypes.Queue, doc.Name, ImportOutcomes.Updated, restart: restart), async () => {
        if (settingsDiffer) {
          Apply(current, doc, templateId, gameId);
          await _queues.UpdateAsync(current).ConfigureAwait(false);
        }

        if (entriesDiffer) {
          _runtime.SetEntries(current.Id, entryIds);
        }
      }));
    }
  }

  private static void Apply(ExecutionQueue queue, QueueSettingsDoc doc, string? templateId, string? gameId) {
    queue.Name = doc.Name;
    queue.EmulatorSerial = doc.EmulatorSerial;
    queue.EmulatorInstanceName = doc.EmulatorInstanceName;
    queue.EmulatorInstanceIndex = doc.EmulatorInstanceIndex;
    queue.CycleExecution = doc.CycleExecution;
    queue.PauseWhenIdle = doc.PauseWhenIdle;
    queue.IdleThresholdSeconds = doc.IdleThresholdSeconds;
    queue.ResumeOnServiceStart = doc.ResumeOnServiceStart;
    queue.NotificationLevel = doc.NotificationLevel;
    queue.FailurePolicy = doc.FailurePolicy;
    queue.DeviceRecovery = doc.DeviceRecovery;
    queue.LinkedTemplateId = templateId;
    queue.LinkedGameId = gameId;
  }

  private static QueueSettingsDoc WithLinks(QueueSettingsDoc doc, string? templateName, string? gameName, IReadOnlyList<string> entries) =>
    QueueSettingsExporter.ToQueueDoc(ToQueue(doc), templateName, gameName, entries);

  private static ExecutionQueue ToQueue(QueueSettingsDoc doc) {
    var queue = new ExecutionQueue();
    Apply(queue, doc, null, null);
    return queue;
  }

  private QueueSettingsDoc ToQueueDoc(ExecutionQueue queue, PlanContext context) {
    var templateName = string.IsNullOrWhiteSpace(queue.LinkedTemplateId) ? null : context.Map.NameOf(SettingsObjectTypes.QueueTemplate, queue.LinkedTemplateId);
    var gameName = string.IsNullOrWhiteSpace(queue.LinkedGameId) ? null : context.Map.NameOf(SettingsObjectTypes.Game, queue.LinkedGameId);
    var entries = _runtime.GetEntries(queue.Id)
      .Select(e => context.Map.NameOf(SettingsObjectTypes.Sequence, e.SequenceId))
      .Where(n => n is not null)
      .Select(n => n!)
      .ToList();
    return QueueSettingsExporter.ToQueueDoc(queue, templateName, gameName, entries);
  }

  private static bool SameName(string left, string right) => string.Equals(left, right, StringComparison.OrdinalIgnoreCase);
}
