using System;
using System.Collections.Generic;
using System.Linq;
using GameBot.Domain.Queues;
using GameBot.Domain.QueueTemplates;

namespace GameBot.Service.Services.QueueExecution;

/// <summary>One entry of a running queue that matches a template entry by position and sequence.</summary>
internal sealed record UpgradeMatch(string QueueId, int Position, string SequenceId);

/// <summary>An entry of a running queue that the upgrade cannot match to a template entry.</summary>
internal sealed record UpgradeUnmatched(string QueueId, int Position, string? RunningSequenceId, string? TemplateSequenceId, string Reason);

/// <summary>A running queue that uses the template, and the match of its entries.</summary>
internal sealed class RunningQueueUpgrade {
  public RunningQueueUpgrade(string queueId, QueueRunSchedule? schedule) {
    QueueId = queueId;
    Schedule = schedule;
  }

  public string QueueId { get; }
  public QueueRunSchedule? Schedule { get; }
  public List<UpgradeMatch> Matches { get; } = new();
  public List<UpgradeUnmatched> Unmatched { get; } = new();
}

/// <summary>
/// The part of the template upgrade (feature 171, FR-027) that concerns running queues. It matches the entries
/// of each running queue that uses the template to the template entries, by position and sequence. After the
/// template has its IDs, it gives the same IDs to the runtime entries and to the run plan, and it moves the
/// consumed schedule state to the new keys in the same step. No schedule and no next due time changes.
/// </summary>
internal static class QueueTemplateUpgradeRunner {
  /// <summary>Matches the entries of each running queue that uses <paramref name="template"/>.</summary>
  public static IReadOnlyList<RunningQueueUpgrade> Plan(
      QueueTemplate template,
      IEnumerable<ExecutionQueue> queues,
      IQueueRunRegistry runs,
      IQueueRuntimeStore runtime) {
    ArgumentNullException.ThrowIfNull(template);
    ArgumentNullException.ThrowIfNull(queues);
    ArgumentNullException.ThrowIfNull(runs);
    ArgumentNullException.ThrowIfNull(runtime);
    var result = new List<RunningQueueUpgrade>();
    foreach (var queue in queues) {
      if (!string.Equals(queue.LinkedTemplateId, template.Id, StringComparison.Ordinal)) continue;
      if (!runs.TryGet(queue.Id, out var handle)) continue;

      var item = new RunningQueueUpgrade(queue.Id, handle.Schedule);
      if (item.Schedule is null) {
        item.Unmatched.Add(new UpgradeUnmatched(queue.Id, 0, null, null, "The run is starting. Try again in a moment."));
        result.Add(item);
        continue;
      }

      MatchRuntimeEntries(item, template, runtime.GetEntries(queue.Id));
      MatchPlanEntries(item, template, item.Schedule.Plan);
      result.Add(item);
    }
    return result;
  }

  private static void MatchRuntimeEntries(RunningQueueUpgrade item, QueueTemplate template, IReadOnlyList<QueueEntry> running) {
    var count = Math.Max(running.Count, template.Entries.Count);
    for (var p = 0; p < count; p++) {
      var runningSequence = p < running.Count ? running[p].SequenceId : null;
      var templateSequence = p < template.Entries.Count ? template.Entries[p].SequenceId : null;
      if (runningSequence is not null && string.Equals(runningSequence, templateSequence, StringComparison.Ordinal)) {
        item.Matches.Add(new UpgradeMatch(item.QueueId, p, runningSequence));
      }
      else {
        var reason = runningSequence is null
          ? "The template has an entry that the running queue does not have."
          : templateSequence is null
            ? "The running queue has an entry that the template does not have."
            : "The sequence of the running entry is not the sequence of the template entry.";
        item.Unmatched.Add(new UpgradeUnmatched(item.QueueId, p, runningSequence, templateSequence, reason));
      }
    }
  }

  // The run plan holds the entries that the run really executes. An entry with an index key must still stand at
  // the same position in the template with the same sequence, or the IDs would go to the wrong entries.
  private static void MatchPlanEntries(RunningQueueUpgrade item, QueueTemplate template, QueueRunPlan plan) {
    for (var i = 0; i < plan.AllKeys.Count; i++) {
      if (!TryParseIndexKey(plan.AllKeys[i], out var position)) continue;
      var runSequence = plan.AllEntries[i].SequenceId;
      var templateSequence = position < template.Entries.Count ? template.Entries[position].SequenceId : null;
      if (!string.Equals(runSequence, templateSequence, StringComparison.Ordinal)
          && !item.Unmatched.Any(u => u.Position == position)) {
        item.Unmatched.Add(new UpgradeUnmatched(item.QueueId, position, runSequence, templateSequence,
          "The run uses a sequence at this position that the template does not have now."));
      }
    }
  }

  /// <summary>
  /// Gives the new IDs to a running queue. Call it after the template has its IDs and is saved. It replaces the
  /// runtime entries and the run plan, and it moves the consumed schedule state to the new keys.
  /// </summary>
  /// <returns>The number of entries of the run plan that got a new key.</returns>
  public static int Apply(RunningQueueUpgrade item, QueueTemplate upgraded, IQueueRuntimeStore runtime) {
    ArgumentNullException.ThrowIfNull(item);
    ArgumentNullException.ThrowIfNull(upgraded);
    ArgumentNullException.ThrowIfNull(runtime);
    if (item.Schedule is null) return 0;

    var renameMap = new Dictionary<string, string>(StringComparer.Ordinal);
    var plan = item.Schedule.Plan;
    foreach (var key in plan.AllKeys) {
      if (!TryParseIndexKey(key, out var position)) continue;
      if (position >= upgraded.Entries.Count) continue;
      var newId = upgraded.Entries[position].EntryId;
      if (!string.IsNullOrEmpty(newId)) renameMap[key] = newId;
    }

    runtime.ReplaceEntries(item.QueueId, upgraded.Entries.Select(e => new QueueEntry { EntryId = e.EntryId, SequenceId = e.SequenceId }));
    if (renameMap.Count == 0) return 0;
    item.Schedule.SwapPlan(plan.WithRenamedKeys(renameMap), renameMap: renameMap);
    return renameMap.Count;
  }

  private static bool TryParseIndexKey(string key, out int position) {
    position = -1;
    return key.StartsWith(QueueRunPlan.IndexKeyPrefix, StringComparison.Ordinal)
      && int.TryParse(key.AsSpan(QueueRunPlan.IndexKeyPrefix.Length), System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out position);
  }
}
