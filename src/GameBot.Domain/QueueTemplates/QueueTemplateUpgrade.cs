using System;
using System.Linq;

namespace GameBot.Domain.QueueTemplates {
  /// <summary>
  /// The manual upgrade of a template saved before feature 171: it gives a stable
  /// <see cref="QueueTemplateEntry.EntryId"/> to each entry that has none. It changes no other field, and a
  /// repeat changes nothing.
  /// </summary>
  public static class QueueTemplateUpgrade {
    /// <summary>A new entry ID: a GUID in format "N".</summary>
    public static string NewEntryId() => Guid.NewGuid().ToString("N");

    /// <summary>
    /// Gives an ID to each entry of <paramref name="template"/> that has none.
    /// </summary>
    /// <returns>The number of entries that got an ID; 0 when the template is already upgraded.</returns>
    public static int Apply(QueueTemplate template) {
      ArgumentNullException.ThrowIfNull(template);
      var taken = template.Entries
        .Where(e => !string.IsNullOrEmpty(e.EntryId))
        .Select(e => e.EntryId)
        .ToHashSet(StringComparer.OrdinalIgnoreCase);
      var changed = 0;
      foreach (var entry in template.Entries) {
        if (!string.IsNullOrEmpty(entry.EntryId)) continue;
        string id;
        do { id = NewEntryId(); } while (!taken.Add(id));
        entry.EntryId = id;
        changed++;
      }
      return changed;
    }
  }
}
