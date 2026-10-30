using GameBot.Domain.QueueTemplates;

namespace GameBot.Domain.Gamepacks.Managed;

/// <summary>A sequence that the new version adds.</summary>
/// <param name="Id">The stored id of the sequence.</param>
/// <param name="Name">The sequence name.</param>
public sealed record NewSequence(string Id, string Name);

/// <summary>
/// Books the entries of the new sequences in a template (feature 148, research R8). Each new entry is disabled, has the
/// default schedule type, and has no timer and no parameter values. The booker never changes an entry that exists.
/// </summary>
public static class TemplateEntryBooker {
  /// <summary>Gives the entries that <see cref="Book"/> adds. The template does not change.</summary>
  /// <param name="template">The template.</param>
  /// <param name="newSequences">The new sequences.</param>
  public static IReadOnlyList<EntryRef> Plan(QueueTemplate template, IEnumerable<NewSequence> newSequences) {
    ArgumentNullException.ThrowIfNull(template);
    ArgumentNullException.ThrowIfNull(newSequences);
    var known = new HashSet<string>(template.Entries.Select(e => e.SequenceId), StringComparer.OrdinalIgnoreCase);
    var result = new List<EntryRef>();
    var position = template.Entries.Count;
    foreach (var sequence in newSequences) {
      if (known.Add(sequence.Id)) {
        result.Add(new EntryRef(template.Name, sequence.Name, position++));
      }
    }

    return result;
  }

  /// <summary>Appends one disabled entry for each new sequence that the template does not refer to yet.</summary>
  /// <param name="template">The template. It changes in memory. The caller saves it.</param>
  /// <param name="newSequences">The new sequences.</param>
  /// <returns>The entries that were added.</returns>
  public static IReadOnlyList<EntryRef> Book(QueueTemplate template, IEnumerable<NewSequence> newSequences) {
    ArgumentNullException.ThrowIfNull(template);
    ArgumentNullException.ThrowIfNull(newSequences);
    var list = newSequences.ToList();
    var planned = Plan(template, list);
    // Feature 171: a template that has entry IDs gets an ID for each new entry. A template that needs an
    // upgrade gets none: the operator upgrades it with the explicit action, never in silence.
    var giveIds = !template.NeedsUpgrade;
    foreach (var entry in planned) {
      var sequence = list.First(s => string.Equals(s.Name, entry.SequenceName, StringComparison.Ordinal));
      template.Entries.Add(new QueueTemplateEntry {
        EntryId = giveIds ? QueueTemplateUpgrade.NewEntryId() : string.Empty,
        SequenceId = sequence.Id,
        Enabled = false
      });
    }

    return planned;
  }

  /// <summary>Finds the entries whose sequence the new version removed. The entries stay in the template.</summary>
  /// <param name="template">The template.</param>
  /// <param name="removedSequences">The removed sequences: id and name.</param>
  public static IReadOnlyList<EntryRef> FindRemovedSequenceEntries(QueueTemplate template, IEnumerable<NewSequence> removedSequences) {
    ArgumentNullException.ThrowIfNull(template);
    ArgumentNullException.ThrowIfNull(removedSequences);
    var removed = removedSequences.ToDictionary(s => s.Id, s => s.Name, StringComparer.OrdinalIgnoreCase);
    var result = new List<EntryRef>();
    for (var i = 0; i < template.Entries.Count; i++) {
      if (removed.TryGetValue(template.Entries[i].SequenceId, out var name)) {
        result.Add(new EntryRef(template.Name, name, i));
      }
    }

    return result;
  }
}
