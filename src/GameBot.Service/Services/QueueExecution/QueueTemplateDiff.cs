using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using GameBot.Domain.QueueTemplates;

namespace GameBot.Service.Services.QueueExecution;

/// <summary>The wire names of the types of a template difference (feature 171).</summary>
internal static class TemplateDifferenceTypes {
  public const string Added = "added";
  public const string Removed = "removed";
  public const string Enabled = "enabled";
  public const string Disabled = "disabled";
  public const string Parameters = "parameters";
  public const string Schedule = "schedule";
  public const string Order = "order";
}

/// <summary>
/// One difference between the entries of a running queue and the entries of its template.
/// </summary>
/// <param name="Key">Stable key of the difference: <c>&lt;type&gt;:&lt;entryId&gt;</c> (<c>order:all</c> for a new order).</param>
/// <param name="Type">One of <see cref="TemplateDifferenceTypes"/>.</param>
/// <param name="EntryId">The entry.</param>
/// <param name="SequenceName">The display name of the sequence of the entry.</param>
/// <param name="Summary">One sentence for the operator.</param>
/// <param name="SelectedByDefault">False for a schedule change: it needs a direct instruction (FR-010).</param>
/// <param name="AffectsSchedule">True when the change starts or moves the schedule of the entry.</param>
internal sealed record TemplateDifference(
  string Key,
  string Type,
  string EntryId,
  string SequenceName,
  string Summary,
  bool SelectedByDefault,
  bool AffectsSchedule);

/// <summary>The differences, the token of the template that they come from, and the upgrade flag.</summary>
/// <param name="NeedsUpgrade">True when the template or the run plan has an entry with no ID. The list is empty then.</param>
/// <param name="BaseToken">Token of the template content. An apply must send it back (FR-019).</param>
/// <param name="Differences">The differences, in template order, then the removals.</param>
internal sealed record TemplateDiffResult(bool NeedsUpgrade, string BaseToken, IReadOnlyList<TemplateDifference> Differences);

/// <summary>
/// Computes the differences between the entries of a run plan and the entries of the linked template
/// (feature 171). Entries match by entry ID, so two entries of one sequence stay separate.
/// </summary>
internal static class QueueTemplateDiff {
  /// <summary>Computes the differences.</summary>
  /// <param name="plan">The plan of the run (all its entries, also the disabled ones).</param>
  /// <param name="template">The linked template as it is stored now.</param>
  /// <param name="sequenceNames">Sequence ID to display name.</param>
  public static TemplateDiffResult Compute(QueueRunPlan plan, QueueTemplate template, IReadOnlyDictionary<string, string> sequenceNames) {
    ArgumentNullException.ThrowIfNull(plan);
    ArgumentNullException.ThrowIfNull(template);
    ArgumentNullException.ThrowIfNull(sequenceNames);
    var token = ComputeBaseToken(template);
    if (template.NeedsUpgrade || plan.HasIndexKeys) {
      return new TemplateDiffResult(true, token, Array.Empty<TemplateDifference>());
    }

    string Name(string sequenceId) => sequenceNames.TryGetValue(sequenceId, out var name) ? name : sequenceId;
    var differences = new List<TemplateDifference>();
    var targetKeys = new HashSet<string>(StringComparer.Ordinal);

    foreach (var target in template.Entries) {
      targetKeys.Add(target.EntryId);
      if (!plan.TryGetFromAllByKey(target.EntryId, out var live)) {
        differences.Add(new TemplateDifference(
          Key(TemplateDifferenceTypes.Added, target.EntryId), TemplateDifferenceTypes.Added, target.EntryId, Name(target.SequenceId),
          $"Add the entry '{Name(target.SequenceId)}' ({ScheduleText(target)}).", true, true));
        continue;
      }

      if (live.Enabled && !target.Enabled) {
        differences.Add(new TemplateDifference(
          Key(TemplateDifferenceTypes.Disabled, target.EntryId), TemplateDifferenceTypes.Disabled, target.EntryId, Name(target.SequenceId),
          $"Disable the entry '{Name(target.SequenceId)}'.", true, false));
      }
      else if (!live.Enabled && target.Enabled) {
        differences.Add(new TemplateDifference(
          Key(TemplateDifferenceTypes.Enabled, target.EntryId), TemplateDifferenceTypes.Enabled, target.EntryId, Name(target.SequenceId),
          $"Enable the entry '{Name(target.SequenceId)}'.", true, true));
      }

      // Feature 171 (FR-008, FR-010): a schedule change is never selected at first. The entry keeps its schedule and its
      // next due time unless the operator selects this difference.
      if (live.ScheduleType != target.ScheduleType
          || live.TimerTimeOfDay != target.TimerTimeOfDay
          || live.TimerRelativeOffset != target.TimerRelativeOffset) {
        differences.Add(new TemplateDifference(
          Key(TemplateDifferenceTypes.Schedule, target.EntryId), TemplateDifferenceTypes.Schedule, target.EntryId, Name(target.SequenceId),
          $"Change the schedule of the entry '{Name(target.SequenceId)}' from {ScheduleText(live)} to {ScheduleText(target)}.", false, true));
      }

      var sequenceChanged = !string.Equals(live.SequenceId, target.SequenceId, StringComparison.Ordinal);
      if (sequenceChanged || !SameValues(live, target)) {
        var summary = sequenceChanged
          ? $"Change the entry '{Name(live.SequenceId)}' to run the sequence '{Name(target.SequenceId)}'."
          : $"Change the parameter values of the entry '{Name(target.SequenceId)}'.";
        differences.Add(new TemplateDifference(
          Key(TemplateDifferenceTypes.Parameters, target.EntryId), TemplateDifferenceTypes.Parameters, target.EntryId, Name(target.SequenceId),
          summary, true, false));
      }
    }

    for (var i = 0; i < plan.AllEntries.Count; i++) {
      var key = plan.AllKeys[i];
      if (targetKeys.Contains(key)) continue;
      var live = plan.AllEntries[i];
      differences.Add(new TemplateDifference(
        Key(TemplateDifferenceTypes.Removed, key), TemplateDifferenceTypes.Removed, key, Name(live.SequenceId),
        $"Remove the entry '{Name(live.SequenceId)}'.", true, false));
    }

    // User Story 4: one difference for a new order. It compares only the entries that both sides have, so an added or a
    // removed entry alone is not an order change. State follows the entry ID, so an order change moves no timer.
    var commonInRun = plan.AllKeys.Where(targetKeys.Contains).ToList();
    var commonInTemplate = template.Entries.Select(e => e.EntryId).Where(id => plan.TryGetFromAllByKey(id, out _)).ToList();
    if (!commonInRun.SequenceEqual(commonInTemplate, StringComparer.Ordinal)) {
      differences.Add(new TemplateDifference(
        Key(TemplateDifferenceTypes.Order, OrderKeyId), TemplateDifferenceTypes.Order, string.Empty, string.Empty,
        "Put the entries in the order of the template. No schedule changes.", true, false));
    }

    return new TemplateDiffResult(false, token, differences);
  }

  /// <summary>The entry part of the key of the order difference: <c>order:all</c>.</summary>
  public const string OrderKeyId = "all";

  /// <summary>
  /// The token of the content of a template: the name and the entries, with no time stamp. A save that changes
  /// nothing gives the same token.
  /// </summary>
  public static string ComputeBaseToken(QueueTemplate template) {
    ArgumentNullException.ThrowIfNull(template);
    var json = JsonSerializer.Serialize(new { template.Name, template.Entries });
    var hash = SHA256.HashData(Encoding.UTF8.GetBytes(json));
    return "sha256:" + Convert.ToHexString(hash).ToLowerInvariant();
  }

  /// <summary>The key of a difference.</summary>
  public static string Key(string type, string entryId) => type + ":" + entryId;

  private static bool SameValues(QueueTemplateEntry a, QueueTemplateEntry b) {
    if (a.ParameterValues.Count != b.ParameterValues.Count) return false;
    for (var i = 0; i < a.ParameterValues.Count; i++) {
      if (!string.Equals(a.ParameterValues[i].Name, b.ParameterValues[i].Name, StringComparison.Ordinal)
          || !string.Equals(a.ParameterValues[i].Value, b.ParameterValues[i].Value, StringComparison.Ordinal)) {
        return false;
      }
    }
    return true;
  }

  /// <summary>A short text of the schedule of an entry, for a summary.</summary>
  internal static string ScheduleText(QueueTemplateEntry entry) => entry.ScheduleType switch {
    ScheduleType.Timer when entry.TimerTimeOfDay is { } tod => $"at {tod:HH:mm}",
    ScheduleType.Timer when entry.TimerRelativeOffset is { } offset => $"{offset:hh\\:mm\\:ss} after the start",
    ScheduleType.Timer => "timer",
    ScheduleType.EveryStep => "after every step",
    ScheduleType.AtQueueStart => "at queue start",
    ScheduleType.BeforeEachRun => "before each run",
    _ => "once per run"
  };
}
