using System;
using System.Collections.Generic;
using System.Linq;
using GameBot.Domain.QueueTemplates;

namespace GameBot.Service.Services.QueueExecution;

/// <summary>
/// One enabled entry of a run plan: the template entry, its stable <paramref name="Key"/>, and its
/// <paramref name="Position"/> in <see cref="QueueRunPlan.Entries"/> (the index that the cycle ledger shows).
/// </summary>
internal readonly record struct PlanEntry(QueueTemplateEntry Entry, string Key, int Position) {
  /// <summary>Lets the run loop write <c>foreach (var (entry, index) in ...)</c>, as it did before the plan existed.</summary>
  public void Deconstruct(out QueueTemplateEntry entry, out int position) {
    entry = Entry;
    position = Position;
  }
}

/// <summary>
/// The entries that a queue run executes, and the partitions by schedule type that the run loop kept in
/// locals before feature 171. A plan is immutable. A live apply builds a new plan with a higher
/// <see cref="Version"/> and swaps it at a pause point; the run loop reads the new plan on its next iteration.
/// <para>
/// Each entry has a key. The key is the entry ID. An entry of a template that needs an upgrade has no ID, so
/// its key is <c>idx:&lt;position&gt;</c>, where the position counts all entries of the template, also the
/// disabled ones. The consumed-work state of <see cref="QueueRunSchedule"/> uses these keys, so it survives a
/// plan swap.
/// </para>
/// <para>
/// The plan also keeps the disabled entries (<see cref="AllEntries"/>). They do not run, but a live apply needs
/// them: it tells an entry that the operator enables (it was in the template at the start) from a new entry.
/// </para>
/// </summary>
internal sealed class QueueRunPlan {
  /// <summary>The prefix of the key of an entry that has no ID.</summary>
  public const string IndexKeyPrefix = "idx:";

  private readonly Dictionary<string, QueueTemplateEntry> _byKey;
  private readonly Dictionary<string, QueueTemplateEntry> _allByKey;

  private QueueRunPlan(
      IReadOnlyList<QueueTemplateEntry> allEntries,
      IReadOnlyList<string> allKeys,
      IReadOnlyList<QueueTemplateEntry> entries,
      IReadOnlyList<string> keys,
      int version) {
    AllEntries = allEntries;
    AllKeys = allKeys;
    Entries = entries;
    Keys = keys;
    Version = version;
    _byKey = new Dictionary<string, QueueTemplateEntry>(StringComparer.Ordinal);
    for (var i = 0; i < entries.Count; i++) _byKey[keys[i]] = entries[i];
    _allByKey = new Dictionary<string, QueueTemplateEntry>(StringComparer.Ordinal);
    for (var i = 0; i < allEntries.Count; i++) _allByKey[allKeys[i]] = allEntries[i];

    var all = entries.Select((entry, position) => new PlanEntry(entry, keys[position], position)).ToList();
    AtQueueStart = all.Where(x => x.Entry.ScheduleType == ScheduleType.AtQueueStart).Select(x => x.Entry).ToList();
    OncePerRun = all.Where(x => x.Entry.ScheduleType == ScheduleType.OncePerRun).ToList();
    EveryStep = all.Where(x => x.Entry.ScheduleType == ScheduleType.EveryStep).Select(x => x.Entry).ToList();
    BeforeEachRun = all.Where(x => x.Entry.ScheduleType == ScheduleType.BeforeEachRun).Select(x => x.Entry).ToList();
    Timers = all.Where(x => x.Entry.ScheduleType == ScheduleType.Timer).ToList();
  }

  /// <summary>The enabled entries, in run order.</summary>
  public IReadOnlyList<QueueTemplateEntry> Entries { get; }

  /// <summary>The key of each entry of <see cref="Entries"/>, at the same index.</summary>
  public IReadOnlyList<string> Keys { get; }

  /// <summary>All entries of the run, also the disabled ones, in template order.</summary>
  public IReadOnlyList<QueueTemplateEntry> AllEntries { get; }

  /// <summary>The key of each entry of <see cref="AllEntries"/>, at the same index.</summary>
  public IReadOnlyList<string> AllKeys { get; }

  /// <summary>Starts at 0 for the plan of a run. Each swap makes it larger.</summary>
  public int Version { get; }

  /// <summary>Entries that run once, before the loop.</summary>
  public IReadOnlyList<QueueTemplateEntry> AtQueueStart { get; }

  /// <summary>Entries that run once in each cycle.</summary>
  public IReadOnlyList<PlanEntry> OncePerRun { get; }

  /// <summary>Entries that run after each firing.</summary>
  public IReadOnlyList<QueueTemplateEntry> EveryStep { get; }

  /// <summary>Entries that run before each timed, live, or self-rescheduled firing.</summary>
  public IReadOnlyList<QueueTemplateEntry> BeforeEachRun { get; }

  /// <summary>Timer entries (time of day or relative offset).</summary>
  public IReadOnlyList<PlanEntry> Timers { get; }

  /// <summary>True when the run needs its loop for the plan alone (once-per-run, every-step, or timer work).</summary>
  public bool HasLoopWork => OncePerRun.Count > 0 || EveryStep.Count > 0 || Timers.Count > 0;

  /// <summary>True when an entry of the plan has no ID, so its key is an index key (the template needs an upgrade).</summary>
  public bool HasIndexKeys => AllKeys.Any(k => k.StartsWith(IndexKeyPrefix, StringComparison.Ordinal));

  /// <summary>Finds an entry of the plan by its key.</summary>
  public bool TryGetByKey(string key, out QueueTemplateEntry entry) => _byKey.TryGetValue(key, out entry!);

  /// <summary>Finds an entry of the run by its key, also a disabled entry.</summary>
  public bool TryGetFromAllByKey(string key, out QueueTemplateEntry entry) => _allByKey.TryGetValue(key, out entry!);

  /// <summary>
  /// Makes a plan with the same entries in the same order, and the same version, where each key in
  /// <paramref name="renameMap"/> gets its new key. The renamed entry is a copy that has the new ID as its
  /// <see cref="QueueTemplateEntry.EntryId"/>. Used when the upgrade of a template gives IDs to the entries of a
  /// running queue (feature 171).
  /// </summary>
  public QueueRunPlan WithRenamedKeys(IReadOnlyDictionary<string, string> renameMap) {
    ArgumentNullException.ThrowIfNull(renameMap);
    var renamed = new Dictionary<QueueTemplateEntry, QueueTemplateEntry>(ReferenceEqualityComparer.Instance);
    QueueTemplateEntry Rename(QueueTemplateEntry entry, string key) {
      if (!renameMap.TryGetValue(key, out var newId)) return entry;
      if (!renamed.TryGetValue(entry, out var copy)) {
        copy = entry.Clone();
        copy.EntryId = newId;
        renamed[entry] = copy;
      }
      return copy;
    }

    var allEntries = new List<QueueTemplateEntry>(AllEntries.Count);
    var allKeys = new List<string>(AllEntries.Count);
    for (var i = 0; i < AllEntries.Count; i++) {
      allEntries.Add(Rename(AllEntries[i], AllKeys[i]));
      allKeys.Add(renameMap.TryGetValue(AllKeys[i], out var id) ? id : AllKeys[i]);
    }
    var entries = new List<QueueTemplateEntry>(Entries.Count);
    var keys = new List<string>(Entries.Count);
    for (var i = 0; i < Entries.Count; i++) {
      entries.Add(Rename(Entries[i], Keys[i]));
      keys.Add(renameMap.TryGetValue(Keys[i], out var id) ? id : Keys[i]);
    }
    return new QueueRunPlan(allEntries, allKeys, entries, keys, Version);
  }

  /// <summary>The index of this entry object in <see cref="Entries"/>, or -1.</summary>
  public int IndexOf(QueueTemplateEntry entry) {
    for (var i = 0; i < Entries.Count; i++) {
      if (ReferenceEquals(Entries[i], entry)) return i;
    }
    return -1;
  }

  /// <summary>True when an entry with this key is in the plan.</summary>
  public bool ContainsKey(string key) => _byKey.ContainsKey(key);

  /// <summary>The index of the entry with this key in <see cref="Entries"/>, or -1 when it is not in the plan.</summary>
  public int IndexOfKey(string key) {
    for (var i = 0; i < Keys.Count; i++) {
      if (string.Equals(Keys[i], key, StringComparison.Ordinal)) return i;
    }
    return -1;
  }

  /// <summary>
  /// Builds a plan from entries that the caller already chose (no filter on <see cref="QueueTemplateEntry.Enabled"/>).
  /// The key of an entry with no ID uses its position in <paramref name="entries"/>.
  /// </summary>
  public static QueueRunPlan Build(IReadOnlyList<QueueTemplateEntry> entries) {
    ArgumentNullException.ThrowIfNull(entries);
    var list = entries.ToList();
    var keys = list.Select((entry, position) => KeyOf(entry, position)).ToList();
    return new QueueRunPlan(list, keys, list, keys, 0);
  }

  /// <summary>
  /// Builds a plan from the entries of a template, in template order. A disabled entry stays in
  /// <see cref="AllEntries"/> but is left out of <see cref="Entries"/>.
  /// </summary>
  /// <param name="templateEntries">All entries of the template, also the disabled ones.</param>
  /// <param name="version">The version of the new plan.</param>
  public static QueueRunPlan Build(IEnumerable<QueueTemplateEntry> templateEntries, int version) {
    ArgumentNullException.ThrowIfNull(templateEntries);
    var all = templateEntries.ToList();
    var allKeys = all.Select((entry, position) => KeyOf(entry, position)).ToList();
    return FromAll(all, allKeys, version);
  }

  /// <summary>
  /// Builds a plan from all entries of a run with their keys. The enabled entries run. The caller keeps the
  /// order of <paramref name="allEntries"/>.
  /// </summary>
  public static QueueRunPlan FromAll(IReadOnlyList<QueueTemplateEntry> allEntries, IReadOnlyList<string> allKeys, int version) {
    ArgumentNullException.ThrowIfNull(allEntries);
    ArgumentNullException.ThrowIfNull(allKeys);
    if (allEntries.Count != allKeys.Count) throw new ArgumentException("Each entry needs one key.", nameof(allKeys));
    var entries = new List<QueueTemplateEntry>();
    var keys = new List<string>();
    for (var i = 0; i < allEntries.Count; i++) {
      if (!allEntries[i].Enabled) continue;
      entries.Add(allEntries[i]);
      keys.Add(allKeys[i]);
    }
    return new QueueRunPlan(allEntries.ToList(), allKeys.ToList(), entries, keys, version);
  }

  /// <summary>The key of <paramref name="entry"/>, which stands at <paramref name="templatePosition"/> in its template.</summary>
  public static string KeyOf(QueueTemplateEntry entry, int templatePosition) =>
    string.IsNullOrEmpty(entry.EntryId)
      ? IndexKeyPrefix + templatePosition.ToString(System.Globalization.CultureInfo.InvariantCulture)
      : entry.EntryId;
}
