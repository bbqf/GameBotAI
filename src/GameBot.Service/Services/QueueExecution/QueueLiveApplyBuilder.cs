using System;
using System.Collections.Generic;
using System.Linq;
using GameBot.Domain.QueueTemplates;

namespace GameBot.Service.Services.QueueExecution;

/// <summary>The entries that a live apply builds, and what it drops or clears in the consumed schedule state.</summary>
/// <param name="AllEntries">All entries of the new plan, in order.</param>
/// <param name="AllKeys">The key of each entry.</param>
/// <param name="DroppedKeys">Keys of the removed entries.</param>
/// <param name="ClearedKeys">Keys of the entries with a selected schedule change.</param>
/// <param name="TouchedEntries">The entries that the apply added or changed. The service validates them.</param>
internal sealed record LiveApplyEntries(
  IReadOnlyList<QueueTemplateEntry> AllEntries,
  IReadOnlyList<string> AllKeys,
  IReadOnlyList<string> DroppedKeys,
  IReadOnlyList<string> ClearedKeys,
  IReadOnlyList<QueueTemplateEntry> TouchedEntries);

/// <summary>
/// Builds the entries of the new plan of a live apply (feature 171). It is a pure function: it changes neither the plan
/// nor the template. Only the selected differences go in. An entry with no selected difference stays as it is, so its
/// schedule and its consumed state do not change (FR-008).
/// </summary>
internal static class QueueLiveApplyBuilder {
  /// <param name="plan">The plan of the run.</param>
  /// <param name="template">The linked template as it is stored now.</param>
  /// <param name="selected">The keys of the selected differences.</param>
  public static LiveApplyEntries Build(QueueRunPlan plan, QueueTemplate template, ISet<string> selected) {
    ArgumentNullException.ThrowIfNull(plan);
    ArgumentNullException.ThrowIfNull(template);
    ArgumentNullException.ThrowIfNull(selected);
    var targetById = template.Entries.ToDictionary(e => e.EntryId, StringComparer.Ordinal);
    var entries = new List<QueueTemplateEntry>();
    var keys = new List<string>();
    var dropped = new List<string>();
    var cleared = new List<string>();
    var touched = new List<QueueTemplateEntry>();

    for (var i = 0; i < plan.AllEntries.Count; i++) {
      var key = plan.AllKeys[i];
      var live = plan.AllEntries[i];
      if (!targetById.TryGetValue(key, out var target)) {
        if (selected.Contains(QueueTemplateDiff.Key(TemplateDifferenceTypes.Removed, key))) {
          dropped.Add(key);
          continue;
        }
        entries.Add(live);
        keys.Add(key);
        continue;
      }

      var working = live;
      QueueTemplateEntry Edit() => ReferenceEquals(working, live) ? (working = live.Clone()) : working;
      if (selected.Contains(QueueTemplateDiff.Key(TemplateDifferenceTypes.Enabled, key))) Edit().Enabled = true;
      if (selected.Contains(QueueTemplateDiff.Key(TemplateDifferenceTypes.Disabled, key))) Edit().Enabled = false;
      if (selected.Contains(QueueTemplateDiff.Key(TemplateDifferenceTypes.Parameters, key))) {
        var edit = Edit();
        edit.SequenceId = target.SequenceId;
        edit.ParameterValues.Clear();
        foreach (var binding in target.ParameterValues) edit.ParameterValues.Add(binding);
      }
      if (selected.Contains(QueueTemplateDiff.Key(TemplateDifferenceTypes.Schedule, key))) {
        var edit = Edit();
        edit.ScheduleType = target.ScheduleType;
        edit.TimerTimeOfDay = target.TimerTimeOfDay;
        edit.TimerRelativeOffset = target.TimerRelativeOffset;
        cleared.Add(key);
      }

      if (!ReferenceEquals(working, live)) touched.Add(working);
      entries.Add(working);
      keys.Add(key);
    }

    // User Story 4: the entries that the template also has take the order of the template. An entry that the template
    // does not have keeps its slot.
    if (selected.Contains(QueueTemplateDiff.Key(TemplateDifferenceTypes.Order, QueueTemplateDiff.OrderKeyId))) {
      var rank = new Dictionary<string, int>(StringComparer.Ordinal);
      for (var t = 0; t < template.Entries.Count; t++) rank[template.Entries[t].EntryId] = t;
      var byKey = new Dictionary<string, QueueTemplateEntry>(StringComparer.Ordinal);
      for (var i = 0; i < keys.Count; i++) byKey[keys[i]] = entries[i];
      var sorted = keys.Where(rank.ContainsKey).OrderBy(k => rank[k]).ToList();
      var next = 0;
      for (var i = 0; i < keys.Count; i++) {
        if (!rank.ContainsKey(keys[i])) continue;
        keys[i] = sorted[next++];
        entries[i] = byKey[keys[i]];
      }
    }

    // A new entry goes after the nearest entry before it in the template that the new plan holds. This keeps the
    // template order near the new entry.
    for (var t = 0; t < template.Entries.Count; t++) {
      var target = template.Entries[t];
      if (plan.TryGetFromAllByKey(target.EntryId, out _)) continue;
      if (!selected.Contains(QueueTemplateDiff.Key(TemplateDifferenceTypes.Added, target.EntryId))) continue;

      var insertAt = 0;
      for (var before = t - 1; before >= 0; before--) {
        var at = keys.IndexOf(template.Entries[before].EntryId);
        if (at >= 0) {
          insertAt = at + 1;
          break;
        }
      }
      var added = target.Clone();
      entries.Insert(insertAt, added);
      keys.Insert(insertAt, target.EntryId);
      touched.Add(added);
    }

    return new LiveApplyEntries(entries, keys, dropped, cleared, touched);
  }
}
