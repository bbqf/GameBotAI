using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using GameBot.Domain.Commands;
using GameBot.Domain.Images;
using GameBot.Domain.Queues;
using GameBot.Domain.QueueSettings;
using GameBot.Domain.QueueTemplates;
using GameBot.Domain.Services;
using GameBot.Service.Endpoints;
using Microsoft.Extensions.Logging;

namespace GameBot.Service.Services.QueueExecution;

/// <summary>The result of a read of the differences, or of an apply (feature 171).</summary>
internal enum LiveApplyStatus {
  /// <summary>The differences were read.</summary>
  Ok,

  /// <summary>The selected differences are in the queue.</summary>
  Applied,

  /// <summary>No queue has this ID.</summary>
  QueueNotFound,

  /// <summary>The queue has no linked template, or the template does not exist.</summary>
  NoTemplate,

  /// <summary>The queue has no run that the apply can reach (not running, or the run is starting).</summary>
  NotRunning,

  /// <summary>The template or the run has entries with no ID. The operator upgrades the template first (FR-028).</summary>
  NeedsUpgrade,

  /// <summary>The queue is not in the state "paused" (FR-012).</summary>
  NotPaused,

  /// <summary>The template changed after the operator read the differences (FR-019).</summary>
  TemplateChanged,

  /// <summary>Another apply runs for this queue (FR-020).</summary>
  ApplyInProgress,

  /// <summary>A selected key is not a difference of this token.</summary>
  UnknownKey,

  /// <summary>The new entries are not valid. The queue is unchanged (FR-015, FR-016).</summary>
  Invalid
}

/// <summary>The result of the read of the differences of a queue.</summary>
internal sealed class LiveDiffOutcome {
  public LiveApplyStatus Status { get; init; }
  public string QueueId { get; init; } = string.Empty;
  public string TemplateId { get; init; } = string.Empty;
  public TemplateDiffResult? Diff { get; init; }
}

/// <summary>The result of an apply.</summary>
internal sealed class LiveApplyOutcome {
  public LiveApplyStatus Status { get; init; }
  public IReadOnlyList<string> Applied { get; init; } = Array.Empty<string>();
  public int PlanVersion { get; init; }
  public IReadOnlyList<string> UnknownKeys { get; init; } = Array.Empty<string>();
  public IReadOnlyList<string> Errors { get; init; } = Array.Empty<string>();
  public IReadOnlyList<ParameterValidationIssue> Issues { get; init; } = Array.Empty<ParameterValidationIssue>();
}

/// <summary>
/// Applies selected differences between the linked template and a paused queue (feature 171). The change is atomic:
/// the service builds the new entries off to the side, checks them, and only then swaps the run plan and the runtime
/// entries. If a check fails, the queue stays as it was (FR-015). A pause, an apply, and a resume never move a timer:
/// an entry with no selected difference keeps its consumed state (FR-008, FR-009).
/// </summary>
internal sealed partial class QueueLiveApplyService {
  private readonly IQueueRepository _queues;
  private readonly IQueueTemplateRepository _templates;
  private readonly ISequenceRepository _sequences;
  private readonly ICommandRepository _commands;
  private readonly IImageRepository _images;
  private readonly IQueueRunRegistry _runs;
  private readonly IQueueRuntimeStore _runtime;
  private readonly ILogger<QueueLiveApplyService> _logger;

  public QueueLiveApplyService(
      IQueueRepository queues,
      IQueueTemplateRepository templates,
      ISequenceRepository sequences,
      ICommandRepository commands,
      IImageRepository images,
      IQueueRunRegistry runs,
      IQueueRuntimeStore runtime,
      ILogger<QueueLiveApplyService> logger) {
    _queues = queues;
    _templates = templates;
    _sequences = sequences;
    _commands = commands;
    _images = images;
    _runs = runs;
    _runtime = runtime;
    _logger = logger;
  }

  /// <summary>Reads the differences between the run of a queue and its linked template. It changes nothing.</summary>
  public async Task<LiveDiffOutcome> GetDiffAsync(string queueId) {
    var context = await ResolveAsync(queueId).ConfigureAwait(false);
    if (context.Failure is { } failure) return new LiveDiffOutcome { Status = failure, QueueId = queueId };

    var names = await SequenceNamesAsync().ConfigureAwait(false);
    var diff = QueueTemplateDiff.Compute(context.Schedule!.Plan, context.Template!, names);
    return new LiveDiffOutcome { Status = LiveApplyStatus.Ok, QueueId = queueId, TemplateId = context.Template!.Id, Diff = diff };
  }

  /// <summary>Applies the selected differences to the paused queue.</summary>
  /// <param name="queueId">The queue.</param>
  /// <param name="baseToken">The token of the template that the operator reviewed.</param>
  /// <param name="selectedKeys">The keys of the selected differences.</param>
  public async Task<LiveApplyOutcome> ApplyAsync(string queueId, string? baseToken, IReadOnlyCollection<string> selectedKeys, CancellationToken ct = default) {
    ArgumentNullException.ThrowIfNull(selectedKeys);
    var context = await ResolveAsync(queueId).ConfigureAwait(false);
    if (context.Failure is { } failure) return new LiveApplyOutcome { Status = failure };

    var handle = context.Handle!;
    var schedule = context.Schedule!;
    var template = context.Template!;

    // The needs-upgrade refusal comes before the pause check: the operator must upgrade the template whatever the state is.
    if (template.NeedsUpgrade || schedule.Plan.HasIndexKeys) return new LiveApplyOutcome { Status = LiveApplyStatus.NeedsUpgrade };
    if (handle.OperatorPause != OperatorPauseState.Paused) return new LiveApplyOutcome { Status = LiveApplyStatus.NotPaused };
    if (!handle.TryEnterApply()) return new LiveApplyOutcome { Status = LiveApplyStatus.ApplyInProgress };
    try {
      var plan = schedule.Plan;
      var names = await SequenceNamesAsync().ConfigureAwait(false);
      var diff = QueueTemplateDiff.Compute(plan, template, names);
      if (!string.Equals(diff.BaseToken, baseToken, StringComparison.Ordinal)) {
        return new LiveApplyOutcome { Status = LiveApplyStatus.TemplateChanged };
      }

      var known = new HashSet<string>(diff.Differences.Select(d => d.Key), StringComparer.Ordinal);
      var unknown = selectedKeys.Where(k => !known.Contains(k)).Distinct(StringComparer.Ordinal).ToList();
      if (unknown.Count > 0) return new LiveApplyOutcome { Status = LiveApplyStatus.UnknownKey, UnknownKeys = unknown };

      var selected = new HashSet<string>(selectedKeys, StringComparer.Ordinal);
      // An empty selection applies nothing: the plan keeps its version, so the run loop has nothing to read again.
      if (selected.Count == 0) return new LiveApplyOutcome { Status = LiveApplyStatus.Applied, PlanVersion = plan.Version };
      var built = QueueLiveApplyBuilder.Build(plan, template, selected);

      var (errors, issues) = await ValidateAsync(built.TouchedEntries, names, ct).ConfigureAwait(false);
      if (errors.Count > 0 || issues.Count > 0) {
        return new LiveApplyOutcome { Status = LiveApplyStatus.Invalid, Errors = errors, Issues = issues };
      }

      // Nothing after this point can fail: both swaps work on memory only.
      var newPlan = QueueRunPlan.FromAll(built.AllEntries, built.AllKeys, plan.Version + 1);
      schedule.SwapPlan(newPlan, built.DroppedKeys, built.ClearedKeys);
      _runtime.ReplaceEntries(queueId, built.AllEntries.Select(e => new QueueEntry { EntryId = e.EntryId, SequenceId = e.SequenceId }));

      var applied = selected.OrderBy(k => k, StringComparer.Ordinal).ToList();
      var appliedText = string.Join(", ", applied);
      LogApplied(_logger, queueId, newPlan.Version, applied.Count, appliedText);
      return new LiveApplyOutcome { Status = LiveApplyStatus.Applied, Applied = applied, PlanVersion = newPlan.Version };
    }
    finally {
      handle.ExitApply();
    }
  }

  private sealed class Context {
    public LiveApplyStatus? Failure { get; init; }
    public QueueRunHandle? Handle { get; init; }
    public QueueRunSchedule? Schedule { get; init; }
    public QueueTemplate? Template { get; init; }
  }

  private async Task<Context> ResolveAsync(string queueId) {
    var queue = await _queues.GetAsync(queueId).ConfigureAwait(false);
    if (queue is null) return new Context { Failure = LiveApplyStatus.QueueNotFound };
    if (string.IsNullOrEmpty(queue.LinkedTemplateId)) return new Context { Failure = LiveApplyStatus.NoTemplate };
    var template = await _templates.GetAsync(queue.LinkedTemplateId).ConfigureAwait(false);
    if (template is null) return new Context { Failure = LiveApplyStatus.NoTemplate };
    if (_runtime.GetStatus(queueId) != QueueExecutionStatus.Running || !_runs.TryGet(queueId, out var handle) || handle.Schedule is null) {
      return new Context { Failure = LiveApplyStatus.NotRunning };
    }
    return new Context { Handle = handle, Schedule = handle.Schedule, Template = template };
  }

  private async Task<IReadOnlyDictionary<string, string>> SequenceNamesAsync() {
    var all = await _sequences.ListAsync().ConfigureAwait(false);
    return all.ToDictionary(s => s.Id, s => s.Name, StringComparer.Ordinal);
  }

  // The template passed these checks when it was saved. The apply checks the entries again, because a sequence or an
  // image can be gone since then (FR-016).
  private async Task<(List<string> Errors, List<ParameterValidationIssue> Issues)> ValidateAsync(
      IReadOnlyList<QueueTemplateEntry> touched, IReadOnlyDictionary<string, string> sequenceNames, CancellationToken ct) {
    var errors = new List<string>();
    var issues = new List<ParameterValidationIssue>();
    if (touched.Count == 0) return (errors, issues);

    var candidate = new QueueTemplate { Id = "apply", Name = "apply" };
    for (var i = 0; i < touched.Count; i++) {
      var entry = touched[i];
      if (!sequenceNames.ContainsKey(entry.SequenceId)) {
        errors.Add($"The entry {entry.EntryId} uses a sequence that does not exist: '{entry.SequenceId}'.");
        continue;
      }

      var timerError = QueueTemplateRules.ValidateTimerFields(entry.ScheduleType, entry.TimerTimeOfDay.HasValue, entry.TimerRelativeOffset.HasValue);
      if (timerError is not null) errors.Add($"The entry {entry.EntryId} {timerError}.");

      foreach (var bad in QueueTemplateRules.ValidateParameterNames(entry.ParameterValues)) {
        issues.Add(new ParameterValidationIssue(ParameterValidationCodes.InvalidValueName, $"Entry {entry.EntryId}: {bad.Error}.", null, bad.Name, i));
      }
      candidate.Entries.Add(entry);
    }

    if (errors.Count == 0 && issues.Count == 0) {
      issues.AddRange(await QueueTemplatesEndpoints.FindUnknownImageReferencesAsync(candidate, _sequences, _commands, _images, ct).ConfigureAwait(false));
    }
    return (errors, issues);
  }

  [LoggerMessage(EventId = 1136, Level = LogLevel.Information, Message = "Queue {QueueId} got template changes while paused (plan version {PlanVersion}, {Count} differences): {Keys}")]
  private static partial void LogApplied(ILogger logger, string queueId, int planVersion, int count, string keys);
}
