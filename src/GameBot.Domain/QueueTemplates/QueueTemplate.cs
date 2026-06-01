using System;
using System.Collections.ObjectModel;

namespace GameBot.Domain.QueueTemplates {
  /// <summary>
  /// A named, persisted, ordered list of sequence entries that is independent of any queue,
  /// emulator, or execution status. Unlike a queue's runtime entries (which are not persisted),
  /// a template and its entries are durable and survive service restarts. Templates are the
  /// reusable, shareable persistence of "queue elements".
  /// </summary>
  public class QueueTemplate {
    /// <summary>Stable identifier (GUID "N"); generated on create when absent.</summary>
    public string Id { get; set; } = string.Empty;

    /// <summary>
    /// Human-readable name, unique across templates (case-insensitive). The casing entered by
    /// the operator is preserved for display.
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>Ordered list of sequence entries; may be empty. Order is run order.</summary>
    public Collection<QueueTemplateEntry> Entries { get; init; } = new();

    public DateTimeOffset? CreatedAt { get; set; }

    public DateTimeOffset? UpdatedAt { get; set; }
  }
}
