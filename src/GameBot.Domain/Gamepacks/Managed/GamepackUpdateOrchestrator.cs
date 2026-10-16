using GameBot.Domain.Gamepacks.Signing;
using GameBot.Domain.QueueSettings;
using GameBot.Domain.QueueTemplates;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace GameBot.Domain.Gamepacks.Managed;

/// <summary>
/// The managed gamepack update (feature 148). One call does these steps in order: check, find the scope, stop the affected
/// queues, back up the affected templates, install (or roll back), book disabled entries for new sequences, and restart
/// the queues that the flow stopped. A failure after the first stop restores the previous version and the previous queue
/// state. A dry run returns the plan and all problems and changes nothing.
/// <para>
/// The orchestrator is a singleton. One flag lets only one managed call run on the instance (FR-015). The flag is always
/// released. A dry run does not take it. The flow does not hold the gate of <see cref="GamepackService"/> for the whole
/// call, because the gate is not reentrant (research R2).
/// </para>
/// </summary>
public sealed partial class GamepackUpdateOrchestrator {
  private readonly GamepackService _gamepacks;
  private readonly IManagedQueueControl _queues;
  private readonly IQueueTemplateRepository _templates;
  private readonly QueueSettingsExporter _exporter;
  private readonly QueueSettingsImporter _importer;
  private readonly IManagedBackupStore _backups;
  private readonly ManagedUpdateOptions _options;
  private readonly ILogger _logger;
  private int _busy;

  /// <summary>Creates the orchestrator.</summary>
  /// <param name="gamepacks">The gamepack service.</param>
  /// <param name="queues">The queue port.</param>
  /// <param name="templates">The queue template store.</param>
  /// <param name="exporter">The settings exporter (feature 145).</param>
  /// <param name="importer">The settings importer (feature 145).</param>
  /// <param name="backups">The backup store.</param>
  /// <param name="options">The limits.</param>
  /// <param name="logger">The logger, or null.</param>
  public GamepackUpdateOrchestrator(
      GamepackService gamepacks,
      IManagedQueueControl queues,
      IQueueTemplateRepository templates,
      QueueSettingsExporter exporter,
      QueueSettingsImporter importer,
      IManagedBackupStore backups,
      ManagedUpdateOptions options,
      ILogger<GamepackUpdateOrchestrator>? logger = null) {
    _gamepacks = gamepacks ?? throw new ArgumentNullException(nameof(gamepacks));
    _queues = queues ?? throw new ArgumentNullException(nameof(queues));
    _templates = templates ?? throw new ArgumentNullException(nameof(templates));
    _exporter = exporter ?? throw new ArgumentNullException(nameof(exporter));
    _importer = importer ?? throw new ArgumentNullException(nameof(importer));
    _backups = backups ?? throw new ArgumentNullException(nameof(backups));
    _options = options ?? throw new ArgumentNullException(nameof(options));
    _logger = (ILogger?)logger ?? NullLogger.Instance;
  }

  /// <summary>True while a managed call (not a dry run) runs on the instance.</summary>
  public bool IsBusy => Volatile.Read(ref _busy) != 0;

  /// <summary>Runs one managed call.</summary>
  /// <param name="request">The request.</param>
  /// <param name="ct">The cancellation token.</param>
  public async Task<ManagedUpdateReport> RunAsync(ManagedUpdateRequest request, CancellationToken ct = default) {
    ArgumentNullException.ThrowIfNull(request);
    if (request.DryRun) {
      var plan = await PrepareAsync(request, ct).ConfigureAwait(false);
      return PlanReport(request, plan);
    }

    if (Interlocked.CompareExchange(ref _busy, 1, 0) != 0) {
      return new ManagedUpdateReport {
        Result = ManagedUpdateResults.Rejected,
        Reason = ManagedUpdateReasons.UpdateInProgress,
        GamepackName = request.GamepackName,
        Problems = new[] { InProgressProblem() }
      };
    }

    try {
      var startName = SafeName(request.GamepackName);
      var modeText = request.Mode.ToString();
      LogStart(_logger, startName, modeText);
      var report = await ExecuteAsync(request, ct).ConfigureAwait(false);
      var endName = SafeName(report.GamepackName);
      var reasonText = report.Reason ?? "none";
      LogEnd(_logger, endName, modeText, report.Result, reasonText, report.Restore.Restored);
      return report;
    }
    finally {
      Interlocked.Exchange(ref _busy, 0);
    }
  }

  // ---- check and scope ----

  private sealed class Prepared {
    public string? Name { get; set; }
    public string? Version { get; set; }
    public string? FileText { get; set; }
    public string? SignatureText { get; set; }
    public string? Reason { get; set; }
    public List<GamepackProblem> Problems { get; } = new();
    public SignatureCheckResult? Signature { get; set; }
    public GamepackInstalledSnapshot? Installed { get; set; }
    public ManagedUpdateScope Scope { get; set; } = ManagedUpdateScope.Compute(Array.Empty<QueueTemplate>(), Array.Empty<ManagedQueueInfo>(), Array.Empty<string>(), null);
    public int Created { get; set; }
    public int Updated { get; set; }
    public int Removed { get; set; }
    public List<string> NewFileSequenceNames { get; } = new();
    public bool HasFileNames { get; set; }
  }

  private async Task<Prepared> PrepareAsync(ManagedUpdateRequest request, CancellationToken ct) {
    var p = new Prepared { SignatureText = request.SignatureText };
    if (request.Mode == ManagedUpdateMode.Rollback) {
      await CheckRollbackAsync(request, p, ct).ConfigureAwait(false);
    }
    else {
      await CheckUpdateAsync(request, p, ct).ConfigureAwait(false);
    }

    var allTemplates = await _templates.ListAsync().ConfigureAwait(false);
    var allQueues = await _queues.ListAsync(ct).ConfigureAwait(false);
    var installedIds = p.Installed?.Record.Objects
      .Where(o => o.Type == GamepackObjectTypes.Sequence).Select(o => o.Id).ToList() ?? new List<string>();
    p.Scope = ManagedUpdateScope.Compute(allTemplates, allQueues, installedIds, request.Templates);

    foreach (var missing in p.Scope.MissingTemplates) {
      p.Problems.Add(new GamepackProblem(
        GamepackProblemCategories.Structure, GamepackObjectTypes.Manifest, missing,
        "The template list has a name that no template has.", Code: ManagedUpdateReasons.TemplateNotFound));
    }

    if (p.Scope.MissingTemplates.Count > 0) {
      p.Reason = ManagedUpdateReasons.TemplateNotFound;
    }

    if (request.DryRun && IsBusy) {
      p.Problems.Add(InProgressProblem());
      p.Reason ??= ManagedUpdateReasons.UpdateInProgress;
    }

    return p;
  }

  private async Task CheckUpdateAsync(ManagedUpdateRequest request, Prepared p, CancellationToken ct) {
    if (request.FileText is null) {
      p.Reason = ManagedUpdateReasons.InvalidRequest;
      return;
    }

    p.FileText = request.FileText;
    var outcome = await _gamepacks.InstallAsync(request.FileText, dryRun: true, request.SignatureText, ct).ConfigureAwait(false);
    p.Name = outcome.Name;
    p.Version = outcome.Version;
    p.Signature = outcome.Signature;
    p.Problems.AddRange(outcome.Problems);
    switch (outcome.Status) {
      case GamepackInstallStatus.Ok:
        p.Created = outcome.Created;
        p.Updated = outcome.Updated;
        p.Removed = outcome.Removed;
        break;
      case GamepackInstallStatus.InvalidJson:
        p.Reason = ManagedUpdateReasons.InvalidFile;
        break;
      case GamepackInstallStatus.SignatureRejected:
        p.Reason = ManagedUpdateReasons.SignatureRejected;
        break;
      default:
        p.Reason = ManagedUpdateReasons.ValidationFailed;
        break;
    }

    var parsed = GamepackFileSerializer.Parse(request.FileText);
    if (parsed.File is not null) {
      p.HasFileNames = true;
      p.NewFileSequenceNames.AddRange(parsed.File.Sequences.Select(s => s.Name).Where(n => !string.IsNullOrWhiteSpace(n)));
    }

    if (GamepackNameRules.IsValid(p.Name)) {
      p.Installed = await _gamepacks.GetInstalledSnapshotAsync(p.Name!, includeLiveContent: false, ct).ConfigureAwait(false);
    }
  }

  private async Task CheckRollbackAsync(ManagedUpdateRequest request, Prepared p, CancellationToken ct) {
    p.Name = request.GamepackName;
    if (!GamepackNameRules.IsValid(request.GamepackName)) {
      p.Reason = ManagedUpdateReasons.GamepackNotFound;
      return;
    }

    // The live content is read only for a real call. A dry run does not need it.
    p.Installed = await _gamepacks.GetInstalledSnapshotAsync(request.GamepackName!, includeLiveContent: !request.DryRun, ct).ConfigureAwait(false);
    if (p.Installed is null) {
      p.Reason = ManagedUpdateReasons.GamepackNotFound;
      return;
    }

    p.Name = p.Installed.Record.Name;
    var previous = p.Installed.Record.Previous;
    if (previous is null || p.Installed.PreviousFileText is null) {
      p.Reason = ManagedUpdateReasons.NoPreviousVersion;
      return;
    }

    p.Version = previous.Version;
    p.FileText = p.Installed.PreviousFileText;
    var parsed = GamepackFileSerializer.Parse(p.FileText);
    if (parsed.File is null) {
      p.Problems.AddRange(parsed.Problems);
      p.Reason = ManagedUpdateReasons.RollbackRefused;
      return;
    }

    p.HasFileNames = true;
    p.NewFileSequenceNames.AddRange(parsed.File.Sequences.Select(s => s.Name).Where(n => !string.IsNullOrWhiteSpace(n)));
  }

  private static GamepackProblem InProgressProblem() => new(
    GamepackProblemCategories.Conflict, GamepackObjectTypes.Manifest, null,
    "A managed call runs on this instance. Try again when it ends.", Code: ManagedUpdateReasons.UpdateInProgress);

  // ---- names and new entries ----

  private static HashSet<string> NewNamesOf(Prepared p) {
    var oldNames = OldSequences(p).Select(o => o.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
    return p.NewFileSequenceNames.Where(n => !oldNames.Contains(n)).ToHashSet(StringComparer.OrdinalIgnoreCase);
  }

  private static List<GamepackObjectRef> OldSequences(Prepared p) =>
    p.Installed?.Record.Objects.Where(o => o.Type == GamepackObjectTypes.Sequence).ToList() ?? new List<GamepackObjectRef>();

  private static List<NewSequence> RemovedSequences(Prepared p) {
    var newNames = p.NewFileSequenceNames.ToHashSet(StringComparer.OrdinalIgnoreCase);
    return p.HasFileNames
      ? OldSequences(p).Where(o => !newNames.Contains(o.Name)).Select(o => new NewSequence(o.Id, o.Name)).ToList()
      : new List<NewSequence>();
  }

  private static bool IsSameVersion(ManagedUpdateRequest request, Prepared p) =>
    request.Mode == ManagedUpdateMode.Update
    && p.Installed is not null
    && string.Equals(p.Installed.Record.Version, p.Version, StringComparison.Ordinal);

  // ---- dry run ----

  private static ManagedUpdateReport PlanReport(ManagedUpdateRequest request, Prepared p) {
    if (p.Reason is not null) {
      return RejectedReport(request, p);
    }

    var newNames = NewNamesOf(p);
    var planned = newNames.Select(n => new NewSequence("new:" + n, n)).ToList();
    var removed = RemovedSequences(p);
    var newEntries = new List<EntryRef>();
    var removedEntries = new List<EntryRef>();
    foreach (var template in p.Scope.AffectedTemplates) {
      newEntries.AddRange(TemplateEntryBooker.Plan(template, planned));
      removedEntries.AddRange(TemplateEntryBooker.FindRemovedSequenceEntries(template, removed));
    }

    return new ManagedUpdateReport {
      Result = ManagedUpdateResults.Planned,
      DryRun = true,
      GamepackName = p.Name,
      GamepackVersion = p.Version,
      PreviousVersion = PreviousVersionOf(p),
      StoppedQueues = p.Scope.QueuesToStop.Select(ToRef(p)).ToList(),
      Created = p.Created,
      Updated = p.Updated,
      Removed = p.Removed,
      NewEntries = newEntries,
      RemovedSequenceEntries = removedEntries,
      NotAffectedTemplates = p.Scope.NotAffectedTemplates,
      Problems = p.Problems,
      Signature = p.Signature
    };
  }

  private static ManagedUpdateReport RejectedReport(ManagedUpdateRequest request, Prepared p) => new() {
    Result = ManagedUpdateResults.Rejected,
    Reason = p.Reason,
    DryRun = request.DryRun,
    GamepackName = p.Name,
    GamepackVersion = p.Version,
    PreviousVersion = PreviousVersionOf(p),
    NotAffectedTemplates = p.Scope.NotAffectedTemplates,
    Problems = p.Problems,
    Signature = p.Signature
  };

  // For an update and for a rollback, the version that is installed before the call.
  private static string? PreviousVersionOf(Prepared p) => p.Installed?.Record.Version;

  private static Func<ManagedQueueInfo, QueueRef> ToRef(Prepared p) {
    var names = p.Scope.AffectedTemplates.ToDictionary(t => t.Id, t => t.Name, StringComparer.OrdinalIgnoreCase);
    return q => new QueueRef(q.Id, q.Name, q.LinkedTemplateId is not null && names.TryGetValue(q.LinkedTemplateId, out var n) ? n : null);
  }

  // ---- the real call ----

  private sealed class RunState {
    public List<ManagedQueueInfo> Stopped { get; } = new();
    public string? BackupId { get; set; }
    public bool InstallAttempted { get; set; }
    public bool InstallApplied { get; set; }
    public bool InstallPartial { get; set; }
    public int Created { get; set; }
    public int Updated { get; set; }
    public int Removed { get; set; }
    public List<EntryRef> NewEntries { get; } = new();
    public List<EntryRef> RemovedEntries { get; } = new();
  }

  [System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1064:Exceptions should be public", Justification = "Private control-flow type. It never leaves the orchestrator.")]
  [System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1032:Implement standard exception constructors", Justification = "Private control-flow type. It never leaves the orchestrator.")]
  private sealed class StepException : Exception {
    public StepException(string reason, string message)
      : base(message) {
      Reason = reason;
    }

    public string Reason { get; }
  }

  private async Task<ManagedUpdateReport> ExecuteAsync(ManagedUpdateRequest request, CancellationToken ct) {
    var p = await PrepareAsync(request, ct).ConfigureAwait(false);
    if (p.Reason is not null) {
      return RejectedReport(request, p);
    }

    if (IsSameVersion(request, p)) {
      return new ManagedUpdateReport {
        Result = ManagedUpdateResults.Done,
        GamepackName = p.Name,
        GamepackVersion = p.Version,
        PreviousVersion = p.Installed!.Record.Version,
        NotAffectedTemplates = p.Scope.NotAffectedTemplates,
        Signature = p.Signature
      };
    }

    var state = new RunState();
    try {
      await StopStepAsync(p, state, ct).ConfigureAwait(false);
      await BackupStepAsync(p, state, ct).ConfigureAwait(false);
      await InstallStepAsync(request, p, state, ct).ConfigureAwait(false);
      await BookStepAsync(p, state, ct).ConfigureAwait(false);
    }
    catch (StepException ex) {
      return await FailAsync(request, p, state, ex.Reason, ex.Message).ConfigureAwait(false);
    }
    catch (OperationCanceledException) {
      await FailAsync(request, p, state, ManagedUpdateReasons.Unexpected, "The call was cancelled.").ConfigureAwait(false);
      throw;
    }
    catch (Exception ex) {
      return await FailAsync(request, p, state, ManagedUpdateReasons.Unexpected, ex.Message).ConfigureAwait(false);
    }

    return await RestartStepAsync(request, p, state, ct).ConfigureAwait(false);
  }

  private async Task StopStepAsync(Prepared p, RunState state, CancellationToken ct) {
    foreach (var queue in p.Scope.QueuesToStop) {
      // The flow records the queue first. A stop that throws can still leave the queue stopped, and the restore starts it.
      state.Stopped.Add(queue);
      var stopped = await _queues.StopAsync(queue.Id, _options.StopTimeout, ct).ConfigureAwait(false);
      if (!stopped) {
        throw new StepException(ManagedUpdateReasons.StopTimeout, $"The queue '{queue.Name}' did not stop in the time limit.");
      }
    }
  }

  private async Task BackupStepAsync(Prepared p, RunState state, CancellationToken ct) {
    if (p.Scope.AffectedTemplates.Count == 0) {
      return;
    }

    try {
      var document = await _exporter.ExportAsync(ct).ConfigureAwait(false);
      state.BackupId = await _backups.WriteAsync(p.Name!, Reduce(document, p.Scope.AffectedTemplates), ct).ConfigureAwait(false);
    }
    catch (Exception ex) when (ex is not OperationCanceledException) {
      throw new StepException(ManagedUpdateReasons.BackupFailed, "The backup failed: " + ex.Message);
    }
  }

  private static SettingsDocument Reduce(SettingsDocument document, IReadOnlyList<QueueTemplate> affected) {
    var names = affected.Select(t => t.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
    var templates = document.QueueTemplates.Where(t => names.Contains(t.Name)).ToList();
    var queues = document.Queues.Where(q => q.LinkedTemplate is not null && names.Contains(q.LinkedTemplate)).ToList();
    var users = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
    foreach (var t in templates) {
      users.Add(SettingsObjectTypes.QueueTemplate + ":" + t.Name);
    }

    foreach (var q in queues) {
      users.Add(SettingsObjectTypes.Queue + ":" + q.Name);
    }

    return new SettingsDocument {
      FormatVersion = document.FormatVersion,
      ExportedAtUtc = document.ExportedAtUtc,
      Queues = queues,
      QueueTemplates = templates,
      References = document.References.Where(r => users.Contains(r.UsedBy)).ToList()
    };
  }

  private async Task InstallStepAsync(ManagedUpdateRequest request, Prepared p, RunState state, CancellationToken ct) {
    state.InstallAttempted = true;
    if (request.Mode == ManagedUpdateMode.Rollback) {
      var outcome = await _gamepacks.RollbackAsync(p.Name!, ct).ConfigureAwait(false);
      if (outcome.Status != GamepackRollbackStatus.RolledBack) {
        state.InstallPartial = outcome.Status == GamepackRollbackStatus.PartialRestore;
        var reason = outcome.Status == GamepackRollbackStatus.Refused ? ManagedUpdateReasons.RollbackRefused : ManagedUpdateReasons.InstallFailed;
        throw new StepException(reason, "The rollback failed. " + outcome.ErrorMessage);
      }

      state.InstallApplied = true;
      state.Created = outcome.Created;
      state.Updated = outcome.Updated;
      state.Removed = outcome.Removed;
      return;
    }

    var installed = await _gamepacks.InstallAsync(p.FileText!, dryRun: false, p.SignatureText, ct).ConfigureAwait(false);
    if (installed.Status != GamepackInstallStatus.Ok) {
      state.InstallPartial = installed.Status == GamepackInstallStatus.PartialRestore;
      throw new StepException(ManagedUpdateReasons.InstallFailed, "The install failed. " + installed.ErrorMessage);
    }

    state.InstallApplied = true;
    state.Created = installed.Created;
    state.Updated = installed.Updated;
    state.Removed = installed.Removed;
  }

  private async Task BookStepAsync(Prepared p, RunState state, CancellationToken ct) {
    if (p.Scope.AffectedTemplates.Count == 0) {
      return;
    }

    try {
      var after = await _gamepacks.GetInstalledSnapshotAsync(p.Name!, includeLiveContent: false, ct).ConfigureAwait(false);
      var newNames = NewNamesOf(p);
      var newSequences = after?.Record.Objects
        .Where(o => o.Type == GamepackObjectTypes.Sequence && newNames.Contains(o.Name))
        .Select(o => new NewSequence(o.Id, o.Name)).ToList() ?? new List<NewSequence>();
      var removed = RemovedSequences(p);
      foreach (var affected in p.Scope.AffectedTemplates) {
        var template = await _templates.GetAsync(affected.Id).ConfigureAwait(false);
        if (template is null) {
          continue;
        }

        state.RemovedEntries.AddRange(TemplateEntryBooker.FindRemovedSequenceEntries(template, removed));
        var booked = TemplateEntryBooker.Book(template, newSequences);
        if (booked.Count > 0) {
          await _templates.UpdateAsync(template).ConfigureAwait(false);
          state.NewEntries.AddRange(booked);
        }
      }
    }
    catch (Exception ex) when (ex is not OperationCanceledException) {
      throw new StepException(ManagedUpdateReasons.BookingFailed, "The booking of the new entries failed: " + ex.Message);
    }
  }

  private async Task<ManagedUpdateReport> RestartStepAsync(ManagedUpdateRequest request, Prepared p, RunState state, CancellationToken ct) {
    var toRef = ToRef(p);
    var restarted = new List<QueueRef>();
    var notRestarted = new List<QueueRef>();
    foreach (var queue in state.Stopped) {
      var error = await StartSafeAsync(queue.Id, ct).ConfigureAwait(false);
      if (error is null) {
        restarted.Add(toRef(queue));
      }
      else {
        notRestarted.Add(toRef(queue) with { Error = error });
      }
    }

    return new ManagedUpdateReport {
      Result = ManagedUpdateResults.Done,
      GamepackName = p.Name,
      GamepackVersion = p.Version,
      PreviousVersion = PreviousVersionOf(p),
      StoppedQueues = state.Stopped.Select(toRef).ToList(),
      RestartedQueues = restarted,
      NotRestartedQueues = notRestarted,
      BackupId = state.BackupId,
      Created = state.Created,
      Updated = state.Updated,
      Removed = state.Removed,
      NewEntries = state.NewEntries,
      RemovedSequenceEntries = state.RemovedEntries,
      NotAffectedTemplates = p.Scope.NotAffectedTemplates,
      Problems = p.Problems,
      Signature = p.Signature
    };
  }

  private async Task<string?> StartSafeAsync(string queueId, CancellationToken ct) {
    try {
      return await _queues.StartAsync(queueId, ct).ConfigureAwait(false);
    }
    catch (Exception ex) when (ex is not OperationCanceledException) {
      return ex.Message;
    }
  }

  // ---- restore ----

  private async Task<ManagedUpdateReport> FailAsync(ManagedUpdateRequest request, Prepared p, RunState state, string reason, string message) {
    // The restore ignores the token of the request, so a client that goes away cannot stop it half way.
    var ct = CancellationToken.None;
    var steps = new List<RestoreStep>();
    var failedObjects = new List<string>();

    await RestoreGamepackAsync(request, p, state, steps, failedObjects, ct).ConfigureAwait(false);
    await RestoreTemplatesAsync(state, steps, failedObjects, ct).ConfigureAwait(false);

    var toRef = ToRef(p);
    var restarted = new List<QueueRef>();
    var notRestarted = new List<QueueRef>();
    foreach (var queue in state.Stopped) {
      var error = await StartSafeAsync(queue.Id, ct).ConfigureAwait(false);
      steps.Add(new RestoreStep("start_queue:" + queue.Name, error is null ? "ok" : "failed", error is null ? null : ManagedUpdateReasons.RestartFailed + ": " + error));
      if (error is null) {
        restarted.Add(toRef(queue));
      }
      else {
        notRestarted.Add(toRef(queue) with { Error = error });
        failedObjects.Add("queue:" + queue.Name);
      }
    }

    LogFailed(_logger, SafeName(p.Name), reason, message);
    return new ManagedUpdateReport {
      Result = ManagedUpdateResults.Failed,
      Reason = reason,
      GamepackName = p.Name,
      GamepackVersion = p.Version,
      PreviousVersion = PreviousVersionOf(p),
      StoppedQueues = state.Stopped.Select(toRef).ToList(),
      RestartedQueues = restarted,
      NotRestartedQueues = notRestarted,
      BackupId = state.BackupId,
      NotAffectedTemplates = p.Scope.NotAffectedTemplates,
      Detail = message,
      Problems = p.Problems,
      Signature = p.Signature,
      Restore = new RestoreReport {
        Happened = steps.Count > 0,
        Restored = steps.Count > 0 && steps.All(s => s.Outcome == "ok"),
        Steps = steps,
        FailedObjects = failedObjects
      }
    };
  }

  private async Task RestoreGamepackAsync(ManagedUpdateRequest request, Prepared p, RunState state, List<RestoreStep> steps, List<string> failedObjects, CancellationToken ct) {
    if (!state.InstallAttempted) {
      return;
    }

    var name = p.Name!;
    if (request.Mode == ManagedUpdateMode.Rollback) {
      if (state.InstallApplied) {
        // The restore does not call the rollback a second time. It puts back the content that the flow recorded.
        var error = await TryAsync(() => _gamepacks.ReapplyInstalledAsync(p.Installed!, ct)).ConfigureAwait(false);
        AddStep(steps, failedObjects, "gamepack_reapply", error, "gamepack:" + name);
      }
      else if (state.InstallPartial) {
        AddStep(steps, failedObjects, "gamepack_partial_restore", "The rollback restore was partial.", "gamepack:" + name);
      }

      return;
    }

    if (p.Installed is null) {
      // A failed first install: return to "no gamepack installed". The applier restore does most of the work. The flow removes what is left.
      var left = await _gamepacks.GetInstalledSnapshotAsync(name, includeLiveContent: false, ct).ConfigureAwait(false);
      if (left is null && !state.InstallPartial) {
        steps.Add(new RestoreStep("gamepack_verify_empty", "ok"));
        return;
      }

      var failed = await _gamepacks.RemoveInstalledAsync(name, p.FileText, ct).ConfigureAwait(false);
      steps.Add(new RestoreStep("gamepack_remove", failed.Count == 0 ? "ok" : "failed", failed.Count == 0 ? null : "Objects stay: " + string.Join(", ", failed)));
      failedObjects.AddRange(failed);
      return;
    }

    if (state.InstallApplied) {
      var rolled = await _gamepacks.RollbackAsync(name, ct).ConfigureAwait(false);
      AddStep(steps, failedObjects, "gamepack_rollback", rolled.Status == GamepackRollbackStatus.RolledBack ? null : "The rollback gave the status " + rolled.Status + ".", "gamepack:" + name);
      var metadata = await TryAsync(async () => {
        await _gamepacks.RestorePreviousMetadataAsync(p.Installed, ct).ConfigureAwait(false);
        return null;
      }).ConfigureAwait(false);
      AddStep(steps, failedObjects, "gamepack_metadata", metadata, "gamepack:" + name);
    }
    else if (state.InstallPartial) {
      AddStep(steps, failedObjects, "gamepack_partial_restore", "The install restore was partial.", "gamepack:" + name);
    }
  }

  private async Task RestoreTemplatesAsync(RunState state, List<RestoreStep> steps, List<string> failedObjects, CancellationToken ct) {
    if (state.BackupId is null) {
      return;
    }

    var text = _backups.Read(state.BackupId);
    if (text is null) {
      AddStep(steps, failedObjects, "settings_import", "The backup file is missing.", "backup:" + state.BackupId);
      return;
    }

    try {
      var outcome = await _importer.ImportAsync(text, ct).ConfigureAwait(false);
      if (outcome.Status == ImportStatus.Ok) {
        steps.Add(new RestoreStep("settings_import", "ok"));
        return;
      }

      steps.Add(new RestoreStep("settings_import", "failed", outcome.Status.ToString()));
      var bad = outcome.Report?.Failed is { } f ? new[] { f.Type + ":" + f.Name } : new[] { "settings" };
      failedObjects.AddRange(bad);
    }
    catch (Exception ex) when (ex is not OperationCanceledException) {
      AddStep(steps, failedObjects, "settings_import", ex.Message, "settings");
    }
  }

  private static async Task<string?> TryAsync(Func<Task<string?>> action) {
    try {
      return await action().ConfigureAwait(false);
    }
    catch (Exception ex) when (ex is not OperationCanceledException) {
      return ex.Message;
    }
  }

  private static void AddStep(List<RestoreStep> steps, List<string> failedObjects, string step, string? error, string objectName) {
    steps.Add(new RestoreStep(step, error is null ? "ok" : "failed", error));
    if (error is not null) {
      failedObjects.Add(objectName);
    }
  }

  private static string SafeName(string? name) => GamepackNameRules.IsValid(name) ? name! : "(not valid)";

  [LoggerMessage(EventId = 1481, Level = LogLevel.Information, Message = "A managed gamepack call starts. Gamepack: {Name}. Mode: {Mode}.")]
  private static partial void LogStart(ILogger logger, string name, string mode);

  [LoggerMessage(EventId = 1482, Level = LogLevel.Information, Message = "A managed gamepack call ends. Gamepack: {Name}. Mode: {Mode}. Result: {Result}. Reason: {Reason}. Restored: {Restored}.")]
  private static partial void LogEnd(ILogger logger, string name, string mode, string result, string reason, bool restored);

  [LoggerMessage(EventId = 1483, Level = LogLevel.Warning, Message = "A managed gamepack call failed. Gamepack: {Name}. Reason: {Reason}. Detail: {Detail}.")]
  private static partial void LogFailed(ILogger logger, string name, string reason, string detail);
}
